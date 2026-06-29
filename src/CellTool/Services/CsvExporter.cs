using System.IO;
using System.Text;
using CellTool.Models;

namespace CellTool.Services;

public class CsvExporter
{
    private static readonly Encoding CsvEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public void ExportPeakReport(string filePath, StatePeakInfo[] peaks, double[] voltageCodes)
    {
        var sb = new StringBuilder();
        AppendPeakHeader(sb);

        if (peaks.Length == 0)
            sb.AppendLine("Vt重建,未计算,未计算,未计算,未计算,未计算,未计算,未计算");

        foreach (var p in peaks)
            AppendPeakRow(sb, p);

        File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
    }

    public void ExportSummary(string filePath, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("指标,数值");
        sb.AppendLine($"总Cell数,{result.TotalCells}");
        sb.AppendLine($"电压档位数,{result.VoltageCount}");
        sb.AppendLine($"状态数,{result.StateCount}");
        sb.AppendLine($"曲线数,{result.IncrementCurves.Length}");
        sb.AppendLine();

        AppendPeakHeader(sb);
        if (result.StatePeaks.Length == 0)
            sb.AppendLine("Vt重建,未计算,未计算,未计算,未计算,未计算,未计算,未计算");
        foreach (var p in result.StatePeaks)
            AppendPeakRow(sb, p);

        AppendLevelSpacingSuggestionSection(sb, result.LevelSpacingSuggestion);
        AppendDistributionIntegralSection(sb, result.DistributionIntegrals);

        sb.AppendLine();
        sb.AppendLine("边界,目标Level,页位,位翻转方向,上下文,左RawGray,右RawGray,是否有效,读取边界Code,累计峰偏移Code,累计峰Cell数,增量峰偏移Code,增量峰Cell数,峰值源占比,备注");
        if (result.ErrorTypeDiagnostics.Length == 0)
            sb.AppendLine("未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算");
        foreach (var d in result.ErrorTypeDiagnostics)
        {
            string boundaryLabel = string.IsNullOrWhiteSpace(d.BoundaryLabel)
                ? $"L{d.SourceLevel}->L{d.CurrentLevel}"
                : d.BoundaryLabel;
            string targetLevel = d.TargetLevel >= 0
                ? $"L{d.TargetLevel}"
                : $"L{d.CurrentLevel}";
            sb.AppendLine($"{EscapeCsv(boundaryLabel)},{EscapeCsv(targetLevel)}," +
                $"{EscapeCsv(d.PageName)},{EscapeCsv(d.BitDirection)},{EscapeCsv(d.ContextLabel)}," +
                $"{FormatRawGray(d.LeftRawGray)},{FormatRawGray(d.RightRawGray)},{FormatBool(d.IsValidBoundary)}," +
                $"{FormatDiagnosticNumber(d.ReadBoundaryMv)}," +
                $"{d.PeakOffsetMv:F2},{d.PeakCellCount:F2}," +
                $"{d.DeltaPeakOffsetMv:F2},{d.DeltaPeakCellCount:F2},{d.PeakSourceRatio:P4}," +
                $"{EscapeCsv(d.Note)}");
        }

        sb.AppendLine();
        sb.AppendLine("WL,曲线,最佳读取Code");
        if (result.BestReadVoltages.Count == 0)
            sb.AppendLine("全部,Vt重建,未计算");
        foreach (var wl in result.BestReadVoltages.Keys.OrderBy(k => k))
        {
            var values = result.BestReadVoltages[wl];
            for (int i = 0; i < values.Length; i++)
            {
                var label = i < result.TransitionLabels.Length ? result.TransitionLabels[i] : $"{i}-{i + 1}";
                sb.AppendLine($"{wl},{EscapeCsv(label)},{values[i]:F2}");
            }
        }

        AppendCodewordSection(sb, "最佳电压CW错误", result.BestVoltageErrors);
        AppendCodewordSection(sb, "零偏移CW错误", result.ZeroOffsetErrors);

        File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
    }

