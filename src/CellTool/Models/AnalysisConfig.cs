namespace CellTool.Models;

public class AnalysisConfig
{
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string ExcelFilePath { get; set; } = string.Empty;
    public string GroupModelPath { get; set; } = string.Empty;
    public string ReferenceFilePath { get; set; } = string.Empty;
    public string DieName { get; set; } = string.Empty;
    public int VoltageMinMv { get; set; } = -3000;
    public int VoltageMaxMv { get; set; } = 3000;
    public int VoltageStepMv { get; set; } = 10;
    public int WlCount { get; set; } = 192;
    public int StartPage { get; set; } = 0;
    public string GrayCodeMsb { get; set; } = "U";
    public string GrayCodeLsb { get; set; } = "L";
}
