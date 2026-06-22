namespace CellTool.Models;

public class AppConfiguration
{
    public AnalysisConfig Analysis { get; set; } = new();
    public ChartConfig Chart { get; set; } = new();
    public bool IsDarkTheme { get; set; } = true;
}
