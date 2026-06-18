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

    public Plot Render(AnalysisResult result, double[] voltagesMv, ChartConfig chartConfig)
    {
        var plot = new Plot();

        // Title and axes
        plot.Title(chartConfig.Title);
        plot.XLabel(chartConfig.XAxisLabel);
        plot.YLabel(chartConfig.YAxisLabel);

        // Increment curves per state
        for (int s = 0; s < result.StateCount; s++)
        {
            var curve = result.IncrementCurves[s];
            var color = Color.FromHex(s < DefaultColors.Length
                ? DefaultColors[s] : DefaultColors[^1]);

            var scatter = plot.Add.Scatter(voltagesMv, curve);
            scatter.Color = color;
            scatter.LineWidth = 1.5f;
            scatter.LegendText = $"State {s}";
        }

        // 0.1% boundary lines
        if (chartConfig.ShowBoundaryLines)
        {
            foreach (var peak in result.StatePeaks)
            {
                if (peak.LeftBoundaryMv.HasValue)
                    plot.Add.VerticalLine(peak.LeftBoundaryMv.Value,
                        Colors.Gray, 1, LinePattern.Dash);

                if (peak.RightBoundaryMv.HasValue)
                    plot.Add.VerticalLine(peak.RightBoundaryMv.Value,
                        Colors.Gray, 1, LinePattern.Dash);
            }
        }

        // Best read voltage markers
        if (chartConfig.ShowReadVoltage)
        {
            foreach (var kvp in result.BestReadVoltages)
            {
                foreach (var v in kvp.Value)
                {
                    var vl = plot.Add.VerticalLine(v, Colors.Red, 1.5f, LinePattern.Solid);
                    vl.LegendText = "Best Vread";
                }
            }
        }

        // Legend
        if (chartConfig.ShowLegend)
            plot.ShowLegend();

        // Axis limits if set
        if (chartConfig.XMin < chartConfig.XMax)
            plot.Axes.SetLimitsX(chartConfig.XMin, chartConfig.XMax);
        if (chartConfig.YMin < chartConfig.YMax)
            plot.Axes.SetLimitsY(chartConfig.YMin, chartConfig.YMax);

        return plot;
    }
}

public class ChartConfig
{
    public string Title { get; set; } = "Vt Incremental Distribution";
    public string XAxisLabel { get; set; } = "Voltage Offset (mV)";
    public string YAxisLabel { get; set; } = "Cell Count";
    public double XMin { get; set; }
    public double XMax { get; set; } = 3000;
    public double YMin { get; set; }
    public double YMax { get; set; } = 10000;
    public bool ShowBoundaryLines { get; set; } = true;
    public bool ShowReadVoltage { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
}
