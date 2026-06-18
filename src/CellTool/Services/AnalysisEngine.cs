using System.Collections.Concurrent;
using CellTool.Models;

namespace CellTool.Services;

public class AnalysisEngine
{
    private readonly VoltageFileReader _fileReader = new();
    private readonly ExcelParser _excelParser = new();
    private readonly GroupModelParser _groupModelParser = new();
    private readonly PeakAnalyzer _peakAnalyzer = new();
    private readonly CodewordAnalyzer _codewordAnalyzer = new();

    public async Task<AnalysisResult> RunAsync(
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        IProgress<(double progress, string message)>? progress = null,
        CancellationToken ct = default)
    {
        // 1. Scan voltage files
        progress?.Report((0.05, "Scanning voltage files..."));
        var files = _fileReader.ScanDirectory(config.InputDirectory);
        int voltageCount = files.Count;

        // 2. Read all WL data for all voltages
        int wlCount = Math.Min(config.WlCount, groupModel.WlCount);
        int pageTotalBytes = chip.PageTotalBytes;
        int cellCount = pageTotalBytes * 8;
        int totalCells = wlCount * cellCount;

        var grayDecoder = new GrayCodeDecoder(chip.WlEncoding, chip.BitsPerCell);

        // statesPerVoltage[v][cellIndex] = physical state
        var statesPerVoltage = new int[voltageCount][];

        for (int v = 0; v < voltageCount; v++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(((double)v / voltageCount * 0.4,
                $"Reading {files[v]} ({v + 1}/{voltageCount})..."));

            var allCells = new int[totalCells];

            for (int wl = 0; wl < wlCount; wl++)
            {
                var entry = groupModel.Entries[wl];
                var wlData = _fileReader.ReadWlBytes(files[v].FilePath, entry, pageTotalBytes);
                var states = grayDecoder.DecodeWl(wlData, pageTotalBytes);
                Array.Copy(states, 0, allCells, wl * cellCount, cellCount);
            }

            statesPerVoltage[v] = allCells;
        }

        // 3. Majority vote → GroundTruth
        progress?.Report((0.70, "Computing ground truth via majority vote..."));
        var groundTruth = ComputeGroundTruth(statesPerVoltage, totalCells, chip.StateCount);

        // 4. Increment curves
        progress?.Report((0.75, "Computing increment distributions..."));
        var voltages = files.Select(f => (double)f.OffsetMv).ToArray();
        var increments = ComputeIncrements(statesPerVoltage, totalCells, chip.StateCount, voltageCount);

        // 5. Peak analysis
        progress?.Report((0.80, "Finding peaks and boundaries..."));
        var statePeaks = _peakAnalyzer.Analyze(increments, voltages, totalCells, chip.StateCount);

        // 6. Best read voltages
        progress?.Report((0.85, "Searching optimal read voltages..."));
        var bestVoltages = FindBestReadVoltages(statesPerVoltage, groundTruth, voltages, chip.StateCount, wlCount, cellCount);

        // 7. Codeword error analysis
        progress?.Report((0.92, "Analyzing codeword errors..."));
        var bestVoltageIdx = files.FindIndex(f => Math.Abs(f.OffsetMv - bestVoltages[0][0]) < 1);
        var zeroVoltageIdx = files.FindIndex(f => f.OffsetMv == 0);

        CodewordErrorStat? bestErrors = null;
        CodewordErrorStat? zeroErrors = null;

        // For codeword analysis: use ground truth states vs each voltage's states,
        // converting states back to page data for comparison
        if (!string.IsNullOrEmpty(config.ReferenceFilePath))
        {
            var refData = File.ReadAllBytes(config.ReferenceFilePath);
            int codewordBytes = chip.CodewordBytes;
            int frameCount = chip.FrameCount;

            if (bestVoltageIdx >= 0)
            {
                var testData = ReconstructPageData(statesPerVoltage[bestVoltageIdx], chip, wlCount, cellCount, pageTotalBytes);
                bestErrors = _codewordAnalyzer.Analyze(refData, testData, codewordBytes, frameCount);
            }

            if (zeroVoltageIdx >= 0)
            {
                var testData = ReconstructPageData(statesPerVoltage[zeroVoltageIdx], chip, wlCount, cellCount, pageTotalBytes);
                zeroErrors = _codewordAnalyzer.Analyze(refData, testData, codewordBytes, frameCount);
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
            GroundTruth = groundTruth,
            TotalCells = totalCells,
            VoltageCount = voltageCount,
            StateCount = chip.StateCount
        };
    }

    private int[] ComputeGroundTruth(int[][] statesPerVoltage, int totalCells, int stateCount)
    {
        var votes = new int[totalCells][];
        for (int c = 0; c < totalCells; c++)
            votes[c] = new int[stateCount];

        for (int v = 0; v < statesPerVoltage.Length; v++)
            for (int c = 0; c < totalCells; c++)
                votes[c][statesPerVoltage[v][c]]++;

        var groundTruth = new int[totalCells];
        for (int c = 0; c < totalCells; c++)
        {
            int maxVotes = 0;
            int bestState = 0;
            // Ties go to higher state
            for (int s = stateCount - 1; s >= 0; s--)
            {
                if (votes[c][s] >= maxVotes)
                {
                    maxVotes = votes[c][s];
                    bestState = s;
                }
            }
            groundTruth[c] = bestState;
        }

        return groundTruth;
    }

    private double[][] ComputeIncrements(int[][] statesPerVoltage, int totalCells, int stateCount, int voltageCount)
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

    private Dictionary<int, double[]> FindBestReadVoltages(
        int[][] statesPerVoltage, int[] groundTruth,
        double[] voltages, int stateCount, int wlCount, int cellCount)
    {
        var result = new Dictionary<int, double[]>();

        for (int wl = 0; wl < wlCount; wl++)
        {
            var startCell = wl * cellCount;
            var bestVoltages = new double[stateCount - 1];

            for (int pair = 0; pair < stateCount - 1; pair++)
            {
                double bestV = voltages[0];
                int bestErrors = int.MaxValue;

                for (int v = 0; v < voltages.Length; v++)
                {
                    int errors = 0;
                    for (int c = startCell; c < startCell + cellCount; c++)
                    {
                        int truth = groundTruth[c];
                        int readout = statesPerVoltage[v][c];

                        // Misclassify if truth and readout are on different sides of the boundary
                        bool truthLeft = truth <= pair;
                        bool readLeft = readout <= pair;
                        if (truthLeft != readLeft)
                            errors++;
                    }

                    if (errors < bestErrors)
                    {
                        bestErrors = errors;
                        bestV = voltages[v];
                    }
                }

                bestVoltages[pair] = bestV;
            }

            result[wl] = bestVoltages;
        }

        return result;
    }

    private byte[] ReconstructPageData(
        int[] states, ChipInfo chip,
        int wlCount, int cellCount, int pageTotalBytes)
    {
        int totalBytes = wlCount * chip.BitsPerCell * pageTotalBytes;
        var data = new byte[totalBytes];
        Array.Fill<byte>(data, 0xFF); // start with all 1s
        return data;
    }
}
