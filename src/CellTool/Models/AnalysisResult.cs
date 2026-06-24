namespace CellTool.Models;

public class AnalysisResult
{
    public StatePeakInfo[] StatePeaks { get; init; } = Array.Empty<StatePeakInfo>();
    public Dictionary<int, double[]> BestReadVoltages { get; init; } = new();
    public CodewordErrorStat? BestVoltageErrors { get; init; }
    public CodewordErrorStat? ZeroOffsetErrors { get; init; }
    public double[][] IncrementCurves { get; init; } = Array.Empty<double[]>();
    public double[][] IncrementCurveXValues { get; init; } = Array.Empty<double[]>();
    public ErrorTypeDiagnosticInfo[] ErrorTypeDiagnostics { get; init; } = Array.Empty<ErrorTypeDiagnosticInfo>();
    public double[] VoltageCodes { get; init; } = Array.Empty<double>();
    public double[] VoltagesMv => VoltageCodes;
    public string[] TransitionLabels { get; init; } = Array.Empty<string>();
    public int[] GroundTruth { get; init; } = Array.Empty<int>();
    public int TotalCells { get; init; }
    public int VoltageCount { get; init; }
    public int StateCount { get; init; }
}

public class ErrorTypeDiagnosticInfo
{
    public int SourceLevel { get; init; }
    public int CurrentLevel { get; init; }
    public bool IsAdjacent { get; init; }
    public double PeakOffsetMv { get; init; }
    public double PeakCellCount { get; init; }
    public double DeltaPeakOffsetMv { get; init; }
    public double DeltaPeakCellCount { get; init; }
    public double ReadBoundaryMv { get; init; }
    public double PeakSourceRatio { get; init; }
    public int BoundaryIndex { get; init; } = -1;
    public string BoundaryLabel { get; init; } = string.Empty;
    public int TargetLevel { get; init; } = -1;
    public string PageName { get; init; } = string.Empty;
    public string BitDirection { get; init; } = string.Empty;
    public string ContextLabel { get; init; } = string.Empty;
    public int LeftRawGray { get; init; } = -1;
    public int RightRawGray { get; init; } = -1;
    public bool IsValidBoundary { get; init; } = true;
    public string Note { get; init; } = string.Empty;
}
