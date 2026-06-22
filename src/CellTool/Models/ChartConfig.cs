namespace CellTool.Models;

public class ChartConfig
{
    public string Title { get; set; } = "Vt Incremental Distribution";
    public string XAxisLabel { get; set; } = "Voltage Code from Lower Bound (1 code = 10mV)";
    public string YAxisLabel { get; set; } = "Cell Count";
    public double XMin { get; set; }
    public double XMax { get; set; }
    public double YMin { get; set; }
    public double YMax { get; set; } = 10000;
    public bool ShowBoundaryLines { get; set; } = true;
    public bool ShowReadVoltage { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
}
