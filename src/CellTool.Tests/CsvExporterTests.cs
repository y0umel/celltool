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
                LevelSpacingSuggestion = new LevelSpacingSuggestionInfo
                {
                    CurrentSpacingCode = 80,
                    SuggestedSpacingCode = 86,
                    Confidence = 0.6,
                    ConfidenceLabel = "中",
                    Diagnostic = "诊断文本",
                    SampleCount = 5,
                    MedianGapCode = 86,
                    MaxDeviationCode = 20,
                    StandardDeviationCode = 7,
                    Items =
                    [
                        new LevelSpacingEstimateInfo
                        {
                            LevelIndex = 1,
                            Label = "L1",
                            SuggestedSpacingCode = 84,
                            SampleCount = 3,
                            MedianGapCode = 84,
                            MaxDeviationCode = 4,
                            StandardDeviationCode = 2,
                            Confidence = 0.8,
                            ConfidenceLabel = "高"
                        }
                    ]
                },
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
            Assert.Contains("L间距建议,数值", csv);
            Assert.Contains("建议间距Code,86.00", csv);
            Assert.Contains("置信等级,中", csv);
            Assert.Contains("标准差Code,7.00", csv);
            Assert.Contains("L间距分组,最终间距Code,样本数", csv);
            Assert.Contains("L1,84.00,3,84.00,4.00,2.00,高", csv);
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
