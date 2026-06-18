using System.Text;
using CellTool.Models;

namespace CellTool.Services;

public class CsvExporter
{
    public void ExportPeakReport(string filePath, StatePeakInfo[] peaks, double[] voltagesMv)
    {
        var sb = new StringBuilder();
        sb.AppendLine("State,PeakVoltage(mV),LeftBoundary(mV),RightBoundary(mV),WindowWidth(mV)");

        foreach (var p in peaks)
        {
            sb.AppendLine($"{p.StateIndex},{p.PeakVoltageMv:F2}," +
                $"{FormatNullable(p.LeftBoundaryMv)},{FormatNullable(p.RightBoundaryMv)}," +
                $"{FormatNullable(p.WindowWidthMv)}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public void ExportBestVoltages(string filePath, Dictionary<int, double[]> bestVoltages)
    {
        var sb = new StringBuilder();
        var wlIndices = bestVoltages.Keys.OrderBy(k => k).ToList();

        // Header
        sb.Append("WL");
        int pairCount = bestVoltages.Values.First().Length;
        for (int i = 0; i < pairCount; i++)
            sb.Append($",Vth_{i}-{i + 1}(mV)");
        sb.AppendLine();

        // Data
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
        sb.AppendLine($"TotalCodewords,{stat.TotalCodewords}");
        sb.AppendLine();
        sb.AppendLine("CodewordIndex,BitErrors");

        for (int i = 0; i < stat.ErrorCounts.Length; i++)
            sb.AppendLine($"{i},{stat.ErrorCounts[i]}");

        File.WriteAllText(filePath, sb.ToString());
    }

    private string FormatNullable(double? value) =>
        value.HasValue ? value.Value.ToString("F2") : "err";
}
