namespace CellTool.Models;

public class ChartConfig
{
    public string Title { get; set; } = "Vt Incremental Distribution";
    public string XAxisLabel { get; set; } = "Read Offset Code (10mV/code)";
    public string YAxisLabel { get; set; } = "Cell Count";
    public double XMin { get; set; }
    public double XMax { get; set; }
    public double YMin { get; set; }
    public double YMax { get; set; }
    public bool ShowBoundaryLines { get; set; } = true;
    public bool ShowReadVoltage { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public bool ShowPeakOffsetAnnotations { get; set; }
    public bool UseSavitzkyGolaySmoothing { get; set; } = true;
    public int SavitzkyGolayWindow { get; set; } = 5;
}
