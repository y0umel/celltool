using System.IO;
using System.Text;
using CellTool.Models;

namespace CellTool.Services;

public class CsvExporter
{
    public void ExportPeakReport(string filePath, StatePeakInfo[] peaks, double[] voltageCodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Curve,PeakCode,LeftBoundaryCode,RightBoundaryCode,WindowWidthCode");

        if (peaks.Length == 0)
            sb.AppendLine("Total Gray changes,not calculated,not calculated,not calculated,not calculated");

        foreach (var p in peaks)
        {
            sb.AppendLine($"{FormatLabel(p)},{p.PeakCode:F2}," +
                $"{FormatNullable(p.LeftBoundaryCode)},{FormatNullable(p.RightBoundaryCode)}," +
                $"{FormatNullable(p.WindowWidthCode)}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public void ExportSummary(string filePath, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"TotalCells,{result.TotalCells}");
        sb.AppendLine($"VoltageCodeCount,{result.VoltageCount}");
        sb.AppendLine($"StateCount,{result.StateCount}");
        sb.AppendLine($"TransitionCurveCount,{result.IncrementCurves.Length}");
        sb.AppendLine();

        sb.AppendLine("Curve,PeakCode,LeftBoundaryCode,RightBoundaryCode,WindowWidthCode");
        if (result.StatePeaks.Length == 0)
            sb.AppendLine("Total Gray changes,not calculated,not calculated,not calculated,not calculated");
        foreach (var p in result.StatePeaks)
        {
            sb.AppendLine($"{FormatLabel(p)},{p.PeakCode:F2}," +
                $"{FormatNullable(p.LeftBoundaryCode)},{FormatNullable(p.RightBoundaryCode)}," +
                $"{FormatNullable(p.WindowWidthCode)}");
        }

        sb.AppendLine();
        sb.AppendLine("WL,Curve,BestReadCode");
        if (result.BestReadVoltages.Count == 0)
            sb.AppendLine("all,Total Gray changes,not calculated");
        foreach (var wl in result.BestReadVoltages.Keys.OrderBy(k => k))
        {
            var values = result.BestReadVoltages[wl];
            for (int i = 0; i < values.Length; i++)
            {
                var label = i < result.TransitionLabels.Length ? result.TransitionLabels[i] : $"{i}-{i + 1}";
                sb.AppendLine($"{wl},{EscapeCsv(label)},{values[i]:F2}");
            }
        }

        AppendCodewordSection(sb, "BestVoltageCodewordErrors", result.BestVoltageErrors);
        AppendCodewordSection(sb, "ZeroOffsetCodewordErrors", result.ZeroOffsetErrors);

        File.WriteAllText(filePath, sb.ToString());
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
            sb.AppendLine("WL,BestReadCode");
            sb.AppendLine("all,not calculated");
            File.WriteAllText(filePath, sb.ToString());
            return;
        }

        sb.Append("WL");
        for (int i = 0; i < pairCount; i++)
        {
            var label = transitionLabels is not null && i < transitionLabels.Length
                ? transitionLabels[i]
                : $"{i}-{i + 1}";
            sb.Append($",{EscapeCsv($"{label}_ReadCode")}");
        }
        sb.AppendLine();

        foreach (var wl in wlIndices)
        {
            sb.Append(wl);
            foreach (var v in bestVoltages[wl])
                sb.Append($",{v:F2}");
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public void ExportCodewordErrors(string filePath, CodewordErrorStat stat)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"MaxBitErrors,{stat.MaxBitErrors}");
        sb.AppendLine($"AvgBitErrors,{stat.AvgBitErrors:F4}");
        sb.AppendLine($"TotalBitErrors,{stat.TotalBitErrors}");
        sb.AppendLine($"ErrorRate,{stat.ErrorRate:F8}");
        sb.AppendLine($"TotalCodewords,{stat.TotalCodewords}");
        sb.AppendLine();
        sb.AppendLine("CodewordIndex,BitErrors");

        for (int i = 0; i < stat.ErrorCounts.Length; i++)
            sb.AppendLine($"{i},{stat.ErrorCounts[i]}");

        File.WriteAllText(filePath, sb.ToString());
    }

    private static string FormatNullable(double? value) =>
        value.HasValue ? value.Value.ToString("F2") : "err";

    private static string FormatLabel(StatePeakInfo peak) =>
        EscapeCsv(string.IsNullOrWhiteSpace(peak.Label) ? peak.StateIndex.ToString() : peak.Label);

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AppendCodewordSection(StringBuilder sb, string title, CodewordErrorStat? stat)
    {
        sb.AppendLine();
        sb.AppendLine($"{title},Value");
        if (stat is null)
        {
            sb.AppendLine("Status,not available");
            return;
        }

        sb.AppendLine($"MaxBitErrors,{stat.MaxBitErrors}");
        sb.AppendLine($"AvgBitErrors,{stat.AvgBitErrors:F4}");
        sb.AppendLine($"TotalBitErrors,{stat.TotalBitErrors}");
        sb.AppendLine($"ErrorRate,{stat.ErrorRate:F8}");
        sb.AppendLine($"TotalCodewords,{stat.TotalCodewords}");
    }
}
