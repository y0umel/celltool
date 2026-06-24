using CellTool.Models;
using CellTool.Services;
using Xunit;

namespace CellTool.Tests;

public class RealDataRegressionTests
{
    private const string WorkspaceRoot = "/home/doc/workspace";
    private const string SourceFile = WorkspaceRoot + "/testdata/origin_data.bin";
    private const string GroupModelFile = WorkspaceRoot + "/testdata/x4-9060_GroupModel.txt";

    [Theory]
    [InlineData("testdata", new double[] { -94, 39, 115, 199, 277, 357, 444, 554 }, new double[] { 0, 700, 650, 650, 650, 500, 550, 450 })]
    [InlineData("testdata2", new double[] { -128, 10, 96, 169, 257, 323, 414, 518 }, new double[] { 0, 900, 900, 900, 850, 850, 900, 450 })]
    public async Task AnalysisEngine_ReconstructsReferenceLikeLevelCurves_ForLocalFixtures(
        string fixtureName,
        double[] expectedPeakCodes,
        double[] minimumPeakValues)
    {
        string fixtureRoot = Path.Combine(WorkspaceRoot, fixtureName);
        string inputDirectory = Path.Combine(fixtureRoot, "offset_file");
        if (!HasLocalFixture(inputDirectory))
            return;

        var config = BuildFixtureConfig(fixtureName, inputDirectory);
        var chip = BuildFixtureChip(fixtureName);
        var groupModel = new GroupModelParser().LoadFromFile(GroupModelFile, expectedWlCount: 1600, expectedValidPagesPerWl: 3);
        var result = await new AnalysisEngine().RunAsync(config, chip, groupModel);

        Assert.Equal(8, result.IncrementCurves.Length);
        Assert.All(result.IncrementCurves[1..7], curve => Assert.NotEmpty(curve));
        Assert.All(result.DistributionIntegrals[1..7], integral =>
        {
            Assert.InRange(integral.SourceCellCount, 15000, 22000);
            Assert.Equal(integral.RawObservedIntegral, integral.DisplayObservedIntegral);
            Assert.InRange(integral.DisplayObservedIntegral, 15000, 22000);
            Assert.InRange(Math.Abs(integral.DisplayIntegralDeltaFromSource), 0, 1000);
            Assert.True(integral.LeftOutOfRangeEstimate >= 0);
            Assert.True(integral.RightOutOfRangeEstimate >= 0);
        });

        for (int level = 1; level < result.StatePeaks.Length; level++)
        {
            Assert.InRange(result.StatePeaks[level].PeakCode, expectedPeakCodes[level] - 20, expectedPeakCodes[level] + 20);
            Assert.True(
                result.StatePeaks[level].PeakIncrementValue >= minimumPeakValues[level],
                $"{fixtureName} L{level} peak {result.StatePeaks[level].PeakIncrementValue} is below {minimumPeakValues[level]}.");

            Assert.InRange(SignificantWidth(result, level), 20, 90);
            Assert.True(
                LocalMassRatio(result, level, 80) > 0.95,
                $"{fixtureName} L{level} has too much mass away from the main peak.");
        }

        string outputPath = Path.Combine(Path.GetTempPath(), $"celltool-{fixtureName}-{Guid.NewGuid():N}.png");
        try
        {
            new ChartRenderer().SavePng(
                outputPath,
                result,
                new ChartConfig
                {
                    Title = "Vt Distribution",
                    YAxisLabel = "# Cells",
                    ShowReadVoltage = false,
                    ShowLegend = true,
                    ShowBoundaryLines = true
                });
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 50_000);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static AnalysisConfig BuildFixtureConfig(string fixtureName, string inputDirectory) => new()
    {
        InputDirectory = inputDirectory,
        OutputDirectory = Path.Combine(WorkspaceRoot, fixtureName),
        ReferenceFilePath = SourceFile,
        VoltageMinCode = -128,
        VoltageMaxCode = 127,
        VoltageStepCode = 1,
        WlCount = 1,
        StartPage = 0,
        PageDataBytes = 18432,
        PageRedundantBytes = 0,
        CodewordsPerPage = 1,
        TlcLevelSpacingMv = 80,
        GrayCodeOrder = "U-M-L",
        TlcWlEncoding = "7,6,4,0,2,3,1,5"
    };

    private static ChipInfo BuildFixtureChip(string fixtureName) => new()
    {
        Manufacturer = "Fixture",
        DieName = fixtureName,
        Type = XlcType.TLC,
        PageDataBytes = 18432,
        PageRedundantBytes = 0,
        FrameCount = 1,
        WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
    };

    private static double SignificantWidth(AnalysisResult result, int level)
    {
        var curve = result.IncrementCurves[level];
        var xs = result.IncrementCurveXValues[level];
        double peak = curve.Length == 0 ? 0 : curve.Max();
        if (peak <= 0)
            return 0;

        var significantX = curve
            .Select((y, i) => (Y: y, X: xs[i]))
            .Where(p => p.Y >= Math.Max(20, peak * 0.1))
            .Select(p => p.X)
            .ToArray();

        return significantX.Length == 0 ? 0 : significantX.Max() - significantX.Min();
    }

    private static double LocalMassRatio(AnalysisResult result, int level, double halfWidth)
    {
        var curve = result.IncrementCurves[level];
        var xs = result.IncrementCurveXValues[level];
        if (curve.Length == 0)
            return 0;

        int peakIndex = 0;
        for (int i = 1; i < curve.Length; i++)
        {
            if (curve[i] > curve[peakIndex])
                peakIndex = i;
        }

        double total = curve.Sum();
        if (total <= 0)
            return 0;

        double peakX = xs[peakIndex];
        double local = curve
            .Select((y, i) => (Y: y, X: xs[i]))
            .Where(p => Math.Abs(p.X - peakX) <= halfWidth)
            .Sum(p => p.Y);

        return local / total;
    }

    private static bool HasLocalFixture(string inputDirectory) =>
        File.Exists(SourceFile) &&
        File.Exists(GroupModelFile) &&
        Directory.Exists(inputDirectory) &&
        File.Exists(Path.Combine(inputDirectory, "-128")) &&
        File.Exists(Path.Combine(inputDirectory, "127"));
}
