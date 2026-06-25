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
        int pageTotalBytes = config.PageTotalBytes!.Value;
        int cellCount = pageTotalBytes * 8;
        int totalCells = wlCount * cellCount;

        var voltageCodes = files.Select(f => (double)f.Code).ToArray();
        var modeEncodings = ParseModeEncodings(config, chip);
        int[] groundTruth = Array.Empty<int>();
        double spacingCode = EffectiveLevelSpacingCode(config.GetLevelSpacingMv(chip.Type), chip.StateCount);
        var accumulator = new DirectLevelDistributionAccumulator(chip.StateCount, spacingCode);

        for (int wl = 0; wl < wlCount; wl++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((0.05 + ((double)wl / wlCount * 0.67),
                $"Reading WL {wl} ({wl + 1}/{wlCount}) across {voltageCount} voltage files..."));

            var entry = groupModel.Entries[wl];
            int bitsPerCell = entry.PageIndices.Length;
            var grayDecoder = new GrayCodeDecoder(modeEncodings[bitsPerCell], bitsPerCell, config.GrayCodeOrder);
            var rawGrayByVoltage = new int[voltageCount][];

            for (int v = 0; v < voltageCount; v++)
            {
                ct.ThrowIfCancellationRequested();
                var wlData = fileReader.ReadWlBytes(files[v].FilePath, entry, pageTotalBytes, config.StartPage);
                rawGrayByVoltage[v] = grayDecoder.DecodeRawGrayWl(wlData, pageTotalBytes);
            }

            var sourceRawGray = !string.IsNullOrWhiteSpace(config.ReferenceFilePath)
                ? ReadRawGrayBaselineForWl(config.ReferenceFilePath, config, groupModel, modeEncodings, wl, wlCount, pageTotalBytes)
                : rawGrayByVoltage[0];
            AccumulateDirectLevelDistributionsForWl(
                accumulator,
                rawGrayByVoltage,
                sourceRawGray,
                voltageCodes,
                modeEncodings[bitsPerCell],
                bitsPerCell,
                chip.StateCount,
                cellCount,
                config.GrayCodeOrder);
        }

        progress?.Report((0.72, "Reconstructing Vt distributions..."));
        var reconstructed = accumulator.ToResult();

        var increments = reconstructed.Curves;
        var curveXValues = reconstructed.XValues;
        var transitionLabels = reconstructed.Labels;
        var statePeaks = reconstructed.Peaks;
        var bestVoltages = new Dictionary<int, double[]>();

        progress?.Report((0.91, "Analyzing codeword errors..."));
        var bestVoltageIdx = FindNearestVoltageIndex(voltageCodes, MedianBestVoltage(bestVoltages));
        var zeroVoltageIdx = Array.FindIndex(files.ToArray(), f => f.Code == 0);

        CodewordErrorStat? bestErrors = null;
        CodewordErrorStat? zeroErrors = null;

        if (!string.IsNullOrWhiteSpace(config.ReferenceFilePath))
        {
            var referenceData = ReadReferenceBytes(config.ReferenceFilePath, config, chip, groupModel, wlCount);
            int codewordBytes = config.CodewordBytes!.Value;

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
            IncrementCurveXValues = curveXValues,
            DistributionIntegrals = reconstructed.Integrals,
            ErrorTypeDiagnostics = reconstructed.Diagnostics,
            VoltageCodes = voltageCodes,
            TransitionLabels = transitionLabels,
            LevelSpacingSuggestion = reconstructed.SpacingSuggestion ?? BuildLevelSpacingSuggestion(
                statePeaks,
                chip.StateCount,
                EffectiveLevelSpacingCode(config.GetLevelSpacingMv(chip.Type), chip.StateCount)),
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
        if (chip.StateCount > 2 && EffectiveLevelSpacingCode(config.GetLevelSpacingMv(chip.Type), chip.StateCount) <= 0)
            throw new InvalidDataException("L spacing code must be positive for MLC/TLC/QLC reconstruction.");
        if (config.WlCount <= 0)
            throw new InvalidDataException("WL count must be positive.");
        if (config.WlCount > groupModel.WlCount)
            throw new InvalidDataException($"Configured WL count {config.WlCount} exceeds GroupModel rows {groupModel.WlCount}.");
        if (config.PageDataBytes is not > 0)
            throw new InvalidDataException("数据页大小必须大于 0。");
        if (config.PageRedundantBytes is null or < 0)
            throw new InvalidDataException("冗余页大小必须大于或等于 0。");
        if (config.CodewordsPerPage is not > 0)
            throw new InvalidDataException("CW/Page 必须大于 0。");

        int pageTotalBytes = config.PageTotalBytes!.Value;
        if (pageTotalBytes % config.CodewordsPerPage.Value != 0)
            throw new InvalidDataException(
                $"页总大小 {pageTotalBytes} 不能被 CW/Page {config.CodewordsPerPage.Value} 整除。");
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

    public static SourceLevelDistributionResult ReconstructSourceLevelDistributions(
        int[][] rawGrayPerVoltage,
        int[] sourceRawGrayStates,
        double[] voltageCodes,
        int voltageCount,
        GroupModel groupModel,
        IReadOnlyDictionary<int, int[]> modeEncodings,
        int wlCount,
        int cellCount,
        int stateCount,
        double levelSpacingMv,
        string grayCodeOrder = "U-M-L")
    {
        return ReconstructSourceLevelErrorDistributions(
            rawGrayPerVoltage,
            sourceRawGrayStates,
            voltageCodes,
            voltageCount,
            groupModel,
            modeEncodings,
            wlCount,
            cellCount,
            stateCount,
            levelSpacingMv,
            grayCodeOrder);
    }

    private static void AccumulateDirectLevelDistributionsForWl(
        DirectLevelDistributionAccumulator accumulator,
        int[][] rawGrayByVoltage,
        int[] sourceRawGray,
        double[] voltageCodes,
        int[] wlEncoding,
        int bitsPerCell,
        int stateCount,
        int cellCount,
        string grayCodeOrder)
    {
        var levelMap = BuildRawGrayToLevelMap(wlEncoding);
        var descriptors = BuildBitBoundaryDescriptors(wlEncoding, bitsPerCell, grayCodeOrder);
        int usableCells = Math.Min(cellCount, sourceRawGray.Length);
        for (int cell = 0; cell < usableCells; cell++)
        {
            int sourceRaw = sourceRawGray[cell];
            int sourceLevel = MapRawGrayToLevel(sourceRaw, levelMap, stateCount);
            if (sourceLevel < 0 || sourceLevel >= stateCount)
                continue;

            accumulator.SourceCounts[sourceLevel]++;

            DirectTransitionObservation? left = null;
            DirectTransitionObservation? right = null;
            DirectTransitionObservation? endpoint = null;
            if (sourceLevel > 0)
            {
                var boundary = descriptors[sourceLevel - 1];
                if (boundary.IsValid && boundary.RightRawGray == sourceRaw)
                {
                    left = FindDirectTransitionObservation(
                        rawGrayByVoltage,
                        cell,
                        sourceRaw,
                        boundary.LeftRawGray,
                        voltageCodes,
                        neighborOnHighOffsetSide: true);
                }
            }

            if (sourceLevel < stateCount - 1 && sourceLevel < descriptors.Length)
            {
                var boundary = descriptors[sourceLevel];
                if (boundary.IsValid && boundary.LeftRawGray == sourceRaw)
                {
                    right = FindDirectTransitionObservation(
                        rawGrayByVoltage,
                        cell,
                        sourceRaw,
                        boundary.RightRawGray,
                        voltageCodes,
                        neighborOnHighOffsetSide: false);
                }
            }

            if (sourceLevel == 0 && left is null)
            {
                endpoint = FindAnySourceExitObservation(
                    rawGrayByVoltage,
                    cell,
                    sourceRaw,
                    voltageCodes,
                    preferSourceToOther: false);
            }
            else if (sourceLevel == stateCount - 1 && right is null)
            {
                endpoint = FindAnySourceExitObservation(
                    rawGrayByVoltage,
                    cell,
                    sourceRaw,
                    voltageCodes,
                    preferSourceToOther: true);
            }

            double? x = ChooseDirectCellPosition(sourceLevel, accumulator.StateCount, accumulator.SpacingCode, left, right);
            if (!x.HasValue && endpoint.HasValue)
            {
                double boundaryCode = sourceLevel == 0
                    ? BoundaryPositionCode(0, accumulator.SpacingCode)
                    : BoundaryPositionCode(stateCount - 2, accumulator.SpacingCode);
                x = RoundCode(boundaryCode + endpoint.Value.Offset);
            }

            if (x.HasValue)
            {
                accumulator.AddPoint(sourceLevel, x.Value);
            }
            else if (sourceLevel == 0)
            {
                accumulator.LeftOutOfRange[sourceLevel]++;
            }
            else if (sourceLevel == stateCount - 1)
            {
                accumulator.RightOutOfRange[sourceLevel]++;
            }
            else
            {
                accumulator.RightOutOfRange[sourceLevel]++;
            }

            if (left.HasValue && right.HasValue)
            {
                double gap = left.Value.Offset - right.Value.Offset;
                if (gap > 0 && !double.IsNaN(gap) && !double.IsInfinity(gap))
                    accumulator.SpacingSamples[sourceLevel].Add(gap);
            }
        }
    }

    private static double? ChooseDirectCellPosition(
        int sourceLevel,
        int stateCount,
        double spacingCode,
        DirectTransitionObservation? left,
        DirectTransitionObservation? right)
    {
        double? leftX = left.HasValue && sourceLevel > 0
            ? BoundaryPositionCode(sourceLevel - 1, spacingCode) + left.Value.Offset
            : null;
        double? rightX = right.HasValue && sourceLevel < stateCount - 1
            ? BoundaryPositionCode(sourceLevel, spacingCode) + right.Value.Offset
            : null;

        if (leftX.HasValue && rightX.HasValue)
        {
            if (left!.Value.Support != right!.Value.Support)
                return left.Value.Support > right.Value.Support
                    ? RoundCode(leftX.Value)
                    : RoundCode(rightX.Value);

            double levelCenter = sourceLevel == 0
                ? 0
                : sourceLevel == stateCount - 1
                    ? BoundaryPositionCode(stateCount - 2, spacingCode)
                    : BoundaryPositionCode(sourceLevel - 1, spacingCode) + spacingCode / 2.0;
            return Math.Abs(leftX.Value - levelCenter) <= Math.Abs(rightX.Value - levelCenter)
                ? RoundCode(leftX.Value)
                : RoundCode(rightX.Value);
        }

        if (leftX.HasValue)
            return RoundCode(leftX.Value);
        if (rightX.HasValue)
            return RoundCode(rightX.Value);

        return null;
    }

    private static DirectTransitionObservation? FindDirectTransitionObservation(
        int[][] rawGrayByVoltage,
        int cell,
        int sourceRawGray,
        int neighborRawGray,
        double[] voltageCodes,
        bool neighborOnHighOffsetSide)
    {
        int count = Math.Min(rawGrayByVoltage.Length, voltageCodes.Length);
        DirectTransitionObservation? preferred = null;
        DirectTransitionObservation? fallback = null;

        for (int v = 1; v < count; v++)
        {
            int previous = rawGrayByVoltage[v - 1][cell];
            int current = rawGrayByVoltage[v][cell];
            bool sourceToNeighbor = previous == sourceRawGray && current == neighborRawGray;
            bool neighborToSource = previous == neighborRawGray && current == sourceRawGray;
            if (!sourceToNeighbor && !neighborToSource)
                continue;

            int support = sourceToNeighbor
                ? CountSameBackward(rawGrayByVoltage, cell, v - 1, sourceRawGray, count, 8) +
                  CountSameForward(rawGrayByVoltage, cell, v, neighborRawGray, count, 8)
                : CountSameBackward(rawGrayByVoltage, cell, v - 1, neighborRawGray, count, 8) +
                  CountSameForward(rawGrayByVoltage, cell, v, sourceRawGray, count, 8);
            var candidate = new DirectTransitionObservation(voltageCodes[v], support);
            bool isPreferredDirection = neighborOnHighOffsetSide ? sourceToNeighbor : neighborToSource;
            if (isPreferredDirection)
                preferred = BetterTransitionCandidate(preferred, candidate);
            else
                fallback = BetterTransitionCandidate(fallback, candidate);
        }

        return preferred ?? fallback;
    }

    private static DirectTransitionObservation? FindAnySourceExitObservation(
        int[][] rawGrayByVoltage,
        int cell,
        int sourceRawGray,
        double[] voltageCodes,
        bool preferSourceToOther)
    {
        int count = Math.Min(rawGrayByVoltage.Length, voltageCodes.Length);
        DirectTransitionObservation? preferred = null;
        DirectTransitionObservation? fallback = null;

        for (int v = 1; v < count; v++)
        {
            int previous = rawGrayByVoltage[v - 1][cell];
            int current = rawGrayByVoltage[v][cell];
            bool sourceToOther = previous == sourceRawGray && current != sourceRawGray;
            bool otherToSource = previous != sourceRawGray && current == sourceRawGray;
            if (!sourceToOther && !otherToSource)
                continue;

            int support = sourceToOther
                ? CountSameBackward(rawGrayByVoltage, cell, v - 1, sourceRawGray, count, 8) +
                  CountDifferentForward(rawGrayByVoltage, cell, v, sourceRawGray, count, 8)
                : CountDifferentBackward(rawGrayByVoltage, cell, v - 1, sourceRawGray, count, 8) +
                  CountSameForward(rawGrayByVoltage, cell, v, sourceRawGray, count, 8);
            var candidate = new DirectTransitionObservation(voltageCodes[v], support);
            bool isPreferredDirection = preferSourceToOther ? sourceToOther : otherToSource;
            if (isPreferredDirection)
                preferred = BetterTransitionCandidate(preferred, candidate);
            else
                fallback = BetterTransitionCandidate(fallback, candidate);
        }

        return preferred ?? fallback;
    }

    private static DirectTransitionObservation? BetterTransitionCandidate(
        DirectTransitionObservation? current,
        DirectTransitionObservation candidate)
    {
        if (current is null)
            return candidate;
        if (candidate.Support > current.Value.Support)
            return candidate;
        if (candidate.Support < current.Value.Support)
            return current;

        return Math.Abs(candidate.Offset) < Math.Abs(current.Value.Offset)
            ? candidate
            : current;
    }

    private static SourceLevelDistributionResult ReconstructSourceLevelErrorDistributions(
        int[][] rawGrayPerVoltage,
        int[] sourceRawGrayStates,
        double[] voltageCodes,
        int voltageCount,
        GroupModel groupModel,
        IReadOnlyDictionary<int, int[]> modeEncodings,
        int wlCount,
        int cellCount,
        int stateCount,
        double levelSpacingMv,
        string grayCodeOrder)
    {
        double spacingCode = Math.Max(0, EffectiveLevelSpacingCode(levelSpacingMv, stateCount));
        var accumulator = new DirectLevelDistributionAccumulator(stateCount, spacingCode);
        for (int wl = 0; wl < wlCount; wl++)
        {
            int bitsPerCell = groupModel.Entries[wl].PageIndices.Length;
            if (!modeEncodings.TryGetValue(bitsPerCell, out var encoding))
                continue;

            int startCell = wl * cellCount;
            var wlSource = new int[cellCount];
            Array.Copy(sourceRawGrayStates, startCell, wlSource, 0, cellCount);
            var wlRawGrayByVoltage = new int[voltageCount][];
            for (int v = 0; v < voltageCount; v++)
            {
                wlRawGrayByVoltage[v] = new int[cellCount];
                Array.Copy(rawGrayPerVoltage[v], startCell, wlRawGrayByVoltage[v], 0, cellCount);
            }

            AccumulateDirectLevelDistributionsForWl(
                accumulator,
                wlRawGrayByVoltage,
                wlSource,
                voltageCodes,
                encoding,
                bitsPerCell,
                stateCount,
                cellCount,
                grayCodeOrder);
        }

        return accumulator.ToResult();
    }

    private static double BoundaryPositionCode(int boundaryIndex, double spacingCode) =>
        Math.Max(0, boundaryIndex) * spacingCode;

    public static LevelSpacingSuggestionInfo? BuildLevelSpacingSuggestion(
        StatePeakInfo[] peaks,
        int stateCount,
        double currentSpacingCode)
    {
        if (stateCount <= 2 || currentSpacingCode <= 0 || peaks.Length < stateCount)
            return null;

        if (peaks.Any(p => !string.IsNullOrWhiteSpace(p.ObservationSources) &&
                           p.ObservationSources.Contains("R", StringComparison.Ordinal)))
        {
            return new LevelSpacingSuggestionInfo
            {
                CurrentSpacingCode = currentSpacingCode,
                SuggestedSpacingCode = currentSpacingCode,
                Confidence = 0,
                ConfidenceLabel = "未启用",
                Diagnostic = "当前曲线由多个读电压边界方向分量拼接，同一 L 可能有多个峰；自动峰距推断容易被子峰误导，暂按手动 L 间距绘图。",
                SampleCount = 0,
                MedianGapCode = currentSpacingCode,
                MaxDeviationCode = 0,
                StandardDeviationCode = 0
            };
        }

        int lastRegularLevel = Math.Min(stateCount - 2, peaks.Length - 1);
        var peakCodes = new List<double>();
        for (int level = 1; level <= lastRegularLevel; level++)
        {
            var peak = peaks.FirstOrDefault(p => p.StateIndex == level);
            if (peak?.PeakIncrementValue > 0)
                peakCodes.Add(peak.PeakCode);
        }

        var gaps = new List<double>();
        for (int i = 1; i < peakCodes.Count; i++)
        {
            double gap = peakCodes[i] - peakCodes[i - 1];
            if (gap > 0)
                gaps.Add(gap);
        }

        if (gaps.Count < Math.Max(2, stateCount / 3))
        {
            return new LevelSpacingSuggestionInfo
            {
                CurrentSpacingCode = currentSpacingCode,
                SuggestedSpacingCode = currentSpacingCode,
                Confidence = 0,
                ConfidenceLabel = "低",
                Diagnostic = $"可用峰距样本不足，仅找到 {gaps.Count} 个相邻峰距。",
                SampleCount = gaps.Count,
                MedianGapCode = currentSpacingCode,
                MaxDeviationCode = 0,
                StandardDeviationCode = 0
            };
        }

        double medianGap = Median(gaps);
        double maxDeviation = gaps.Max(g => Math.Abs(g - medianGap));
        double standardDeviation = StandardDeviation(gaps);
        double normalizedDeviation = medianGap > 0 ? maxDeviation / medianGap : 1;
        double confidence = Math.Clamp(1 - normalizedDeviation / 0.5, 0, 1);
        string confidenceLabel = confidence >= 0.75
            ? "高"
            : confidence >= 0.45
                ? "中"
                : "低";

        double deltaFromCurrent = medianGap - currentSpacingCode;
        string diagnostic = Math.Abs(deltaFromCurrent) <= Math.Max(2, currentSpacingCode * 0.05)
            ? $"相邻峰距中位数 {medianGap:F2} code，与当前间距 {currentSpacingCode:F2} code 接近。"
            : $"相邻峰距中位数 {medianGap:F2} code，与当前间距 {currentSpacingCode:F2} code 相差 {deltaFromCurrent:F2} code；当前建议只作诊断，不自动覆盖手动设置。";

        return new LevelSpacingSuggestionInfo
        {
            CurrentSpacingCode = currentSpacingCode,
            SuggestedSpacingCode = medianGap,
            Confidence = confidence,
            ConfidenceLabel = confidenceLabel,
            Diagnostic = diagnostic,
            SampleCount = gaps.Count,
            MedianGapCode = medianGap,
            MaxDeviationCode = maxDeviation,
            StandardDeviationCode = standardDeviation
        };
    }

    private static int CountSameBackward(
        int[][] rawGrayPerVoltage,
        int cell,
        int start,
        int value,
        int count,
        int limit)
    {
        int found = 0;
        for (int v = start; v >= 0 && found < limit; v--)
        {
            if (rawGrayPerVoltage[v][cell] != value)
                break;
            found++;
        }

        return found;
    }

    private static int CountSameForward(
        int[][] rawGrayPerVoltage,
        int cell,
        int start,
        int value,
        int count,
        int limit)
    {
        int found = 0;
        for (int v = start; v < count && found < limit; v++)
        {
            if (rawGrayPerVoltage[v][cell] != value)
                break;
            found++;
        }

        return found;
    }

    private static int CountDifferentBackward(
        int[][] rawGrayPerVoltage,
        int cell,
        int start,
        int value,
        int count,
        int limit)
    {
        int found = 0;
        for (int v = start; v >= 0 && found < limit; v--)
        {
            if (rawGrayPerVoltage[v][cell] == value)
                break;
            found++;
        }

        return found;
    }

    private static int CountDifferentForward(
        int[][] rawGrayPerVoltage,
        int cell,
        int start,
        int value,
        int count,
        int limit)
    {
        int found = 0;
        for (int v = start; v < count && found < limit; v++)
        {
            if (rawGrayPerVoltage[v][cell] == value)
                break;
            found++;
        }

        return found;
    }

    public static BitBoundaryDistributionResult ComputeSingleBitBoundaryDistributions(
        int[][] rawGrayPerVoltage,
        int[] sourceRawGrayStates,
        int voltageCount,
        GroupModel groupModel,
        IReadOnlyDictionary<int, int[]> modeEncodings,
        int wlCount,
        int cellCount,
        int stateCount,
        string grayCodeOrder = "U-M-L")
    {
        int boundaryCount = Math.Max(0, stateCount - 1);
        var cumulative = new double[boundaryCount][];
        var leftToRight = new double[boundaryCount][];
        var rightToLeft = new double[boundaryCount][];
        for (int boundary = 0; boundary < boundaryCount; boundary++)
        {
            cumulative[boundary] = new double[voltageCount];
            leftToRight[boundary] = new double[voltageCount];
            rightToLeft[boundary] = new double[voltageCount];
        }

        var sourceCounts = new int[stateCount];
        var boundarySourceCounts = new int[boundaryCount];
        var boundaries = new BitBoundaryDescriptor?[boundaryCount];

        var modeMaps = modeEncodings.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildRawGrayToLevelMap(kvp.Value));
        var modeDescriptors = modeEncodings.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildBitBoundaryDescriptors(kvp.Value, kvp.Key, grayCodeOrder));
        var modePairMaps = modeDescriptors.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildBoundaryPairMap(kvp.Value, 1 << kvp.Key));

        for (int wl = 0; wl < wlCount; wl++)
        {
            int bitsPerCell = groupModel.Entries[wl].PageIndices.Length;
            if (!modeMaps.TryGetValue(bitsPerCell, out var levelMap) ||
                !modeDescriptors.TryGetValue(bitsPerCell, out var descriptors) ||
                !modePairMaps.TryGetValue(bitsPerCell, out var pairMap))
            {
                continue;
            }

            for (int i = 0; i < descriptors.Length && i < boundaries.Length; i++)
                boundaries[i] ??= descriptors[i];

            int modeStateCount = 1 << bitsPerCell;
            int startCell = wl * cellCount;

            for (int cellOffset = 0; cellOffset < cellCount; cellOffset++)
            {
                int cell = startCell + cellOffset;
                int sourceRawGray = sourceRawGrayStates[cell];
                int sourceLevel = MapRawGrayToLevel(sourceRawGray, levelMap, stateCount);
                if (sourceLevel < 0)
                    continue;

                sourceCounts[sourceLevel]++;
                for (int boundary = 0; boundary < descriptors.Length && boundary < boundarySourceCounts.Length; boundary++)
                {
                    var descriptor = descriptors[boundary];
                    if (!descriptor.IsValid)
                        continue;

                    if (sourceRawGray == descriptor.LeftRawGray || sourceRawGray == descriptor.RightRawGray)
                        boundarySourceCounts[boundary]++;
                }

                if (sourceRawGray < 0 || sourceRawGray >= modeStateCount)
                    continue;

                int mapOffset = sourceRawGray * modeStateCount;
                for (int v = 0; v < voltageCount; v++)
                {
                    int currentRawGray = rawGrayPerVoltage[v][cell];
                    if (currentRawGray < 0 || currentRawGray >= modeStateCount)
                        continue;

                    int boundary = pairMap[mapOffset + currentRawGray];
                    if (boundary < 0 || boundary >= cumulative.Length)
                        continue;

                    cumulative[boundary][v]++;

                    var descriptor = descriptors[boundary];
                    if (sourceRawGray == descriptor.LeftRawGray && currentRawGray == descriptor.RightRawGray)
                    {
                        leftToRight[boundary][v]++;
                    }
                    else if (sourceRawGray == descriptor.RightRawGray && currentRawGray == descriptor.LeftRawGray)
                    {
                        rightToLeft[boundary][v]++;
                    }
                }
            }
        }

        return new BitBoundaryDistributionResult
        {
            CumulativeCurves = cumulative,
            LeftToRightCurves = leftToRight,
            RightToLeftCurves = rightToLeft,
            SourceCounts = sourceCounts,
            BoundarySourceCounts = boundarySourceCounts,
            Boundaries = boundaries
        };
    }

    public static BitBoundaryDescriptor[] BuildBitBoundaryDescriptors(
        int[] wlEncoding,
        int bitsPerCell,
        string grayCodeOrder = "U-M-L")
    {
        if (wlEncoding.Length < 2)
            return Array.Empty<BitBoundaryDescriptor>();

        var pageNamesByRawBit = BuildPageNamesByRawBit(grayCodeOrder, bitsPerCell);
        var descriptors = new BitBoundaryDescriptor[wlEncoding.Length - 1];

        for (int i = 0; i < descriptors.Length; i++)
        {
            int leftRawGray = wlEncoding[i];
            int rightRawGray = wlEncoding[i + 1];
            int flipMask = leftRawGray ^ rightRawGray;
            bool isSingleBit = IsSingleBit(flipMask);
            if (!isSingleBit)
            {
                descriptors[i] = BitBoundaryDescriptor.Invalid(
                    i,
                    i,
                    i + 1,
                    leftRawGray,
                    rightRawGray,
                    $"raw Gray {FormatRawGray(leftRawGray, bitsPerCell)}->{FormatRawGray(rightRawGray, bitsPerCell)} changes {CountSetBits(flipMask)} bits");
                continue;
            }

            int rawBitIndex = SingleBitIndex(flipMask);
            int contextMask = ((1 << bitsPerCell) - 1) ^ flipMask;
            int contextValue = leftRawGray & contextMask;
            string direction = $"{(((leftRawGray & flipMask) != 0) ? 1 : 0)}->{(((rightRawGray & flipMask) != 0) ? 1 : 0)}";
            string pageName = rawBitIndex >= 0 && rawBitIndex < pageNamesByRawBit.Length
                ? pageNamesByRawBit[rawBitIndex]
                : $"bit{rawBitIndex}";

            descriptors[i] = new BitBoundaryDescriptor
            {
                BoundaryIndex = i,
                LeftLevel = i,
                RightLevel = i + 1,
                LeftRawGray = leftRawGray,
                RightRawGray = rightRawGray,
                FlipMask = flipMask,
                ContextMask = contextMask,
                ContextValue = contextValue,
                RawBitIndex = rawBitIndex,
                PageName = pageName,
                Direction = direction,
                ContextLabel = FormatContext(contextMask, contextValue, pageNamesByRawBit),
                IsValid = true
            };
        }

        return descriptors;
    }

    public static double DefaultLevelSpacingCode(int stateCount) => stateCount switch
    {
        4 => 145,
        8 => 80,
        16 => 40,
        _ => 0
    };

    private static double EffectiveLevelSpacingCode(double levelSpacingCode, int stateCount)
    {
        if (levelSpacingCode > 0)
            return levelSpacingCode;

        return DefaultLevelSpacingCode(stateCount);
    }

    private static int MapRawGrayToLevel(int rawGray, int[] levelMap, int stateCount)
    {
        if (rawGray < 0 || rawGray >= levelMap.Length)
            return -1;

        int level = levelMap[rawGray];
        return level >= 0 && level < stateCount ? level : -1;
    }

    private static double RoundCode(double value) => Math.Round(value, 6);

    private static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var ordered = values.OrderBy(v => v).ToArray();
        int mid = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[mid]
            : (ordered[mid - 1] + ordered[mid]) / 2.0;
    }

    private static double StandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return 0;

        double average = values.Average();
        double variance = values.Sum(v => Math.Pow(v - average, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static int[] BuildBoundaryPairMap(BitBoundaryDescriptor[] descriptors, int stateCount)
    {
        var map = Enumerable.Repeat(-1, stateCount * stateCount).ToArray();

        foreach (var descriptor in descriptors)
        {
            if (!descriptor.IsValid)
                continue;

            for (int source = 0; source < stateCount; source++)
            {
                for (int current = 0; current < stateCount; current++)
                {
                    if (source == current)
                        continue;

                    int changed = source ^ current;
                    if (changed != descriptor.FlipMask)
                        continue;

                    if ((source & descriptor.ContextMask) != descriptor.ContextValue ||
                        (current & descriptor.ContextMask) != descriptor.ContextValue)
                    {
                        continue;
                    }

                    map[source * stateCount + current] = descriptor.BoundaryIndex;
                }
            }
        }

        return map;
    }

    private static string[] BuildPageNamesByRawBit(string grayCodeOrder, int bitsPerCell)
    {
        var tokens = grayCodeOrder.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < bitsPerCell)
            tokens = Enumerable.Range(0, bitsPerCell).Select(i => $"P{i}").ToArray();

        var names = new string[bitsPerCell];
        for (int i = 0; i < bitsPerCell; i++)
        {
            int rawBitIndex = bitsPerCell - 1 - i;
            names[rawBitIndex] = NormalizePageName(tokens[i]);
        }

        return names;
    }

    private static string NormalizePageName(string token)
    {
        return token.Trim().ToUpperInvariant() switch
        {
            "U" or "UPPER" => "U",
            "M" or "MIDDLE" => "M",
            "L" or "LOWER" => "L",
            var page => page
        };
    }

    private static string FormatContext(int contextMask, int contextValue, string[] pageNamesByRawBit)
    {
        var parts = new List<string>();
        for (int bit = pageNamesByRawBit.Length - 1; bit >= 0; bit--)
        {
            int mask = 1 << bit;
            if ((contextMask & mask) == 0)
                continue;

            string name = bit < pageNamesByRawBit.Length ? pageNamesByRawBit[bit] : $"bit{bit}";
            int value = (contextValue & mask) != 0 ? 1 : 0;
            parts.Add($"{name}={value}");
        }

        return parts.Count == 0 ? "all" : string.Join(" ", parts);
    }

    private static bool IsSingleBit(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int SingleBitIndex(int value)
    {
        int index = 0;
        while ((value >>= 1) != 0)
            index++;

        return index;
    }

    private static int CountSetBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static string FormatRawGray(int value, int bitsPerCell)
    {
        return Convert.ToString(value, 2).PadLeft(bitsPerCell, '0');
    }

    private static int[] BuildRawGrayToLevelMap(int[] wlEncoding)
    {
        int[] map = Enumerable.Repeat(-1, wlEncoding.Length).ToArray();
        for (int level = 0; level < wlEncoding.Length; level++)
        {
            int rawGray = wlEncoding[level];
            if (rawGray >= 0 && rawGray < map.Length)
                map[rawGray] = level;
        }

        return map;
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

    private byte[] ReadReferenceBytes(
        string referenceFilePath,
        AnalysisConfig config,
        ChipInfo chip,
        GroupModel groupModel,
        int wlCount)
    {
        var expectedBytes = groupModel.Entries
            .Take(wlCount)
            .Sum(entry => entry.PageIndices.Length * config.PageTotalBytes!.Value);
        var fileInfo = new FileInfo(referenceFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Reference file not found: {referenceFilePath}", referenceFilePath);

        if (fileInfo.Length == expectedBytes)
            return File.ReadAllBytes(referenceFilePath);

        return ReadSelectedBytes(referenceFilePath, config, chip, groupModel, wlCount);
    }

    private int[] ReadRawGrayBaselineForWl(
        string baselineFilePath,
        AnalysisConfig config,
        GroupModel groupModel,
        Dictionary<int, int[]> modeEncodings,
        int wl,
        int wlCount,
        int pageTotalBytes)
    {
        var fileInfo = new FileInfo(baselineFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Baseline file not found: {baselineFilePath}", baselineFilePath);

        var entry = groupModel.Entries[wl];
        int bitsPerCell = entry.PageIndices.Length;
        var decoder = new GrayCodeDecoder(modeEncodings[bitsPerCell], bitsPerCell, config.GrayCodeOrder);
        long selectedByteCount = groupModel.Entries
            .Take(wlCount)
            .Sum(e => (long)e.PageIndices.Length * pageTotalBytes);

        if (fileInfo.Length == selectedByteCount)
        {
            long offset = 0;
            for (int i = 0; i < wl; i++)
                offset += (long)groupModel.Entries[i].PageIndices.Length * pageTotalBytes;

            int wlBytes = bitsPerCell * pageTotalBytes;
            var wlData = new byte[wlBytes];
            using var stream = File.OpenRead(baselineFilePath);
            stream.Seek(offset, SeekOrigin.Begin);
            int read = stream.Read(wlData, 0, wlBytes);
            if (read != wlBytes)
                throw new EndOfStreamException($"Baseline file ended while reading WL {wl}.");

            return decoder.DecodeRawGrayWl(wlData, pageTotalBytes);
        }

        var selectedWlData = fileReader.ReadWlBytes(baselineFilePath, entry, pageTotalBytes, config.StartPage);
        return decoder.DecodeRawGrayWl(selectedWlData, pageTotalBytes);
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
            totalBytes += groupModel.Entries[wl].PageIndices.Length * config.PageTotalBytes!.Value;
        }

        var data = new byte[totalBytes];

        for (int wl = 0; wl < wlCount; wl++)
        {
            var wlData = fileReader.ReadWlBytes(
                filePath,
                groupModel.Entries[wl],
                config.PageTotalBytes!.Value,
                config.StartPage);

            Array.Copy(wlData, 0, data, wlOffsets[wl], wlData.Length);
        }

        return data;
    }
}

public class SourceLevelDistributionResult
{
    public double[][] Curves { get; init; } = Array.Empty<double[]>();
    public double[][] XValues { get; init; } = Array.Empty<double[]>();
    public string[] Labels { get; init; } = Array.Empty<string>();
    public StatePeakInfo[] Peaks { get; init; } = Array.Empty<StatePeakInfo>();
    public int[] SourceCounts { get; init; } = Array.Empty<int>();
    public DistributionIntegralInfo[] Integrals { get; init; } = Array.Empty<DistributionIntegralInfo>();
    public ErrorTypeDiagnosticInfo[] Diagnostics { get; init; } = Array.Empty<ErrorTypeDiagnosticInfo>();
    public LevelSpacingSuggestionInfo? SpacingSuggestion { get; init; }
}

public class BitBoundaryDistributionResult
{
    public double[][] CumulativeCurves { get; init; } = Array.Empty<double[]>();
    public double[][] LeftToRightCurves { get; init; } = Array.Empty<double[]>();
    public double[][] RightToLeftCurves { get; init; } = Array.Empty<double[]>();
    public int[] SourceCounts { get; init; } = Array.Empty<int>();
    public int[] BoundarySourceCounts { get; init; } = Array.Empty<int>();
    public BitBoundaryDescriptor?[] Boundaries { get; init; } = Array.Empty<BitBoundaryDescriptor?>();
}

public class BitBoundaryDescriptor
{
    public int BoundaryIndex { get; init; }
    public int LeftLevel { get; init; }
    public int RightLevel { get; init; }
    public int LeftRawGray { get; init; }
    public int RightRawGray { get; init; }
    public int FlipMask { get; init; }
    public int ContextMask { get; init; }
    public int ContextValue { get; init; }
    public int RawBitIndex { get; init; }
    public string PageName { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string ContextLabel { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string InvalidReason { get; init; } = string.Empty;

    public static BitBoundaryDescriptor Invalid(
        int boundaryIndex,
        int leftLevel,
        int rightLevel,
        int leftRawGray,
        int rightRawGray,
        string reason)
    {
        return new BitBoundaryDescriptor
        {
            BoundaryIndex = boundaryIndex,
            LeftLevel = leftLevel,
            RightLevel = rightLevel,
            LeftRawGray = leftRawGray,
            RightRawGray = rightRawGray,
            IsValid = false,
            InvalidReason = reason
        };
    }
}

internal sealed class DirectLevelDistributionAccumulator
{
    private readonly Dictionary<double, double>[] histograms;

    public DirectLevelDistributionAccumulator(int stateCount, double spacingCode)
    {
        StateCount = stateCount;
        SpacingCode = Math.Max(0, spacingCode);
        histograms = Enumerable.Range(0, stateCount)
            .Select(_ => new Dictionary<double, double>())
            .ToArray();
        SourceCounts = new int[stateCount];
        LeftOutOfRange = new double[stateCount];
        RightOutOfRange = new double[stateCount];
        SpacingSamples = Enumerable.Range(0, stateCount)
            .Select(_ => new List<double>())
            .ToArray();
    }

    public int StateCount { get; }
    public double SpacingCode { get; }
    public int[] SourceCounts { get; }
    public double[] LeftOutOfRange { get; }
    public double[] RightOutOfRange { get; }
    public List<double>[] SpacingSamples { get; }

    public void AddPoint(int level, double x)
    {
        if (level < 0 || level >= histograms.Length)
            return;

        double rounded = Math.Round(x, 6);
        histograms[level][rounded] = histograms[level].TryGetValue(rounded, out double current)
            ? current + 1
            : 1;
    }

    public SourceLevelDistributionResult ToResult()
    {
        var labels = Enumerable.Range(0, StateCount).Select(i => $"L{i}").ToArray();
        var curves = new double[StateCount][];
        var xValues = new double[StateCount][];
        var peaks = new StatePeakInfo[StateCount];
        var integrals = new DistributionIntegralInfo[StateCount];

        for (int level = 0; level < StateCount; level++)
        {
            var points = histograms[level].OrderBy(p => p.Key).ToArray();
            xValues[level] = points.Select(p => p.Key).ToArray();
            curves[level] = points.Select(p => p.Value).ToArray();
            int peakIndex = LocalFindPeakIndex(curves[level]);
            double levelPosition = level == StateCount - 1
                ? Math.Max(0, StateCount - 2) * SpacingCode
                : level * SpacingCode;

            peaks[level] = new StatePeakInfo
            {
                StateIndex = level,
                Label = labels[level],
                PeakCode = peakIndex >= 0 ? xValues[level][peakIndex] : 0,
                LeftBoundaryCode = xValues[level].Length > 0 ? xValues[level][0] : null,
                RightBoundaryCode = xValues[level].Length > 0 ? xValues[level][^1] : null,
                TotalCellCount = SourceCounts[level],
                PeakIncrementValue = peakIndex >= 0 ? curves[level][peakIndex] : 0,
                AlignmentShiftMv = levelPosition,
                AlignmentScore = null,
                ObservationSources = "direct adjacent raw Gray transition"
            };

            double observed = curves[level].Sum();
            integrals[level] = new DistributionIntegralInfo
            {
                LevelIndex = level,
                Label = labels[level],
                SourceCellCount = SourceCounts[level],
                RawObservedIntegral = observed,
                DisplayObservedIntegral = observed,
                LeftOutOfRangeEstimate = LeftOutOfRange[level],
                RightOutOfRangeEstimate = RightOutOfRange[level]
            };
        }

        return new SourceLevelDistributionResult
        {
            Curves = curves,
            XValues = xValues,
            Labels = labels,
            Peaks = peaks,
            SourceCounts = SourceCounts.ToArray(),
            Integrals = integrals,
            Diagnostics = Array.Empty<ErrorTypeDiagnosticInfo>(),
            SpacingSuggestion = BuildSpacingSuggestion()
        };
    }

    private LevelSpacingSuggestionInfo? BuildSpacingSuggestion()
    {
        if (StateCount <= 2 || SpacingCode <= 0)
            return null;

        var items = Enumerable.Range(1, StateCount - 2)
            .Select(level => BuildItem(level, SpacingSamples[level]))
            .ToArray();
        var valid = items.Where(i => i.SampleCount > 0).ToArray();
        if (valid.Length == 0)
        {
            return new LevelSpacingSuggestionInfo
            {
                CurrentSpacingCode = SpacingCode,
                SuggestedSpacingCode = SpacingCode,
                Confidence = 0,
                ConfidenceLabel = "低",
                Diagnostic = "未找到同一源 Level 同时到左右相邻 Gray code 的 direct 跳变样本，绘图使用手动 L 间距。",
                SampleCount = 0,
                MedianGapCode = SpacingCode,
                MaxDeviationCode = 0,
                StandardDeviationCode = 0,
                Items = items
            };
        }

        var values = valid.Select(i => i.SuggestedSpacingCode).ToArray();
        double suggested = LocalMedian(values);
        double std = LocalStandardDeviation(values);
        double maxDeviation = values.Max(v => Math.Abs(v - suggested));
        double coverage = (double)valid.Length / Math.Max(1, StateCount - 2);
        double confidence = Math.Clamp((1 - (suggested > 0 ? std / suggested : 1)) * coverage, 0, 1);

        return new LevelSpacingSuggestionInfo
        {
            CurrentSpacingCode = SpacingCode,
            SuggestedSpacingCode = suggested,
            Confidence = confidence,
            ConfidenceLabel = confidence >= 0.75 ? "高" : confidence >= 0.45 ? "中" : "低",
            Diagnostic = "按同一源 Level 的左右 direct 跳变 offset 差估计各段 L 间距；当前绘图使用手动 L 间距定位边界。",
            SampleCount = valid.Sum(i => i.SampleCount),
            MedianGapCode = suggested,
            MaxDeviationCode = maxDeviation,
            StandardDeviationCode = std,
            Items = items
        };
    }

    private LevelSpacingEstimateInfo BuildItem(int level, IReadOnlyCollection<double> samples)
    {
        if (samples.Count == 0)
        {
            return new LevelSpacingEstimateInfo
            {
                LevelIndex = level,
                Label = $"L{level}",
                SuggestedSpacingCode = SpacingCode,
                SampleCount = 0,
                MedianGapCode = SpacingCode,
                MaxDeviationCode = 0,
                StandardDeviationCode = 0,
                Confidence = 0,
                ConfidenceLabel = "手动"
            };
        }

        double median = LocalMedian(samples);
        double std = LocalStandardDeviation(samples);
        double confidence = Math.Clamp(1 - (median > 0 ? std / median : 1), 0, 1);
        return new LevelSpacingEstimateInfo
        {
            LevelIndex = level,
            Label = $"L{level}",
            SuggestedSpacingCode = median,
            SampleCount = samples.Count,
            MedianGapCode = median,
            MaxDeviationCode = samples.Max(v => Math.Abs(v - median)),
            StandardDeviationCode = std,
            Confidence = confidence,
            ConfidenceLabel = confidence >= 0.75 ? "高" : confidence >= 0.45 ? "中" : "低"
        };
    }

    private static int LocalFindPeakIndex(double[] curve)
    {
        int peakIndex = -1;
        double peakValue = 0;
        for (int i = 0; i < curve.Length; i++)
        {
            if (curve[i] > peakValue)
            {
                peakValue = curve[i];
                peakIndex = i;
            }
        }

        return peakIndex;
    }

    private static double LocalMedian(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var ordered = values.OrderBy(v => v).ToArray();
        int mid = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[mid]
            : (ordered[mid - 1] + ordered[mid]) / 2.0;
    }

    private static double LocalStandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return 0;

        double average = values.Average();
        double variance = values.Sum(v => Math.Pow(v - average, 2)) / values.Count;
        return Math.Sqrt(variance);
    }
}

internal readonly record struct DirectTransitionObservation(double Offset, int Support);
