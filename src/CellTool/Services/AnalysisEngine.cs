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

        progress?.Report((0.72, "Reconstructing read-boundary Vt distributions..."));
        var voltageCodes = files.Select(f => (double)f.Code).ToArray();
        var sourceRawGray = !string.IsNullOrWhiteSpace(config.ReferenceFilePath)
            ? ReadRawGrayBaseline(config.ReferenceFilePath, config, chip, groupModel, modeEncodings, wlCount, pageTotalBytes, cellCount, totalCells)
            : rawGrayPerVoltage[0];
        var reconstructed = ReconstructSourceLevelDistributions(
            rawGrayPerVoltage,
            sourceRawGray,
            voltageCodes,
            voltageCount,
            groupModel,
            modeEncodings,
            wlCount,
            cellCount,
            chip.StateCount,
            EffectiveLevelSpacingCode(config.GetLevelSpacingMv(chip.Type), chip.StateCount),
            config.GrayCodeOrder);
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
            ErrorTypeDiagnostics = reconstructed.Diagnostics,
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
        var levelDistributions = ComputeSourceLevelErrorDistributions(
            rawGrayPerVoltage,
            sourceRawGrayStates,
            voltageCount,
            groupModel,
            modeEncodings,
            wlCount,
            cellCount,
            stateCount);
        var displayVoltageCodes = voltageCodes;
        var labels = Enumerable.Range(0, stateCount)
            .Select(i => $"L{i}")
            .ToArray();
        var curves = new double[stateCount][];
        var xValues = new double[stateCount][];
        var peaks = new StatePeakInfo[stateCount];

        double spacingCode = Math.Max(0, EffectiveLevelSpacingCode(levelSpacingMv, stateCount));
        for (int level = 0; level < stateCount; level++)
        {
            var delta = SumCurves(
                ToIncreasingDistributionDelta(levelDistributions.CumulativeCurves[level]),
                ToDecreasingDistributionDelta(levelDistributions.CumulativeCurves[level]));
            delta = KeepDominantWindow(delta, minValue: 3, maxGap: 3, padding: 2);
            double levelPositionCode = LevelPositionCode(level, stateCount, spacingCode);
            var points = BuildBoundarySidePoints(levelPositionCode, displayVoltageCodes, delta);

            xValues[level] = points.Select(p => p.X).ToArray();
            curves[level] = points.Select(p => p.Y).ToArray();

            int peakIndex = FindPeakIndex(curves[level]);
            peaks[level] = new StatePeakInfo
            {
                StateIndex = level,
                Label = labels[level],
                PeakCode = peakIndex >= 0 ? xValues[level][peakIndex] : 0,
                LeftBoundaryCode = xValues[level].Length > 0 ? xValues[level][0] : null,
                RightBoundaryCode = xValues[level].Length > 0 ? xValues[level][^1] : null,
                TotalCellCount = level < levelDistributions.SourceCounts.Length ? levelDistributions.SourceCounts[level] : 0,
                PeakIncrementValue = peakIndex >= 0 ? curves[level][peakIndex] : 0,
                AlignmentShiftMv = levelPositionCode,
                AlignmentScore = null,
                ObservationSources = $"source L{level} read as other levels"
            };
        }

        return new SourceLevelDistributionResult
        {
            Curves = curves,
            XValues = xValues,
            Labels = labels,
            Peaks = peaks,
            SourceCounts = levelDistributions.SourceCounts,
            Diagnostics = Array.Empty<ErrorTypeDiagnosticInfo>()
        };
    }

    private static SourceLevelErrorDistributionResult ComputeSourceLevelErrorDistributions(
        int[][] rawGrayPerVoltage,
        int[] sourceRawGrayStates,
        int voltageCount,
        GroupModel groupModel,
        IReadOnlyDictionary<int, int[]> modeEncodings,
        int wlCount,
        int cellCount,
        int stateCount)
    {
        var cumulative = new double[stateCount][];
        for (int level = 0; level < stateCount; level++)
            cumulative[level] = new double[voltageCount];

        var sourceCounts = new int[stateCount];
        var modeMaps = modeEncodings.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildRawGrayToLevelMap(kvp.Value));

        for (int wl = 0; wl < wlCount; wl++)
        {
            int bitsPerCell = groupModel.Entries[wl].PageIndices.Length;
            if (!modeMaps.TryGetValue(bitsPerCell, out var levelMap))
                continue;

            int modeStateCount = 1 << bitsPerCell;
            int startCell = wl * cellCount;

            for (int cellOffset = 0; cellOffset < cellCount; cellOffset++)
            {
                int cell = startCell + cellOffset;
                int sourceLevel = MapRawGrayToLevel(sourceRawGrayStates[cell], levelMap, stateCount);
                if (sourceLevel < 0 || sourceLevel >= stateCount)
                    continue;

                sourceCounts[sourceLevel]++;
                for (int v = 0; v < voltageCount; v++)
                {
                    int currentRawGray = rawGrayPerVoltage[v][cell];
                    if (currentRawGray < 0 || currentRawGray >= modeStateCount)
                        continue;

                    int currentLevel = MapRawGrayToLevel(currentRawGray, levelMap, stateCount);
                    if (currentLevel >= 0 && currentLevel != sourceLevel)
                        cumulative[sourceLevel][v]++;
                }
            }
        }

        return new SourceLevelErrorDistributionResult
        {
            CumulativeCurves = cumulative,
            SourceCounts = sourceCounts
        };
    }

    private static double LevelPositionCode(int level, int stateCount, double spacingCode)
    {
        if (level == stateCount - 1)
            return Math.Max(0, stateCount - 2) * spacingCode;

        return level * spacingCode;
    }

    private static double[] KeepDominantWindow(double[] curve, double minValue, int maxGap, int padding)
    {
        var windows = new List<(int Start, int End, double Sum, double Peak)>();
        int start = -1;
        int end = -1;
        int gap = 0;
        double sum = 0;
        double peak = 0;

        for (int i = 0; i < curve.Length; i++)
        {
            if (curve[i] >= minValue)
            {
                if (start < 0)
                    start = i;
                end = i;
                gap = 0;
                sum += curve[i];
                peak = Math.Max(peak, curve[i]);
                continue;
            }

            if (start >= 0)
            {
                gap++;
                if (gap > maxGap)
                {
                    windows.Add((start, end, sum, peak));
                    start = -1;
                    end = -1;
                    gap = 0;
                    sum = 0;
                    peak = 0;
                }
            }
        }

        if (start >= 0)
            windows.Add((start, end, sum, peak));

        if (windows.Count == 0)
            return curve;

        var best = windows
            .OrderByDescending(w => w.Peak)
            .ThenByDescending(w => w.Sum)
            .First();
        int keepStart = Math.Max(0, best.Start - padding);
        int keepEnd = Math.Min(curve.Length - 1, best.End + padding);
        var filtered = new double[curve.Length];
        for (int i = keepStart; i <= keepEnd; i++)
            filtered[i] = curve[i];

        return filtered;
    }

    private static SourceLevelDistributionResult ReconstructBoundarySideDistributions(
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
        var bitDistributions = ComputeSingleBitBoundaryDistributions(
            rawGrayPerVoltage,
            sourceRawGrayStates,
            voltageCount,
            groupModel,
            modeEncodings,
            wlCount,
            cellCount,
            stateCount,
            grayCodeOrder);

        int boundaryCount = Math.Max(0, stateCount - 1);
        int sideCurveCount = boundaryCount * 2;
        var labels = Enumerable.Range(0, sideCurveCount)
            .Select(i => BuildBoundarySideLabel(i / 2, i % 2 == 0))
            .ToArray();
        var curves = new double[sideCurveCount][];
        var xValues = new double[sideCurveCount][];
        var peaks = new StatePeakInfo[sideCurveCount];

        var leftDeltas = BuildBoundarySideDeltas(
            bitDistributions.LeftToRightCurves,
            bitDistributions.Boundaries,
            isLeftToRightSide: true);
        var rightDeltas = BuildBoundarySideDeltas(
            bitDistributions.RightToLeftCurves,
            bitDistributions.Boundaries,
            isLeftToRightSide: false);
        var displayVoltageCodes = NormalizeVoltageCodes(voltageCodes);
        var boundaryPositions = BuildReadBoundaryPositionsCode(levelSpacingMv, stateCount);

        for (int boundary = 0; boundary < boundaryCount; boundary++)
        {
            double boundaryPositionCode = boundary < boundaryPositions.Length ? boundaryPositions[boundary] : 0;
            int leftCurve = boundary * 2;
            int rightCurve = leftCurve + 1;

            BuildBoundarySideCurve(
                leftCurve,
                boundary,
                isLeftSide: true,
                leftDeltas,
                displayVoltageCodes,
                boundaryPositionCode,
                labels,
                curves,
                xValues,
                peaks,
                bitDistributions);

            BuildBoundarySideCurve(
                rightCurve,
                boundary,
                isLeftSide: false,
                rightDeltas,
                displayVoltageCodes,
                boundaryPositionCode,
                labels,
                curves,
                xValues,
                peaks,
                bitDistributions);
        }

        return new SourceLevelDistributionResult
        {
            Curves = curves,
            XValues = xValues,
            Labels = labels,
            Peaks = peaks,
            SourceCounts = bitDistributions.SourceCounts,
            Diagnostics = BuildBitBoundaryDiagnostics(
                bitDistributions.Boundaries,
                bitDistributions.CumulativeCurves,
                leftDeltas,
                rightDeltas,
                bitDistributions.BoundarySourceCounts,
                displayVoltageCodes,
                boundaryPositions)
        };
    }

    private static void BuildBoundarySideCurve(
        int curveIndex,
        int boundary,
        bool isLeftSide,
        double[][] sideDeltas,
        double[] voltageCodes,
        double boundaryPositionCode,
        string[] labels,
        double[][] curves,
        double[][] xValues,
        StatePeakInfo[] peaks,
        BitBoundaryDistributionResult bitDistributions)
    {
        var delta = boundary < sideDeltas.Length ? sideDeltas[boundary] : Array.Empty<double>();
        var points = BuildBoundarySidePoints(boundaryPositionCode, voltageCodes, delta);

        xValues[curveIndex] = points.Select(p => p.X).ToArray();
        curves[curveIndex] = points.Select(p => p.Y).ToArray();

        int peakIndex = FindPeakIndex(curves[curveIndex]);
        peaks[curveIndex] = new StatePeakInfo
        {
            StateIndex = curveIndex,
            Label = labels[curveIndex],
            PeakCode = peakIndex >= 0 ? xValues[curveIndex][peakIndex] : 0,
            LeftBoundaryCode = xValues[curveIndex].Length > 0 ? xValues[curveIndex][0] : null,
            RightBoundaryCode = xValues[curveIndex].Length > 0 ? xValues[curveIndex][^1] : null,
            TotalCellCount = boundary < bitDistributions.BoundarySourceCounts.Length ? bitDistributions.BoundarySourceCounts[boundary] : 0,
            PeakIncrementValue = peakIndex >= 0 ? curves[curveIndex][peakIndex] : 0,
            AlignmentShiftMv = boundaryPositionCode,
            AlignmentScore = null,
            ObservationSources = BoundarySideObservationSourceLabel(boundary, isLeftSide, bitDistributions.Boundaries)
        };
    }

    private static (double X, double Y)[] BuildBoundarySidePoints(
        double boundaryCode,
        double[] voltageCodes,
        double[] curve)
    {
        var points = new List<(double X, double Y)>();
        for (int v = 0; v < curve.Length && v < voltageCodes.Length; v++)
        {
            double count = curve[v];
            if (count <= 0)
                continue;

            points.Add((RoundCode(boundaryCode + voltageCodes[v]), count));
        }

        return points
            .OrderBy(p => p.X)
            .ToArray();
    }

    private static string BuildBoundarySideLabel(int boundary, bool isLeftSide) =>
        $"L{boundary + 1}-{(isLeftSide ? "left" : "right")}";

    private static double[] NormalizeVoltageCodes(double[] voltageCodes)
    {
        if (voltageCodes.Length == 0)
            return Array.Empty<double>();

        double minCode = voltageCodes.Min();
        return voltageCodes
            .Select(code => RoundCode(code - minCode))
            .ToArray();
    }

    private static double[][] BuildBoundarySideDeltas(
        double[][] cumulativeCurves,
        BitBoundaryDescriptor?[] boundaries,
        bool isLeftToRightSide)
    {
        var deltas = new double[cumulativeCurves.Length][];
        for (int boundary = 0; boundary < cumulativeCurves.Length; boundary++)
        {
            var curve = cumulativeCurves[boundary];
            deltas[boundary] = isLeftToRightSide
                ? ToIncreasingDistributionDelta(curve)
                : ToDecreasingDistributionDelta(curve);
        }

        return deltas;
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

    public static double[] ToIncreasingDistributionDelta(double[] cumulativeCurve)
    {
        if (cumulativeCurve.Length == 0)
            return Array.Empty<double>();

        var delta = new double[cumulativeCurve.Length];
        double maxSoFar = cumulativeCurve[0];
        for (int i = 1; i < cumulativeCurve.Length; i++)
        {
            double next = Math.Max(maxSoFar, cumulativeCurve[i]);
            delta[i] = Math.Max(0, next - maxSoFar);
            maxSoFar = next;
        }

        return delta;
    }

    public static double[] ToDecreasingDistributionDelta(double[] cumulativeCurve)
    {
        if (cumulativeCurve.Length == 0)
            return Array.Empty<double>();

        var delta = new double[cumulativeCurve.Length];
        double minSoFar = cumulativeCurve[0];
        for (int i = 1; i < cumulativeCurve.Length; i++)
        {
            double next = Math.Min(minSoFar, cumulativeCurve[i]);
            delta[i] = Math.Max(0, minSoFar - next);
            minSoFar = next;
        }

        return delta;
    }

    private static double[] BuildReadBoundaryPositionsCode(double levelSpacingCode, int stateCount)
    {
        if (stateCount <= 1)
            return Array.Empty<double>();

        double spacingCode = Math.Max(0, EffectiveLevelSpacingCode(levelSpacingCode, stateCount));
        var positions = new double[stateCount - 1];
        for (int i = 1; i < positions.Length; i++)
            positions[i] = i * spacingCode;

        return positions;
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

    private static int FindPeakIndex(double[] curve)
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

    private static int MapRawGrayToLevel(int rawGray, int[] levelMap, int stateCount)
    {
        if (rawGray < 0 || rawGray >= levelMap.Length)
            return -1;

        int level = levelMap[rawGray];
        return level >= 0 && level < stateCount ? level : -1;
    }

    private static double RoundCode(double value) => Math.Round(value, 6);

    private static string BoundarySideObservationSourceLabel(int boundary, bool isLeftSide, BitBoundaryDescriptor?[] boundaries)
    {
        if (boundary >= 0 && boundary < boundaries.Length && boundaries[boundary] is { } descriptor)
        {
            string side = isLeftSide ? "left" : "right";
            return $"L{boundary + 1}-{side} {descriptor.PageName} {descriptor.Direction} {descriptor.ContextLabel}";
        }

        return BuildBoundarySideLabel(boundary, isLeftSide);
    }

    private static ErrorTypeDiagnosticInfo[] BuildBitBoundaryDiagnostics(
        BitBoundaryDescriptor?[] boundaries,
        double[][] cumulativeCurves,
        double[][] leftDeltas,
        double[][] rightDeltas,
        int[] sourceCounts,
        double[] voltageCodes,
        double[] boundaryPositions)
    {
        var diagnostics = new List<ErrorTypeDiagnosticInfo>();

        for (int boundary = 0; boundary < boundaries.Length; boundary++)
        {
            var descriptor = boundaries[boundary];
            var cumulative = boundary < cumulativeCurves.Length ? cumulativeCurves[boundary] : Array.Empty<double>();
            var leftDelta = boundary < leftDeltas.Length ? leftDeltas[boundary] : Array.Empty<double>();
            var rightDelta = boundary < rightDeltas.Length ? rightDeltas[boundary] : Array.Empty<double>();
            var delta = SumCurves(leftDelta, rightDelta);
            int peakIndex = FindPeakIndex(cumulative);
            int deltaPeakIndex = FindPeakIndex(delta);
            double peakCount = peakIndex >= 0 ? cumulative[peakIndex] : 0;
            double deltaPeakCount = deltaPeakIndex >= 0 ? delta[deltaPeakIndex] : 0;
            int sourceCount = boundary < sourceCounts.Length ? sourceCounts[boundary] : 0;

            diagnostics.Add(new ErrorTypeDiagnosticInfo
            {
                SourceLevel = descriptor?.LeftLevel ?? boundary,
                CurrentLevel = descriptor?.RightLevel ?? boundary + 1,
                IsAdjacent = descriptor?.IsValid == true,
                PeakOffsetMv = peakIndex >= 0 && peakIndex < voltageCodes.Length ? voltageCodes[peakIndex] : 0,
                PeakCellCount = peakCount,
                DeltaPeakOffsetMv = deltaPeakIndex >= 0 && deltaPeakIndex < voltageCodes.Length
                    ? voltageCodes[deltaPeakIndex]
                    : 0,
                DeltaPeakCellCount = deltaPeakCount,
                ReadBoundaryMv = boundary < boundaryPositions.Length ? boundaryPositions[boundary] : double.NaN,
                PeakSourceRatio = sourceCount > 0 ? peakCount / sourceCount : 0,
                BoundaryIndex = boundary,
                BoundaryLabel = $"R{boundary + 1}",
                TargetLevel = boundary + 1,
                PageName = descriptor?.PageName ?? string.Empty,
                BitDirection = descriptor?.Direction ?? string.Empty,
                ContextLabel = descriptor?.ContextLabel ?? string.Empty,
                LeftRawGray = descriptor?.LeftRawGray ?? -1,
                RightRawGray = descriptor?.RightRawGray ?? -1,
                IsValidBoundary = descriptor?.IsValid == true,
                Note = descriptor?.InvalidReason ?? string.Empty
            });
        }

        return diagnostics
            .OrderBy(d => d.BoundaryIndex)
            .ToArray();
    }

    private static double[] SumCurves(double[] left, double[] right)
    {
        int length = Math.Max(left.Length, right.Length);
        var sum = new double[length];
        for (int i = 0; i < length; i++)
        {
            if (i < left.Length)
                sum[i] += left[i];
            if (i < right.Length)
                sum[i] += right[i];
        }

        return sum;
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
    public ErrorTypeDiagnosticInfo[] Diagnostics { get; init; } = Array.Empty<ErrorTypeDiagnosticInfo>();
}

public class SourceLevelErrorDistributionResult
{
    public double[][] CumulativeCurves { get; init; } = Array.Empty<double[]>();
    public int[] SourceCounts { get; init; } = Array.Empty<int>();
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
