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
    public void ReconstructSourceLevelDistributions_RecordsRawIntegralBeforeDisplayClipping()
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
        Assert.Equal(4, integral.DisplayObservedIntegral);
        Assert.Equal(4, integral.ClippedIntegral);
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
        Assert.Equal(2, integral.LeftOutOfRangeEstimate);
        Assert.Equal(1, integral.RightOutOfRangeEstimate);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_OutputsPhysicalLevelCurves()
    {
        int[][] rawGrayStates =
        [
            [7, 6, 4],
            [7, 6, 4],
            [6, 4, 0],
            [6, 4, 0],
            [7, 6, 4]
        ];
        int[] sourceBaseline = [7, 6, 4];
        double[] voltageCodes = [-2, -1, 0, 1, 2];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: 5,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 3,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal(
            ["L0", "L1", "L2", "L3", "L4", "L5", "L6", "L7"],
            result.Labels);
        Assert.Equal(8, result.Curves.Length);
        Assert.Equal([1], result.Curves[0]);
        Assert.Equal([0], result.XValues[0]);
        Assert.Equal([1], result.Curves[1]);
        Assert.Equal(80, result.Peaks[1].PeakCode);
        Assert.Equal([1], result.Curves[2]);
        Assert.Equal(160, result.Peaks[2].PeakCode);
        Assert.Equal("source L1 read as other levels", result.Peaks[1].ObservationSources);
        Assert.Equal(80, result.Peaks[1].AlignmentShiftMv);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_PlacesPhysicalLevelCurvesByManualCodeSpacing()
    {
        int[][] rawGrayStates =
        [
            [7, 6, 4],
            [7, 6, 4],
            [6, 4, 0],
            [6, 4, 0],
            [7, 6, 4]
        ];
        int[] sourceBaseline = [7, 6, 4];
        double[] voltageCodes = [-2, -1, 0, 1, 2];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: 5,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 3,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal([0], result.XValues[0]);
        Assert.Equal(80, result.Peaks[1].PeakCode);
        Assert.Equal(160, result.Peaks[2].PeakCode);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_UsesRawVoltageCodeOffsets()
    {
        int[][] rawGrayStates =
        [
            [6],
            [6],
            [4]
        ];
        int[] sourceBaseline = [6];
        double[] voltageCodes = [-128, 0, 127];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: 3,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal([207], result.XValues[1]);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_KeepsLastLevelAtLastReadBoundary()
    {
        int[][] rawGrayStates =
        [
            [5],
            [1],
            [1]
        ];
        int[] sourceBaseline = [5];
        double[] voltageCodes = [-1, 0, 1];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: 3,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 1,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Equal([480], result.XValues[7]);
    }

    [Fact]
    public void ReconstructSourceLevelDistributions_UsesSourceLevelEvenForMultiBitChanges()
    {
        int[][] rawGrayStates =
        [
            [4, 2],
            [7, 2],
            [4, 2]
        ];
        int[] sourceBaseline = [7, 4];
        double[] voltageCodes = [-1, 0, 1];
        var groupModel = OneTlcWl();
        var encodings = TlcEncodings();

        var result = AnalysisEngine.ReconstructSourceLevelDistributions(
            rawGrayStates,
            sourceBaseline,
            voltageCodes,
            voltageCount: 3,
            groupModel,
            encodings,
            wlCount: 1,
            cellCount: 2,
            stateCount: 8,
            levelSpacingMv: 80);

        Assert.Contains(1, result.Curves[0]);
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
}
