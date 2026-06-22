namespace CellTool.Models;

public class AnalysisConfig
{
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string ExcelFilePath { get; set; } = string.Empty;
    public string GroupModelPath { get; set; } = string.Empty;
    public string ReferenceFilePath { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string DieName { get; set; } = string.Empty;
    public int VoltageMinCode { get; set; } = -128;
    public int VoltageMaxCode { get; set; } = 127;
    public int VoltageStepCode { get; set; } = 1;
    public int VoltageMinMv { get => VoltageMinCode; set => VoltageMinCode = value; }
    public int VoltageMaxMv { get => VoltageMaxCode; set => VoltageMaxCode = value; }
    public int VoltageStepMv { get => VoltageStepCode; set => VoltageStepCode = value; }
    public int WlCount { get; set; } = 192;
    public int StartPage { get; set; } = 0;
    public string GrayCodeOrder { get; set; } = "U-M-L";
    public string SlcWlEncoding { get; set; } = string.Empty;
    public string MlcWlEncoding { get; set; } = string.Empty;
    public string TlcWlEncoding { get; set; } = string.Empty;
    public string QlcWlEncoding { get; set; } = string.Empty;
}
