using System.IO;
using CellTool.Models;

namespace CellTool.Services;

public class AnalysisEngine
{
    private readonly VoltageFileReader fileReader = new();
    private readonly CodewordAnalyzer codewordAnalyzer = new();

    /// <summary>
    /// Runs the NAND voltage scan analysis pipeline.
    /// </summary>
    public Task<AnalysisResult> RunAsync(
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        IProgress<(double progress, string message)>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => RunCore(config, chip, groupModel, progress, ct), ct);
    }

    private AnalysisResult RunCore(
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        IProgress<(double progress, string message)>? progress,
        CancellationToken ct)
    {
        ValidateConfig(config, chip, groupModel);

        progress?.Report((0.03, "Scanning voltage code files..."));
        var scanResult = fileReader.ScanDirectoryDetailed(
            config.InputDirectory,
            config.VoltageMinMv,
            config.VoltageMaxMv,
            config.VoltageStepMv);
        var files = scanResult.Files;

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"No voltage scan files matched. Directory='{config.InputDirectory}', " +
                $"candidates={scanResult.TotalCandidateFiles}, nameMismatch={scanResult.NameMismatchFiles}, " +
                $"rangeFiltered={scanResult.RangeFilteredFiles}, stepFiltered={scanResult.StepFilteredFiles}, " +
                $"range={config.VoltageMinCode}..{config.VoltageMaxCode} code, step={config.VoltageStepCode} code. " +
                "Expected file names like '0', '10', or '-127'; '.bin' suffix is also supported. One code equals 10mV.");
        }

        int voltageCount = files.Count;
        int wlCount = config.WlCount;
        int pageTotalBytes = chip.PageTotalBytes;
        int cellCount = pageTotalBytes * 8;
        int totalCells = wlCount * cellCount;

        var modeEncodings = ParseModeEncodings(config, chip);
        var statesPerVoltage = new int[voltageCount][];
        var rawGrayPerVoltage = new int[voltageCount][];

        for (int v = 0; v < voltageCount; v++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((0.05 + ((double)v / voltageCount * 0.55),
                $"Reading {files[v]} ({v + 1}/{voltageCount})..."));

            var allCells = new int[totalCells];
            var allRawGrayCells = new int[totalCells];

            for (int wl = 0; wl < wlCount; wl++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = groupModel.Entries[wl];
                int bitsPerCell = entry.PageIndices.Length;
                var encoding = modeEncodings[bitsPerCell];
                var grayDecoder = new GrayCodeDecoder(encoding, bitsPerCell, config.GrayCodeOrder);
                var wlData = fileReader.ReadWlBytes(files[v].FilePath, entry, pageTotalBytes, config.StartPage);
                var states = grayDecoder.DecodeWl(wlData, pageTotalBytes);
                var rawGrayCodes = grayDecoder.DecodeRawGrayWl(wlData, pageTotalBytes);
                Array.Copy(states, 0, allCells, wl * cellCount, cellCount);
                Array.Copy(rawGrayCodes, 0, allRawGrayCells, wl * cellCount, cellCount);
            }

            statesPerVoltage[v] = allCells;
            rawGrayPerVoltage[v] = allRawGrayCells;
        }

        progress?.Report((0.65, "Computing ground truth via majority vote..."));
        var groundTruth = ComputeGroundTruth(statesPerVoltage, totalCells, chip.StateCount);

        progress?.Report((0.72, "Computing total raw Gray change distribution..."));
        var voltageCodes = files.Select(f => (double)f.Code).ToArray();
        var sourceRawGray = !string.IsNullOrWhiteSpace(config.ReferenceFilePath)
            ? ReadRawGrayBaseline(config.ReferenceFilePath, config, chip, groupModel, modeEncodings, wlCount, pageTotalBytes, cellCount, totalCells)
            : rawGrayPerVoltage[0];
        var totalIncrements = ComputeFirstStableRawGrayFlipIncrements(
            rawGrayPerVoltage,
            sourceRawGray,
            totalCells,
            voltageCount,
            stableWindow: 2);
        var increments = new[] { totalIncrements };
        var transitionLabels = new[] { "Total Gray changes" };
        var statePeaks = Array.Empty<StatePeakInfo>();
        var bestVoltages = new Dictionary<int, double[]>();

        progress?.Report((0.91, "Analyzing codeword errors..."));
        var bestVoltageIdx = FindNearestVoltageIndex(voltageCodes, MedianBestVoltage(bestVoltages));
        var zeroVoltageIdx = Array.FindIndex(files.ToArray(), f => f.Code == 0);

        CodewordErrorStat? bestErrors = null;
        CodewordErrorStat? zeroErrors = null;

        if (!string.IsNullOrWhiteSpace(config.ReferenceFilePath))
        {
            var referenceData = ReadReferenceBytes(config.ReferenceFilePath, config, chip, groupModel, wlCount);
            int codewordBytes = chip.CodewordBytes;

            if (bestVoltageIdx >= 0)
            {
                var testData = ReadSelectedBytes(files[bestVoltageIdx].FilePath, config, chip, groupModel, wlCount);
                bestErrors = codewordAnalyzer.Analyze(referenceData, testData, codewordBytes);
            }

            if (zeroVoltageIdx >= 0)
            {
                var testData = ReadSelectedBytes(files[zeroVoltageIdx].FilePath, config, chip, groupModel, wlCount);
                zeroErrors = codewordAnalyzer.Analyze(referenceData, testData, codewordBytes);
            }
        }

        progress?.Report((1.0, "Analysis complete."));

        return new AnalysisResult
        {
            StatePeaks = statePeaks,
            BestReadVoltages = bestVoltages,
            BestVoltageErrors = bestErrors,
            ZeroOffsetErrors = zeroErrors,
            IncrementCurves = increments,
            VoltageCodes = voltageCodes,
            TransitionLabels = transitionLabels,
            GroundTruth = groundTruth,
            TotalCells = totalCells,
            VoltageCount = voltageCount,
            StateCount = chip.StateCount
        };
    }

    private static void ValidateConfig(AnalysisConfig config, ChipInfo chip, GroupModel groupModel)
    {
        if (string.IsNullOrWhiteSpace(config.InputDirectory))
            throw new InvalidDataException("Input directory is required.");
        if (string.IsNullOrWhiteSpace(config.OutputDirectory))
            throw new InvalidDataException("Output directory is required.");
        if (config.VoltageMinCode > config.VoltageMaxCode)
            throw new InvalidDataException("Voltage code minimum must be less than or equal to voltage code maximum.");
        if (config.VoltageStepCode <= 0)
            throw new InvalidDataException("Voltage code step must be positive.");
        if (config.WlCount <= 0)
            throw new InvalidDataException("WL count must be positive.");
        if (config.WlCount > groupModel.WlCount)
            throw new InvalidDataException($"Configured WL count {config.WlCount} exceeds GroupModel rows {groupModel.WlCount}.");
        if (chip.FrameCount <= 0)
            throw new InvalidDataException("Frame count must be positive.");
        if (chip.PageTotalBytes % chip.FrameCount != 0)
            throw new InvalidDataException(
                $"Page size {chip.PageTotalBytes} is not divisible by frame count {chip.FrameCount}.");
        var modeEncodings = ParseModeEncodings(config, chip);

        for (int wl = 0; wl < config.WlCount; wl++)
        {
            int bitsPerCell = groupModel.Entries[wl].PageIndices.Length;
            if (bitsPerCell < 1 || bitsPerCell > 4)
            {
                throw new InvalidDataException(
                    $"WL {wl}: expected 1 to 4 valid page indices for SLC/MLC/TLC/QLC mode, got {bitsPerCell}.");
            }
            if (modeEncodings[bitsPerCell].Length == 0)
            {
                throw new InvalidDataException(
                    $"WL {wl}: {ModeName(bitsPerCell)} mode is required by GroupModel, but its WL encoding is empty.");
            }
        }
    }

    public static int[] ComputeGroundTruth(int[][] statesPerVoltage, int totalCells, int stateCount)
    {
        var groundTruth = new int[totalCells];

        for (int c = 0; c < totalCells; c++)
        {
            var votes = new int[stateCount];
            for (int v = 0; v < statesPerVoltage.Length; v++)
                votes[statesPerVoltage[v][c]]++;

            int maxVotes = -1;
            int bestState = 0;
            for (int s = 0; s < stateCount; s++)
            {
                if (votes[s] >= maxVotes)
                {
                    maxVotes = votes[s];
                    bestState = s;
                }
            }

            groundTruth[c] = bestState;
        }

        return groundTruth;
    }

    public static double[][] ComputeIncrements(int[][] statesPerVoltage, int totalCells, int stateCount, int voltageCount)
    {
        var increments = new double[stateCount][];
        for (int s = 0; s < stateCount; s++)
            increments[s] = new double[voltageCount];

        for (int v = 1; v < voltageCount; v++)
        {
            for (int c = 0; c < totalCells; c++)
            {
                int prev = statesPerVoltage[v - 1][c];
                int curr = statesPerVoltage[v][c];
                if (prev != curr)
                    increments[curr][v]++;
            }
        }

        return increments;
    }

    public static double[][] ComputeRawGrayTransitionIncrements(
        int[][] rawGrayPerVoltage,
        int[] baselineRawGrayStates,
        int totalCells,
        int[] wlEncoding,
        int voltageCount)
    {
        var transitions = BuildTransitionPairs(wlEncoding);
        var increments = new double[transitions.Length][];
        for (int t = 0; t < transitions.Length; t++)
            increments[t] = new double[voltageCount];

        for (int v = 0; v < voltageCount; v++)
        {
            var previousStates = v == 0 ? baselineRawGrayStates : rawGrayPerVoltage[v - 1];
            var currentStates = rawGrayPerVoltage[v];

            for (int c = 0; c < totalCells; c++)
            {
                int previous = previousStates[c];
                int current = currentStates[c];

                for (int t = 0; t < transitions.Length; t++)
                {
                    if (previous == transitions[t].Left && current == transitions[t].Right)
                    {
                        increments[t][v]++;
                        break;
                    }
                }
            }
        }

        return increments;
    }

    public static double[] ComputeRawGrayChangeIncrements(
        int[][] rawGrayPerVoltage,
        int[] baselineRawGrayStates,
        int totalCells,
        int voltageCount)
    {
        var increments = new double[voltageCount];

        for (int v = 0; v < voltageCount; v++)
        {
            var previousStates = v == 0 ? baselineRawGrayStates : rawGrayPerVoltage[v - 1];
            var currentStates = rawGrayPerVoltage[v];

            for (int c = 0; c < totalCells; c++)
            {
                if (previousStates[c] != currentStates[c])
                    increments[v]++;
            }
        }

        return increments;
    }

    public static double[] ComputeFirstStableRawGrayFlipIncrements(
        int[][] rawGrayPerVoltage,
        int[] baselineRawGrayStates,
        int totalCells,
        int voltageCount,
        int stableWindow = 2)
    {
        var increments = new double[voltageCount];
        int requiredStableWindow = Math.Max(1, stableWindow);

        for (int c = 0; c < totalCells; c++)
        {
            int baseline = baselineRawGrayStates[c];

            for (int v = 0; v < voltageCount; v++)
            {
                int current = rawGrayPerVoltage[v][c];
                if (current == baseline)
                    continue;

                if (IsStableFrom(rawGrayPerVoltage, c, v, current, requiredStableWindow))
                {
                    increments[v]++;
                    break;
                }
            }
        }

        return increments;
    }

    private static bool IsStableFrom(
        int[][] rawGrayPerVoltage,
        int cell,
        int startVoltage,
        int rawGray,
        int stableWindow)
    {
        int endVoltage = Math.Min(rawGrayPerVoltage.Length, startVoltage + stableWindow);
        for (int v = startVoltage; v < endVoltage; v++)
        {
            if (rawGrayPerVoltage[v][cell] != rawGray)
                return false;
        }

        return true;
    }

    public static string[] BuildTransitionLabels(int[] wlEncoding)
    {
        return BuildTransitionPairs(wlEncoding)
            .Select(pair => $"Gray {pair.Left}->{pair.Right}")
            .ToArray();
    }

    public static Dictionary<int, double[]> FindBestReadVoltages(
        int[][] statesPerVoltage,
        int[] groundTruth,
        double[] voltages,
        StatePeakInfo[] statePeaks,
        int stateCount,
        int wlCount,
        int cellCount)
    {
        var result = new Dictionary<int, double[]>();

        for (int wl = 0; wl < wlCount; wl++)
        {
            var startCell = wl * cellCount;
            var bestVoltages = new double[stateCount - 1];

            for (int pair = 0; pair < stateCount - 1; pair++)
            {
                double currentPeak = pair < statePeaks.Length ? statePeaks[pair].PeakCode : voltages[0];
                double minVoltage = voltages.Min();
                double maxVoltage = voltages.Max();

                if (pair > 0 && pair - 1 < statePeaks.Length)
                    minVoltage = Math.Min(currentPeak, (statePeaks[pair - 1].PeakCode + currentPeak) / 2.0);

                if (pair + 1 < statePeaks.Length)
                    maxVoltage = Math.Max(currentPeak, (currentPeak + statePeaks[pair + 1].PeakCode) / 2.0);

                double midpoint = currentPeak;

                int bestIdx = -1;
                int bestErrors = int.MaxValue;
                double bestDistance = double.MaxValue;

                for (int v = 0; v < voltages.Length; v++)
                {
                    if (voltages[v] < minVoltage || voltages[v] > maxVoltage)
                        continue;

                    int errors = CountBoundaryErrors(statesPerVoltage[v], groundTruth, startCell, cellCount, pair);
                    double distance = Math.Abs(voltages[v] - midpoint);

                    if (errors < bestErrors || (errors == bestErrors && distance < bestDistance))
                    {
                        bestErrors = errors;
                        bestDistance = distance;
                        bestIdx = v;
                    }
                }

                if (bestIdx < 0)
                {
                    for (int v = 0; v < voltages.Length; v++)
                    {
                        int errors = CountBoundaryErrors(statesPerVoltage[v], groundTruth, startCell, cellCount, pair);
                        double distance = Math.Abs(voltages[v] - midpoint);

                        if (errors < bestErrors || (errors == bestErrors && distance < bestDistance))
                        {
                            bestErrors = errors;
                            bestDistance = distance;
                            bestIdx = v;
                        }
                    }
                }

                bestVoltages[pair] = voltages[bestIdx];
            }

            result[wl] = bestVoltages;
        }

        return result;
    }

    private static int CountBoundaryErrors(
        int[] readStates,
        int[] groundTruth,
        int startCell,
        int cellCount,
        int pair)
    {
        int errors = 0;
        int endCell = startCell + cellCount;

        for (int c = startCell; c < endCell; c++)
        {
            bool truthLeft = groundTruth[c] <= pair;
            bool readLeft = readStates[c] <= pair;
            if (truthLeft != readLeft)
                errors++;
        }

        return errors;
    }

    private static int FindNearestVoltageIndex(double[] voltages, double target)
    {
        if (voltages.Length == 0 || double.IsNaN(target))
            return -1;

        int bestIdx = 0;
        double bestDistance = Math.Abs(voltages[0] - target);
        for (int i = 1; i < voltages.Length; i++)
        {
            double distance = Math.Abs(voltages[i] - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    private static double MedianBestVoltage(Dictionary<int, double[]> bestVoltages)
    {
        var values = bestVoltages.Values
            .SelectMany(v => v)
            .OrderBy(v => v)
            .ToArray();

        if (values.Length == 0)
            return double.NaN;

        int mid = values.Length / 2;
        return values.Length % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2.0;
    }

    private static Dictionary<int, int[]> ParseModeEncodings(AnalysisConfig config, ChipInfo chip)
    {
        var encodings = new Dictionary<int, int[]>();

        encodings[1] = ParseModeEncoding(config.SlcWlEncoding, 1, nameof(config.SlcWlEncoding));
        encodings[2] = ParseModeEncoding(config.MlcWlEncoding, 2, nameof(config.MlcWlEncoding));
        encodings[3] = ParseModeEncoding(config.TlcWlEncoding, 3, nameof(config.TlcWlEncoding));
        encodings[4] = ParseModeEncoding(config.QlcWlEncoding, 4, nameof(config.QlcWlEncoding));

        int chipBits = chip.BitsPerCell;
        if (chipBits >= 1 && chipBits <= 4 && encodings[chipBits].Length == 0)
            encodings[chipBits] = ValidateModeEncoding(chip.WlEncoding, chipBits, $"{chip.Type} WL encoding from chip database");

        return encodings;
    }

    private static int[] ParseModeEncoding(string text, int bitsPerCell, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var encoding = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out encoding[i]))
                throw new InvalidDataException($"{fieldName}: cannot parse '{parts[i]}' as a raw Gray code.");
        }

        return ValidateModeEncoding(encoding, bitsPerCell, fieldName);
    }

    private static int[] ValidateModeEncoding(int[] encoding, int bitsPerCell, string fieldName)
    {
        int expectedLength = 1 << bitsPerCell;
        if (encoding.Length != expectedLength)
        {
            if (encoding.Length == 0)
                return encoding;

            throw new InvalidDataException(
                $"{fieldName}: expected {expectedLength} raw Gray codes for {ModeName(bitsPerCell)}, got {encoding.Length}.");
        }

        var seen = new HashSet<int>();
        int maxValue = expectedLength - 1;
        foreach (var rawGray in encoding)
        {
            if (rawGray < 0 || rawGray > maxValue)
                throw new InvalidDataException($"{fieldName}: raw Gray code {rawGray} is outside 0..{maxValue}.");
            if (!seen.Add(rawGray))
                throw new InvalidDataException($"{fieldName}: duplicate raw Gray code {rawGray}.");
        }

        return encoding;
    }

    private static string ModeName(int bitsPerCell) => bitsPerCell switch
    {
        1 => "SLC",
        2 => "MLC",
        3 => "TLC",
        4 => "QLC",
        _ => $"{bitsPerCell}-bit/cell"
    };

    private static (int Left, int Right)[] BuildTransitionPairs(int[] wlEncoding)
    {
        if (wlEncoding.Length < 2)
            return Array.Empty<(int Left, int Right)>();

        var transitions = new (int Left, int Right)[wlEncoding.Length - 1];
        for (int i = 0; i < transitions.Length; i++)
            transitions[i] = (wlEncoding[i], wlEncoding[i + 1]);

        return transitions;
    }

    private byte[] ReadReferenceBytes(
        string referenceFilePath,
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        int wlCount)
    {
        var expectedBytes = groupModel.Entries
            .Take(wlCount)
            .Sum(entry => entry.PageIndices.Length * chip.PageTotalBytes);
        var fileInfo = new FileInfo(referenceFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Reference file not found: {referenceFilePath}", referenceFilePath);

        if (fileInfo.Length == expectedBytes)
            return File.ReadAllBytes(referenceFilePath);

        return ReadSelectedBytes(referenceFilePath, config, chip, groupModel, wlCount);
    }

    private int[] ReadRawGrayBaseline(
        string baselineFilePath,
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        Dictionary<int, int[]> modeEncodings,
        int wlCount,
        int pageTotalBytes,
        int cellCount,
        int totalCells)
    {
        var fileInfo = new FileInfo(baselineFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Baseline file not found: {baselineFilePath}", baselineFilePath);

        var baseline = new int[totalCells];
        long selectedByteCount = groupModel.Entries
            .Take(wlCount)
            .Sum(entry => (long)entry.PageIndices.Length * pageTotalBytes);

        if (fileInfo.Length == selectedByteCount)
        {
            var selectedData = File.ReadAllBytes(baselineFilePath);
            int offset = 0;

            for (int wl = 0; wl < wlCount; wl++)
            {
                var entry = groupModel.Entries[wl];
                int bitsPerCell = entry.PageIndices.Length;
                int wlBytes = bitsPerCell * pageTotalBytes;
                var wlData = new byte[wlBytes];
                Array.Copy(selectedData, offset, wlData, 0, wlBytes);
                offset += wlBytes;

                var decoder = new GrayCodeDecoder(modeEncodings[bitsPerCell], bitsPerCell, config.GrayCodeOrder);
                var rawGray = decoder.DecodeRawGrayWl(wlData, pageTotalBytes);
                Array.Copy(rawGray, 0, baseline, wl * cellCount, cellCount);
            }

            return baseline;
        }

        for (int wl = 0; wl < wlCount; wl++)
        {
            var entry = groupModel.Entries[wl];
            int bitsPerCell = entry.PageIndices.Length;
            var decoder = new GrayCodeDecoder(modeEncodings[bitsPerCell], bitsPerCell, config.GrayCodeOrder);
            var wlData = fileReader.ReadWlBytes(baselineFilePath, entry, pageTotalBytes, config.StartPage);
            var rawGray = decoder.DecodeRawGrayWl(wlData, pageTotalBytes);
            Array.Copy(rawGray, 0, baseline, wl * cellCount, cellCount);
        }

        return baseline;
    }

    private byte[] ReadSelectedBytes(
        string filePath,
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        int wlCount)
    {
        var wlOffsets = new int[wlCount];
        int totalBytes = 0;
        for (int wl = 0; wl < wlCount; wl++)
        {
            wlOffsets[wl] = totalBytes;
            totalBytes += groupModel.Entries[wl].PageIndices.Length * chip.PageTotalBytes;
        }

        var data = new byte[totalBytes];

        for (int wl = 0; wl < wlCount; wl++)
        {
            var wlData = fileReader.ReadWlBytes(
                filePath,
                groupModel.Entries[wl],
                chip.PageTotalBytes,
                config.StartPage);

            Array.Copy(wlData, 0, data, wlOffsets[wl], wlData.Length);
        }

        return data;
    }
}
