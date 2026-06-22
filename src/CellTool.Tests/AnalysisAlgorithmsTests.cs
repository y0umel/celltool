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
    public void ComputeRawGrayChangeIncrements_CountsAnyRawGrayChangeAsSingleCurve()
    {
        int[][] rawGrayStates =
        [
            [6, 4, 7, 5, 2],
            [4, 0, 6, 5, 3]
        ];
        int[] blank = [7, 6, 4, 0, 2];

        var increments = AnalysisEngine.ComputeRawGrayChangeIncrements(
            rawGrayStates,
            blank,
            totalCells: 5,
            voltageCount: 2);

        Assert.Equal([4, 4], increments);
    }

    [Fact]
    public void FindBestReadVoltages_SearchesBetweenAdjacentPeaks()
    {
        int[][] states =
        [
            [0, 0, 0, 1],
            [0, 0, 1, 1],
            [0, 1, 1, 1],
            [1, 1, 1, 1]
        ];
        int[] truth = [0, 0, 1, 1];
        double[] voltages = [0, 10, 20, 30];
        StatePeakInfo[] peaks =
        [
            new() { StateIndex = 0, PeakCode = 0 },
            new() { StateIndex = 1, PeakCode = 20 }
        ];

        var best = AnalysisEngine.FindBestReadVoltages(
            states,
            truth,
            voltages,
            peaks,
            stateCount: 2,
            wlCount: 1,
            cellCount: 4);

        Assert.Equal(10, best[0][0]);
    }

    [Fact]
    public void ChartDisplayVoltages_StartAtZeroFromLowerBoundInTenMillivoltSteps()
    {
        double[] voltages = [-128, -127, 0, 127];

        var display = ChartAxisMapper.ToDisplayVoltageBins(voltages);

        Assert.Equal([0, 1, 128, 255], display);
    }
}
