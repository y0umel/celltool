using CellTool.Models;
using CellTool.Services;
using Xunit;

namespace CellTool.Tests;

public class AnalysisAlgorithmsTests
{
    [Fact]
    public void ComputeGroundTruth_TiesResolveToHigherState()
    {
        int[][] states =
        [
            [0, 1, 2],
            [1, 1, 2],
            [0, 2, 3],
            [1, 2, 3]
        ];

        var truth = AnalysisEngine.ComputeGroundTruth(states, totalCells: 3, stateCount: 4);

        Assert.Equal([1, 2, 3], truth);
    }

    [Fact]
    public void BuildBitBoundaryDescriptors_DerivesPageBitsAndContextFromWlEncoding()
    {
        var descriptors = AnalysisEngine.BuildBitBoundaryDescriptors(
            [7, 6, 4, 0, 2, 3, 1, 5],
            bitsPerCell: 3,
            grayCodeOrder: "U-M-L");

        Assert.Equal(7, descriptors.Length);
        Assert.Collection(descriptors,
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("L", d.PageName);
                Assert.Equal("1->0", d.Direction);
                Assert.Equal("U=1 M=1", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("M", d.PageName);
                Assert.Equal("1->0", d.Direction);
                Assert.Equal("U=1 L=0", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("U", d.PageName);
                Assert.Equal("1->0", d.Direction);
                Assert.Equal("M=0 L=0", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("M", d.PageName);
                Assert.Equal("0->1", d.Direction);
                Assert.Equal("U=0 L=0", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("L", d.PageName);
                Assert.Equal("0->1", d.Direction);
                Assert.Equal("U=0 M=1", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("M", d.PageName);
                Assert.Equal("1->0", d.Direction);
                Assert.Equal("U=0 L=1", d.ContextLabel);
            },
            d =>
            {
                Assert.True(d.IsValid);
                Assert.Equal("U", d.PageName);
                Assert.Equal("0->1", d.Direction);
                Assert.Equal("M=0 L=1", d.ContextLabel);
            });
    }

    [Fact]
    public void ComputeSingleBitBoundaryDistributions_CountsOnlySingleBitFlipsWithMatchingContext()
    {
        int[][] rawGrayStates =
        [
            [6, 7, 0, 4],
            [6, 7, 4, 0],
            [7, 6, 4, 0]
        ];
        int[] sourceBaseline = [7, 6, 0, 4];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ComputeSingleBitBoundaryDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCount: 3,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 4,
            stateCount: 8);

        Assert.Equal([2, 2, 0], result.CumulativeCurves[0]);
        Assert.Equal([1, 1, 0], result.LeftToRightCurves[0]);
        Assert.Equal([1, 1, 0], result.RightToLeftCurves[0]);
        Assert.Equal([0, 2, 2], result.CumulativeCurves[2]);
        Assert.Equal([0, 1, 1], result.LeftToRightCurves[2]);
        Assert.All(
            result.CumulativeCurves.Where((_, index) => index is not 0 and not 2),
            curve => Assert.Equal([0, 0, 0], curve));
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_SlcUsesSourceErrorIncrementLikeExcel()
    {
        int[][] rawGrayStates =
        [
            BuildSlcRead(0),
            BuildSlcRead(1),
            BuildSlcRead(6),
            BuildSlcRead(27),
            BuildSlcRead(107),
            BuildSlcRead(374),
            BuildSlcRead(1291),
            BuildSlcRead(3677)
        ];
        int[] sourceBaseline = new int[4000];
        double[] voltageCodes = [0, 16, 32, 48, 64, 80, 96, 112];
        var groupModel = OneSlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 4000,
            stateCount: 2,
            levelSpacingMv: 0);

        Assert.Equal(["L0", "L1"], result.Labels);
        Assert.Equal([1, 5, 21, 80, 267, 917, 2386], result.Curves[0]);
        Assert.Equal([16, 32, 48, 64, 80, 96, 112], result.XValues[0]);
        Assert.Empty(result.Curves[1]);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_UsesFullRawIntegralForDisplay()
    {
        int[][] rawGrayStates =
        [
            BuildSlcReadFromChangedRanges(0, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 4),
            BuildSlcReadFromChangedRanges(4, 4)
        ];
        int[] sourceBaseline = new int[8];
        double[] voltageCodes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var groupModel = OneSlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 8,
            stateCount: 2,
            levelSpacingMv: 0);

        var integral = Assert.Single(result.Integrals, i => i.LevelIndex == 0);
        Assert.Equal(8, integral.SourceCellCount);
        Assert.Equal(8, integral.RawObservedIntegral);
        Assert.Equal(8, integral.DisplayObservedIntegral);
        Assert.Equal(0, integral.ClippedIntegral);
    }

    [Fact]
    public void BuildLevelSpacingSuggestion_UsesAdjacentPeakGapMedian()
    {
        StatePeakInfo[] peaks =
        [
            new() { StateIndex = 0, PeakCode = -90, PeakIncrementValue = 5 },
            new() { StateIndex = 1, PeakCode = 10, PeakIncrementValue = 100 },
            new() { StateIndex = 2, PeakCode = 96, PeakIncrementValue = 100 },
            new() { StateIndex = 3, PeakCode = 169, PeakIncrementValue = 100 },
            new() { StateIndex = 4, PeakCode = 257, PeakIncrementValue = 100 },
            new() { StateIndex = 5, PeakCode = 323, PeakIncrementValue = 100 },
            new() { StateIndex = 6, PeakCode = 414, PeakIncrementValue = 100 },
            new() { StateIndex = 7, PeakCode = 518, PeakIncrementValue = 100 }
        ];

        var suggestion = AnalysisEngine.BuildLevelSpacingSuggestion(peaks, stateCount: 8, currentSpacingCode: 80);

        Assert.NotNull(suggestion);
        Assert.Equal(86, suggestion.SuggestedSpacingCode);
        Assert.Equal(5, suggestion.SampleCount);
        Assert.Equal("中", suggestion.ConfidenceLabel);
        Assert.Contains("不自动覆盖", suggestion.Diagnostic);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_DoesNotMirrorSingleSideDirectTransition()
    {
        int[][] rawGrayStates =
        [
            [6, 6, 6, 6],
            [6, 6, 6, 7],
            [6, 6, 7, 7],
            [6, 7, 7, 7]
        ];
        int[] sourceBaseline = [6, 6, 6, 6];
        double[] voltageCodes = [-20, 0, 20, 40];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 4,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal([0, 20, 40], result.XValues[1]);
        Assert.Equal([1, 1, 1], result.Curves[1]);
        Assert.DoesNotContain(result.XValues[1], x => x < 0);
        Assert.Equal(3, result.Integrals[1].DisplayObservedIntegral);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_OverlaysBothAdjacentDirectBoundaries()
    {
        int[][] rawGrayStates =
        [
            [0],
            [4],
            [4],
            [6],
            [4]
        ];
        int[] sourceBaseline = [4];
        double[] voltageCodes = [-40, -20, 0, 40, 60];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal(2, result.Integrals[2].DisplayObservedIntegral);
        Assert.Equal([120, 140], result.XValues[2]);
        Assert.Equal([1, 1], result.Curves[2]);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_OverlaysAdjacentBoundariesWithoutConfidenceAveraging()
    {
        int[][] rawGrayStates =
        [
            [0],
            [0],
            [0],
            [4],
            [6],
            [4]
        ];
        int[] sourceBaseline = [4];
        double[] voltageCodes = [-60, -40, -20, 0, 40, 60];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal([120, 160], result.XValues[2]);
        Assert.Equal([1, 1], result.Curves[2]);
        Assert.Equal(2, result.Integrals[2].DisplayObservedIntegral);
        Assert.Equal(1, result.Integrals[2].BothBoundaryObservedCount);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_ManualSpacingChangesBoundaryComponentSeparation()
    {
        int[][] rawGrayStates =
        [
            [0],
            [4],
            [4],
            [4],
            [4],
            [6],
            [4]
        ];
        int[] sourceBaseline = [4];
        double[] voltageCodes = [-80, -60, -40, -20, 0, 20, 40];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var overlapped = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        var separated = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 120);

        Assert.Equal([100], overlapped.XValues[2]);
        Assert.Equal([2], overlapped.Curves[2]);
        Assert.Equal([140, 180], separated.XValues[2]);
        Assert.Equal([1, 1], separated.Curves[2]);
    }

    [Theory]
    [InlineData(TransitionDetectionMode.StepFit)]
    [InlineData(TransitionDetectionMode.BayesianChangePoint)]
    public void ReconstructSourceLevelDistributions_ModeledTransitionIgnoresSinglePointSpike(
        TransitionDetectionMode mode)
    {
        int[][] rawGrayStates =
        [
            [6],
            [6],
            [7],
            [6],
            [6],
            [7],
            [7],
            [7]
        ];
        int[] sourceBaseline = [6];
        double[] voltageCodes = [-30, -20, -10, 0, 10, 20, 30, 40];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80,
            transitionDetectionMode: mode);

        Assert.Equal([20], result.XValues[1]);
        Assert.Equal([1], result.Curves[1]);
        Assert.Equal(1, result.Integrals[1].DisplayObservedIntegral);
    }

    [Theory]
    [InlineData(TransitionDetectionMode.StepFit)]
    [InlineData(TransitionDetectionMode.BayesianChangePoint)]
    public void ReconstructSourceLevelDistributions_ModeledTransitionReturnsUnclassifiedWhenNoBoundaryEvidence(
        TransitionDetectionMode mode)
    {
        int[][] rawGrayStates =
        [
            [6],
            [6],
            [6],
            [6]
        ];
        int[] sourceBaseline = [6];
        double[] voltageCodes = [-20, 0, 20, 40];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80,
            transitionDetectionMode: mode);

        Assert.Empty(result.XValues[1]);
        Assert.Equal(0, result.Integrals[1].LeftOutOfRangeEstimate);
        Assert.Equal(0, result.Integrals[1].RightOutOfRangeEstimate);
        Assert.Equal(1, result.Integrals[1].UnclassifiedOutOfRangeEstimate);
    }

    [Theory]
    [InlineData(7, 1, 0, 0)]
    [InlineData(4, 0, 1, 0)]
    [InlineData(6, 0, 0, 1)]
    public void ReconstructSourceLevelDistributions_ClassifiesInternalOutOfRangeOnlyWhenBoundarySideIsKnown(
        int observedRaw,
        double expectedLor,
        double expectedRor,
        double expectedUnclassified)
    {
        int[][] rawGrayStates =
        [
            [observedRaw],
            [observedRaw],
            [observedRaw]
        ];
        int[] sourceBaseline = [6];
        double[] voltageCodes = [-20, 0, 20];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Empty(result.XValues[1]);
        Assert.Equal(expectedLor, result.Integrals[1].LeftOutOfRangeEstimate);
        Assert.Equal(expectedRor, result.Integrals[1].RightOutOfRangeEstimate);
        Assert.Equal(expectedUnclassified, result.Integrals[1].UnclassifiedOutOfRangeEstimate);
    }

    [Theory]
    [InlineData(TransitionDetectionMode.StepFit)]
    [InlineData(TransitionDetectionMode.BayesianChangePoint)]
    public void ReconstructSourceLevelDistributions_ModeledEndpointIgnoresSinglePointExitNoise(
        TransitionDetectionMode mode)
    {
        int[][] rawGrayStates =
        [
            [5],
            [5],
            [4],
            [5],
            [5],
            [4],
            [4],
            [4]
        ];
        int[] sourceBaseline = [5];
        double[] voltageCodes = [-30, -20, -10, 0, 10, 20, 30, 40];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80,
            transitionDetectionMode: mode);

        Assert.Equal([500], result.XValues[7]);
        Assert.Equal(1, result.Integrals[7].EndpointObservedCount);
    }

    [Fact]
    public void SavitzkyGolayQuadratic_KeepsConstantCurveUnchanged()
    {
        double[] values = [5, 5, 5, 5, 5, 5, 5];

        var smoothed = ChartRenderer.ApplySavitzkyGolayQuadratic(values, window: 5);

        Assert.All(smoothed, y => Assert.Equal(5, y, precision: 6));
    }

    [Fact]
    public void SavitzkyGolayDisplayCurve_PreservesPeakPositionAndClipsNegativeValues()
    {
        double[] xs = [0, 1, 2, 3, 4, 5, 6];
        double[] ys = [0, 2, 6, 10, 6, 2, 0];

        var display = ChartRenderer.BuildDisplayCurve(
            xs,
            ys,
            new ChartConfig { UseSavitzkyGolaySmoothing = true, SavitzkyGolayWindow = 5 });

        int peakIndex = Array.IndexOf(display.Y, display.Y.Max());
        Assert.Equal(3, display.X[peakIndex]);
        Assert.All(display.Y, y => Assert.True(y >= 0));
    }

    [Fact]
    public void SavitzkyGolayDisplayCurve_DoesNotBridgeLargeXGaps()
    {
        double[] xs = [0, 1, 2, 20, 21, 22];
        double[] ys = [0, 8, 0, 0, 8, 0];

        var display = ChartRenderer.BuildDisplayCurve(
            xs,
            ys,
            new ChartConfig { UseSavitzkyGolaySmoothing = true, SavitzkyGolayWindow = 5 });

        Assert.Equal([0, 1, 2, 3, 19, 20, 21, 22], display.X);
        Assert.Equal(0, display.Y[3]);
        Assert.Equal(0, display.Y[4]);
    }

    [Fact]
    public void SavitzkyGolayDisplayCurve_DoesNotAppendZeroTailPoints()
    {
        double[] xs = [0, 1, 2, 5];
        double[] ys = [3, 6, 3, 1];

        var display = ChartRenderer.BuildDisplayCurve(
            xs,
            ys,
            new ChartConfig { UseSavitzkyGolaySmoothing = true, SavitzkyGolayWindow = 5 });

        Assert.Equal(xs, display.X);
        Assert.Equal(ys.Length, display.Y.Length);
    }

    [Fact]
    public void LogDisplayY_NeverDropsBelowZero()
    {
        double[] values = [-5, 0, 0.5, 1, 10, 100];

        var logY = ChartRenderer.BuildLogDisplayY(values);

        Assert.All(logY, y => Assert.True(y >= 0));
        Assert.Equal(0, logY[0]);
        Assert.Equal(0, logY[1]);
        Assert.Equal(0, logY[2]);
        Assert.Equal(0, logY[3]);
        Assert.Equal(1, logY[4], precision: 6);
        Assert.Equal(2, logY[5], precision: 6);
    }

    [Fact]
    public void SavitzkyGolayDisplayCurve_ReturnsRawCurveWhenDisabled()
    {
        double[] xs = [2, 0, 1];
        double[] ys = [6, 0, 2];

        var display = ChartRenderer.BuildDisplayCurve(
            xs,
            ys,
            new ChartConfig { UseSavitzkyGolaySmoothing = false });

        Assert.Equal([0, 1, 2], display.X);
        Assert.Equal([0, 2, 6], display.Y);
    }

    [Fact]
    public void ChartConfig_DisablesPeakOffsetAnnotationsByDefault()
    {
        Assert.False(new ChartConfig().ShowPeakOffsetAnnotations);
    }

    [Fact]
    public void PeakOffsetAnnotations_ReportPeakDistanceToAdjacentVreads()
    {
        var result = new AnalysisResult
        {
            StateCount = 8,
            VoltageCodes = [0, 1, 2],
            TransitionLabels = ["L0", "L1", "L2", "L3", "L4", "L5", "L6", "L7"],
            IncrementCurves =
            [
                [1, 5, 2],
                [2, 8, 3],
                [],
                [],
                [],
                [],
                [],
                [1, 4, 9]
            ],
            IncrementCurveXValues =
            [
                [-10, 12, 20],
                [30, 44, 70],
                [],
                [],
                [],
                [],
                [],
                [520, 548, 567]
            ],
            StatePeaks =
            [
                new() { StateIndex = 0, AlignmentShiftMv = 0 },
                new() { StateIndex = 1, AlignmentShiftMv = 80 },
                new() { StateIndex = 2, AlignmentShiftMv = 160 },
                new() { StateIndex = 3, AlignmentShiftMv = 240 },
                new() { StateIndex = 4, AlignmentShiftMv = 320 },
                new() { StateIndex = 5, AlignmentShiftMv = 400 },
                new() { StateIndex = 6, AlignmentShiftMv = 480 }
            ]
        };

        var annotations = ChartRenderer.BuildPeakOffsetAnnotations(result);

        var l0 = Assert.Single(annotations, a => a.Level == 0);
        Assert.Equal([0], l0.ReadXs);
        Assert.Equal("R1 +12", l0.Label);

        var l1 = Assert.Single(annotations, a => a.Level == 1);
        Assert.Equal([0, 80], l1.ReadXs);
        Assert.Equal("R1 +44  R2 -36", l1.Label);

        var l7 = Assert.Single(annotations, a => a.Level == 7);
        Assert.Equal([480], l7.ReadXs);
        Assert.Equal("R7 +87", l7.Label);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_EstimatesOutOfRangeCountsFromCumulativeEdges()
    {
        int[][] rawGrayStates =
        [
            BuildSlcRead(2),
            BuildSlcRead(3),
            BuildSlcRead(3)
        ];
        int[] sourceBaseline = new int[4];
        double[] voltageCodes = [0, 1, 2];
        var groupModel = OneSlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 4,
            stateCount: 2,
            levelSpacingMv: 0);

        var integral = Assert.Single(result.Integrals, i => i.LevelIndex == 0);
        Assert.Equal(4, integral.SourceCellCount);
        Assert.Equal(1, integral.RawObservedIntegral);
        Assert.Equal(3, integral.LeftOutOfRangeEstimate);
        Assert.Equal(0, integral.RightOutOfRangeEstimate);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_KeepsSeparatedPeaksInOneLevelCurve()
    {
        int[][] rawGrayStates =
        [
            BuildSlcReadFromChangedRanges(0, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 0),
            BuildSlcReadFromChangedRanges(4, 4),
            BuildSlcReadFromChangedRanges(4, 4)
        ];
        int[] sourceBaseline = new int[8];
        double[] voltageCodes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var groupModel = OneSlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: voltageCodes.Length,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: sourceBaseline.Length,
            stateCount: 2,
            levelSpacingMv: 0);

        Assert.Equal([4, 4], result.Curves[0]);
        Assert.Equal([1, 8], result.XValues[0]);
        var integral = Assert.Single(result.Integrals, i => i.LevelIndex == 0);
        Assert.Equal(8, integral.DisplayObservedIntegral);
    }

    [Theory]
    [InlineData(TransitionDetectionMode.SlidingWindow)]
    [InlineData(TransitionDetectionMode.StepFit)]
    [InlineData(TransitionDetectionMode.BayesianChangePoint)]
    public async Task RunAsync_TlcBoundaryComponentsCountsEachCellOnceAndTracksLimits(TransitionDetectionMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), $"celltool-neighbor-{Guid.NewGuid():N}");
        string input = Path.Combine(root, "offsets");
        Directory.CreateDirectory(input);
        try
        {
            const int pageBytes = 1;
            WriteTlcFile(Path.Combine(root, "source.bin"), [6, 6, 5, 7, 2, 1], pageBytes);
            WriteTlcFile(Path.Combine(input, "-40"), [4, 6, 5, 7, 4, 2], pageBytes);
            WriteTlcFile(Path.Combine(input, "0"), [6, 6, 5, 7, 2, 1], pageBytes);
            WriteTlcFile(Path.Combine(input, "40"), [6, 7, 1, 7, 4, 2], pageBytes);

            var config = new AnalysisConfig
            {
                InputDirectory = input,
                OutputDirectory = root,
                ReferenceFilePath = Path.Combine(root, "source.bin"),
                VoltageMinCode = -40,
                VoltageMaxCode = 40,
                VoltageStepCode = 40,
                WlCount = 1,
                StartPage = 0,
                PageDataBytes = pageBytes,
                PageRedundantBytes = 0,
                CodewordsPerPage = 1,
                TlcLevelSpacingMv = 80,
                GrayCodeOrder = "U-M-L",
                TlcWlEncoding = "7,6,4,0,2,3,1,5",
                TransitionDetectionMode = mode
            };
            var chip = new ChipInfo
            {
                Type = XlcType.TLC,
                PageDataBytes = pageBytes,
                PageRedundantBytes = 0,
                WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
            };
            var groupModel = OneTlcWl();

            var result = await new AnalysisEngine().RunAsync(config, chip, groupModel);

            Assert.Equal(2, result.DistributionIntegrals[1].SourceCellCount);
            Assert.Equal(2, result.DistributionIntegrals[1].DisplayObservedIntegral);
            Assert.Equal(1, result.CurvePointValue(level: 1, x: 40));
            Assert.Equal(1, result.CurvePointValue(level: 1, x: 80));
            Assert.Equal(0, result.CurvePointValue(level: 1, x: 120));
            Assert.Equal(1, result.DistributionIntegrals[7].DisplayObservedIntegral);
            Assert.Equal(1, result.CurvePointValue(level: 7, x: 520));
            Assert.Equal(0, result.DistributionIntegrals[7].RightOutOfRangeEstimate);
            Assert.Equal(1, result.DistributionIntegrals[0].LeftOutOfRangeEstimate);
            Assert.Equal(1, result.DistributionIntegrals[4].SourceCellCount);
            Assert.Equal(0, result.DistributionIntegrals[4].DisplayObservedIntegral);
            Assert.Equal(0, result.DistributionIntegrals[4].LeftOutOfRangeEstimate);
            Assert.Equal(0, result.DistributionIntegrals[4].RightOutOfRangeEstimate);
            Assert.Equal(1, result.DistributionIntegrals[4].UnclassifiedOutOfRangeEstimate);
            Assert.Equal(1, result.DistributionIntegrals[6].SourceCellCount);
            Assert.Equal(0, result.DistributionIntegrals[6].DisplayObservedIntegral);
            Assert.Equal(0, result.DistributionIntegrals[6].LeftOutOfRangeEstimate);
            Assert.Equal(0, result.DistributionIntegrals[6].RightOutOfRangeEstimate);
            Assert.Equal(1, result.DistributionIntegrals[6].UnclassifiedOutOfRangeEstimate);
            Assert.NotNull(result.LevelSpacingSuggestion);
            Assert.Equal(80, result.LevelSpacingSuggestion.SuggestedSpacingCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TlcManualSpacingChangesBoundaryComponentPlacement()
    {
        string root = Path.Combine(Path.GetTempPath(), $"celltool-spacing-{Guid.NewGuid():N}");
        string input = Path.Combine(root, "offsets");
        Directory.CreateDirectory(input);
        try
        {
            const int pageBytes = 1;
            WriteTlcFile(Path.Combine(root, "source.bin"), [4], pageBytes);
            WriteTlcFile(Path.Combine(input, "-80"), [0], pageBytes);
            WriteTlcFile(Path.Combine(input, "-60"), [4], pageBytes);
            WriteTlcFile(Path.Combine(input, "-40"), [4], pageBytes);
            WriteTlcFile(Path.Combine(input, "-20"), [4], pageBytes);
            WriteTlcFile(Path.Combine(input, "0"), [4], pageBytes);
            WriteTlcFile(Path.Combine(input, "20"), [6], pageBytes);
            WriteTlcFile(Path.Combine(input, "40"), [4], pageBytes);

            var config = new AnalysisConfig
            {
                InputDirectory = input,
                OutputDirectory = root,
                ReferenceFilePath = Path.Combine(root, "source.bin"),
                VoltageMinCode = -80,
                VoltageMaxCode = 40,
                VoltageStepCode = 20,
                WlCount = 1,
                StartPage = 0,
                PageDataBytes = pageBytes,
                PageRedundantBytes = 0,
                CodewordsPerPage = 1,
                TlcLevelSpacingMv = 80,
                GrayCodeOrder = "U-M-L",
                TlcWlEncoding = "7,6,4,0,2,3,1,5"
            };
            var chip = new ChipInfo
            {
                Type = XlcType.TLC,
                PageDataBytes = pageBytes,
                PageRedundantBytes = 0,
                WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
            };
            var groupModel = OneTlcWl();

            var spacing80 = await new AnalysisEngine().RunAsync(config, chip, groupModel);
            config.TlcLevelSpacingMv = 120;
            var spacing120 = await new AnalysisEngine().RunAsync(config, chip, groupModel);

            Assert.Equal(80, spacing80.LevelSpacingSuggestion?.CurrentSpacingCode);
            Assert.Equal(120, spacing120.LevelSpacingSuggestion?.CurrentSpacingCode);
            Assert.Equal([100], spacing80.IncrementCurveXValues[2]);
            Assert.Equal([2], spacing80.IncrementCurves[2]);
            Assert.Equal([140, 180], spacing120.IncrementCurveXValues[2]);
            Assert.Equal([1, 1], spacing120.IncrementCurves[2]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ChartDisplayVoltages_StartAtZeroFromLowerBoundInTenMillivoltSteps()
    {
        double[] voltages = [-128, -127, 0, 127];

        var display = ChartAxisMapper.ToDisplayVoltageBins(voltages);

        Assert.Equal([0, 1, 128, 255], display);
    }

    private static GroupModel OneTlcWl() => new()
    {
        Entries =
        [
            new GroupEntry { WlIndex = 0, PageIndices = [0, 1, 2] }
        ]
    };

    private static GroupModel OneSlcWl() => new()
    {
        Entries =
        [
            new GroupEntry { WlIndex = 0, PageIndices = [0] }
        ]
    };

    private static Dictionary<int, int[]> TlcEncodings() => new()
    {
        [1] = [0, 1],
        [2] = [0, 1, 3, 2],
        [3] = [7, 6, 4, 0, 2, 3, 1, 5],
        [4] = [15, 14, 12, 8, 0, 2, 6, 4, 5, 7, 3, 1, 9, 13, 11, 10]
    };

    private static int[] BuildSlcRead(int oneCount)
    {
        var values = new int[4000];
        for (int i = 0; i < oneCount; i++)
            values[i] = 1;

        return values;
    }

    private static int[] BuildSlcReadFromChangedRanges(int firstRangeCount, int secondRangeCount)
    {
        var values = new int[8];
        for (int i = 0; i < firstRangeCount && i < 4; i++)
            values[i] = 1;
        for (int i = 0; i < secondRangeCount && i < 4; i++)
            values[i + 4] = 1;

        return values;
    }

    private static void WriteTlcFile(string path, int[] rawGrayCells, int pageBytes)
    {
        var pages = new byte[3 * pageBytes];
        for (int cell = 0; cell < rawGrayCells.Length; cell++)
        {
            int byteIndex = cell / 8;
            int bit = cell % 8;
            int mask = 1 << bit;
            int raw = rawGrayCells[cell];
            for (int page = 0; page < 3; page++)
            {
                int shift = 2 - page;
                if (((raw >> shift) & 1) != 0)
                    pages[page * pageBytes + byteIndex] |= (byte)mask;
            }
        }

        File.WriteAllBytes(path, pages);
    }

}

internal static class AnalysisResultTestExtensions
{
    public static double CurvePointValue(this AnalysisResult result, int level, double x)
    {
        var xs = result.IncrementCurveXValues[level];
        var ys = result.IncrementCurves[level];
        for (int i = 0; i < xs.Length && i < ys.Length; i++)
        {
            if (Math.Abs(xs[i] - x) < 0.0001)
                return ys[i];
        }

        return 0;
    }
}
