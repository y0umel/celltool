namespace CellTool.Models;

public class AnalysisResult
{
    public StatePeakInfo[] StatePeaks { get; init; } = Array.Empty<StatePeakInfo>();
    public Dictionary<int, double[]> BestReadVoltages { get; init; } = new();
    public CodewordErrorStat? BestVoltageErrors { get; init; }
    public CodewordErrorStat? ZeroOffsetErrors { get; init; }
    public double[][] IncrementCurves { get; init; } = Array.Empty<double[]>();
    public double[] VoltageCodes { get; init; } = Array.Empty<double>();
    public double[] VoltagesMv => VoltageCodes;
    public string[] TransitionLabels { get; init; } = Array.Empty<string>();
    public int[] GroundTruth { get; init; } = Array.Empty<int>();
    public int TotalCells { get; init; }
    public int VoltageCount { get; init; }
    public int StateCount { get; init; }
}