    public void ExportDistWl(string filePath, AnalysisResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var sb = new StringBuilder();
        sb.Append("Level,负扫不到");
        foreach (double code in result.DistWlBinCodes)
            sb.Append(',').Append(FormatDistCode(code));
        sb.AppendLine(",正扫不到,异常,阈值Avg,阈值StDev");

        for (int level = 0; level < result.StateCount; level++)
        {
            var row = level < result.DistWlMatrix.Length
                ? result.DistWlMatrix[level]
                : Array.Empty<uint>();
            var (avg, stdev) = ComputeDistStats(result.DistWlBinCodes, row);

            sb.Append($"L{level}");
            for (int i = 0; i < result.DistWlBinCodes.Length + 3; i++)
                sb.Append(',').Append(i < row.Length ? row[i].ToString() : "0");
            sb.Append(',').Append(double.IsNaN(avg) ? "NaN" : $"{avg:F4}");
            sb.Append(',').Append(double.IsNaN(stdev) ? "NaN" : $"{stdev:F4}");
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
    }

    private static string FormatDistCode(double code) =>
        Math.Abs(code - Math.Round(code)) < 1e-9
            ? ((int)Math.Round(code)).ToString()
            : code.ToString("F2");

    private static (double Avg, double StDev) ComputeDistStats(double[] codes, uint[] row)
    {
        double total = 0;
        double weighted = 0;
        int count = Math.Min(codes.Length, Math.Max(0, row.Length - 3));
        for (int i = 0; i < count; i++)
        {
            uint value = row[i + 1];
            total += value;
            weighted += codes[i] * value;
        }

        if (total <= 0)
            return (double.NaN, double.NaN);

        double avg = weighted / total;
        double variance = 0;
        for (int i = 0; i < count; i++)
        {
            double delta = codes[i] - avg;
            variance += delta * delta * row[i + 1];
        }

        return (avg, Math.Sqrt(variance / total));
    }

    private static void AppendLevelSpacingSuggestionSection(StringBuilder sb, LevelSpacingSuggestionInfo? suggestion)
    {
        sb.AppendLine();
        sb.AppendLine("L间距建议,数值");
        if (suggestion is null)
        {
            sb.AppendLine("状态,未计算");
            return;
        }

        sb.AppendLine($"当前手动间距Code,{suggestion.CurrentSpacingCode:F2}");
        sb.AppendLine($"建议间距Code,{suggestion.SuggestedSpacingCode:F2}");
        sb.AppendLine($"置信度,{suggestion.Confidence:P2}");
        sb.AppendLine($"置信等级,{EscapeCsv(suggestion.ConfidenceLabel)}");
        sb.AppendLine($"样本数,{suggestion.SampleCount}");
        sb.AppendLine($"峰距中位数Code,{suggestion.MedianGapCode:F2}");
        sb.AppendLine($"最大偏差Code,{suggestion.MaxDeviationCode:F2}");
        sb.AppendLine($"标准差Code,{suggestion.StandardDeviationCode:F2}");
        sb.AppendLine($"诊断,{EscapeCsv(suggestion.Diagnostic)}");
        if (suggestion.Items.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("L间距分组,最终间距Code,样本数,中位数Code,最大偏差Code,标准差Code,来源,置信度");
            foreach (var item in suggestion.Items.OrderBy(i => i.LevelIndex))
            {
                string source = item.SampleCount > 0 ? item.ConfidenceLabel : "手动";
                sb.AppendLine(
                    $"{EscapeCsv(item.Label)}," +
                    $"{item.SuggestedSpacingCode:F2}," +
                    $"{item.SampleCount}," +
                    $"{item.MedianGapCode:F2}," +
                    $"{item.MaxDeviationCode:F2}," +
                    $"{item.StandardDeviationCode:F2}," +
                    $"{EscapeCsv(source)}," +
                    $"{item.Confidence:P2}");
            }
        }
    }

    private static void AppendDistributionIntegralSection(StringBuilder sb, DistributionIntegralInfo[] integrals)
    {
        sb.AppendLine();
        sb.AppendLine("Level,源Cell数,未裁剪观测积分,显示曲线积分,裁剪损失,左侧未扫到估计,右侧未扫到估计,未分类未观测估计,未裁剪积分差,显示积分差,左边界观测数,右边界观测数,双边观测数,端点观测数");
        if (integrals.Length == 0)
        {
            sb.AppendLine("未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算,未计算");
            return;
        }

        foreach (var item in integrals.OrderBy(i => i.LevelIndex))
        {
            sb.AppendLine(
                $"{EscapeCsv(item.Label)}," +
                $"{item.SourceCellCount}," +
                $"{item.RawObservedIntegral:F2}," +
                $"{item.DisplayObservedIntegral:F2}," +
                $"{item.ClippedIntegral:F2}," +
                $"{item.LeftOutOfRangeEstimate:F2}," +
                $"{item.RightOutOfRangeEstimate:F2}," +
                $"{item.UnclassifiedOutOfRangeEstimate:F2}," +
                $"{item.RawIntegralDeltaFromSource:F2}," +
                $"{item.DisplayIntegralDeltaFromSource:F2}," +
                $"{item.LeftBoundaryObservedCount}," +
                $"{item.RightBoundaryObservedCount}," +
                $"{item.BothBoundaryObservedCount}," +
                $"{item.EndpointObservedCount}");
        }
    }

    public void ExportBestVoltages(
        string filePath,
        Dictionary<int, double[]> bestVoltages,
        string[]? transitionLabels = null)
    {
        var sb = new StringBuilder();
        var wlIndices = bestVoltages.Keys.OrderBy(k => k).ToList();

        int pairCount = bestVoltages.Values.FirstOrDefault()?.Length ?? 0;
        if (pairCount == 0)
        {
            sb.AppendLine("WL,最佳读取Code");
            sb.AppendLine("全部,未计算");
            File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
            return;
        }

        sb.Append("WL");
        for (int i = 0; i < pairCount; i++)
        {
            var label = transitionLabels is not null && i < transitionLabels.Length
                ? transitionLabels[i]
                : $"{i}-{i + 1}";
            sb.Append($",{EscapeCsv($"{label}_读取Code")}");
        }
        sb.AppendLine();

        foreach (var wl in wlIndices)
        {
            sb.Append(wl);
            foreach (var v in bestVoltages[wl])
                sb.Append($",{v:F2}");
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
    }

    public void ExportCodewordErrors(string filePath, CodewordErrorStat stat)
    {
        var sb = new StringBuilder();
        sb.AppendLine("指标,数值");
        sb.AppendLine($"最大Bit错误数,{stat.MaxBitErrors}");
        sb.AppendLine($"平均Bit错误数,{stat.AvgBitErrors:F4}");
        sb.AppendLine($"总Bit错误数,{stat.TotalBitErrors}");
        sb.AppendLine($"错误率,{stat.ErrorRate:F8}");
        sb.AppendLine($"总CW数,{stat.TotalCodewords}");
        sb.AppendLine();
        sb.AppendLine("CW索引,Bit错误数");

        for (int i = 0; i < stat.ErrorCounts.Length; i++)
            sb.AppendLine($"{i},{stat.ErrorCounts[i]}");

        File.WriteAllText(filePath, sb.ToString(), CsvEncoding);
    }

    private static void AppendPeakHeader(StringBuilder sb)
    {
        sb.AppendLine("曲线,峰值Code,左边界Code,右边界Code,窗口宽度Code,观测来源,对齐偏移Code,对齐评分");
    }

    private static void AppendPeakRow(StringBuilder sb, StatePeakInfo p)
    {
        sb.AppendLine($"{FormatLabel(p)},{p.PeakCode:F2}," +
            $"{FormatNullable(p.LeftBoundaryCode)},{FormatNullable(p.RightBoundaryCode)}," +
            $"{FormatNullable(p.WindowWidthCode)}," +
            $"{EscapeCsv(p.ObservationSources)}," +
            $"{FormatNullable(p.AlignmentShiftMv)}," +
            $"{FormatNullable(p.AlignmentScore)}");
    }

    private static string FormatNullable(double? value) =>
        value.HasValue ? value.Value.ToString("F2") : "错误";

    private static string FormatDiagnosticNumber(double value) =>
        double.IsNaN(value) ? "未计算" : value.ToString("F2");

    private static string FormatRawGray(int value) =>
        value < 0 ? string.Empty : value.ToString();

    private static string FormatLabel(StatePeakInfo peak) =>
        EscapeCsv(string.IsNullOrWhiteSpace(peak.Label) ? peak.StateIndex.ToString() : peak.Label);

    private static string FormatBool(bool value) => value ? "是" : "否";

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AppendCodewordSection(StringBuilder sb, string title, CodewordErrorStat? stat)
    {
        sb.AppendLine();
        sb.AppendLine($"{title},数值");
        if (stat is null)
        {
            sb.AppendLine("状态,无数据");
            return;
        }

        sb.AppendLine($"最大Bit错误数,{stat.MaxBitErrors}");
        sb.AppendLine($"平均Bit错误数,{stat.AvgBitErrors:F4}");
        sb.AppendLine($"总Bit错误数,{stat.TotalBitErrors}");
        sb.AppendLine($"错误率,{stat.ErrorRate:F8}");
        sb.AppendLine($"总CW数,{stat.TotalCodewords}");
    }
}
