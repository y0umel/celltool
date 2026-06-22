using CellTool.Models;
using ScottPlot;

namespace CellTool.Services;

public class ChartRenderer
{
    private static readonly string[] DefaultColors =
    {
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728",
        "#9467bd", "#8c564b", "#e377c2", "#7f7f7f"
    };

    public Plot Render(AnalysisResult result, double[] voltageCodes, ChartConfig chartConfig)
    {
        var plot = new Plot();
        var displayVoltages = ChartAxisMapper.ToDisplayVoltageBins(voltageCodes);
        double offsetCode = voltageCodes.Length == 0 ? 0 : voltageCodes.Min();

        // Title and axes
        plot.Title(chartConfig.Title);
        plot.XLabel(chartConfig.XAxisLabel);
        plot.YLabel(chartConfig.YAxisLabel);

        // Increment curve: total raw Gray changes between consecutive voltage files
        for (int s = 0; s < result.IncrementCurves.Length; s++)
        {
            var curve = result.IncrementCurves[s];
            var color = Color.FromHex(s < DefaultColors.Length
                ? DefaultColors[s] : DefaultColors[^1]);

            var scatter = plot.Add.Scatter(displayVoltages, curve);
            scatter.Color = color;
            scatter.LineWidth = 1.5f;
            scatter.LegendText = s < result.TransitionLabels.Length
                ? result.TransitionLabels[s]
                : $"Transition {s}";
        }

        // 0.1% boundary lines
        if (chartConfig.ShowBoundaryLines)
        {
            foreach (var peak in result.StatePeaks)
            {
                if (peak.LeftBoundaryCode.HasValue)
                {
                    var vl = plot.Add.VerticalLine(ChartAxisMapper.ToDisplayVoltageBin(peak.LeftBoundaryCode.Value, offsetCode));
                    vl.Color = Colors.Gray;
                    vl.LineWidth = 1;
                    vl.LinePattern = LinePattern.Dashed;
                }

                if (peak.RightBoundaryCode.HasValue)
                {
                    var vr = plot.Add.VerticalLine(ChartAxisMapper.ToDisplayVoltageBin(peak.RightBoundaryCode.Value, offsetCode));
                    vr.Color = Colors.Gray;
                    vr.LineWidth = 1;
                    vr.LinePattern = LinePattern.Dashed;
                }
            }
        }

        // Best read code markers
        if (chartConfig.ShowReadVoltage)
        {
            foreach (var kvp in result.BestReadVoltages)
            {
                foreach (var v in kvp.Value)
                {
                    var vl = plot.Add.VerticalLine(ChartAxisMapper.ToDisplayVoltageBin(v, offsetCode));
                    vl.Color = Colors.Red;
                    vl.LineWidth = 1.5f;
                    vl.LinePattern = LinePattern.Solid;
                    vl.LegendText = "Best read code";
                }
            }
        }

        // Legend
        if (chartConfig.ShowLegend)
            plot.ShowLegend();

        // Axis limits if set
        if (chartConfig.XMin < chartConfig.XMax)
        {
            plot.Axes.SetLimitsX(chartConfig.XMin, chartConfig.XMax);
        }
        else if (displayVoltages.Length > 0)
        {
            plot.Axes.SetLimitsX(displayVoltages.Min(), displayVoltages.Max());
        }

        if (chartConfig.YMin < chartConfig.YMax)
            plot.Axes.SetLimitsY(chartConfig.YMin, chartConfig.YMax);

        return plot;
    }

    /// <summary>
    /// Saves a rendered voltage distribution chart to a PNG file.
    /// </summary>
    public void SavePng(string filePath, AnalysisResult result, ChartConfig chartConfig, int width = 1400, int height = 900)
    {
        var plot = Render(result, result.VoltageCodes, chartConfig);
        plot.SavePng(filePath, width, height);
    }
}
