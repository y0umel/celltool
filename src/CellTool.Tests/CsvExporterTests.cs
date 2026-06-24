using CellTool.Models;
using CellTool.Services;
using Xunit;

namespace CellTool.Tests;

public class CsvExporterTests
{
    [Fact]
    public void ExportSummary_WritesFullDistributionIntegralSection()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var result = new AnalysisResult
            {
                TotalCells = 8,
                VoltageCount = 10,
                StateCount = 2,
                DistributionIntegrals =
                [
                    new DistributionIntegralInfo
                    {
                        LevelIndex = 0,
                        Label = "L0",
                        SourceCellCount = 8,
                        RawObservedIntegral = 8,
                        DisplayObservedIntegral = 8,
                        LeftOutOfRangeEstimate = 0,
                        RightOutOfRangeEstimate = 0
                    }
                ]
            };

            new CsvExporter().ExportSummary(filePath, result);

            string csv = File.ReadAllText(filePath);
            Assert.Contains("未裁剪观测积分,显示曲线积分,裁剪损失", csv);
            Assert.Contains("L0,8,8.00,8.00,0.00", csv);
            Assert.DoesNotContain("间距拟合", csv);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}
