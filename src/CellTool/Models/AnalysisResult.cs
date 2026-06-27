namespace CellTool.Models;

public class AnalysisResult
{
    public StatePeakInfo[] StatePeaks { get; init; } = Array.Empty<StatePeakInfo>();
    public Dictionary<int, double[]> BestReadVoltages { get; init; } = new();
    public CodewordErrorStat? BestVoltageErrors { get; init; }
    public CodewordErrorStat? ZeroOffsetErrors { get; init; }
    public double[][] IncrementCurves { get; init; } = Array.Empty<double[]>();
    public double[][] IncrementCurveXValues { get; init; } = Array.Empty<double[]>();
    public DistributionIntegralInfo[] DistributionIntegrals { get; init; } = Array.Empty<DistributionIntegralInfo>();
    public ErrorTypeDiagnosticInfo[] ErrorTypeDiagnostics { get; init; } = Array.Empty<ErrorTypeDiagnosticInfo>();
    public double[] VoltageCodes { get; init; } = Array.Empty<double>();
    public double[] VoltagesMv => VoltageCodes;
    public string[] TransitionLabels { get; init; } = Array.Empty<string>();
    public LevelSpacingSuggestionInfo? LevelSpacingSuggestion { get; init; }
    public int[] GroundTruth { get; init; } = Array.Empty<int>();
    public int TotalCells { get; init; }
    public int VoltageCount { get; init; }
    public int StateCount { get; init; }
}

public class LevelSpacingSuggestionInfo
{
    public double CurrentSpacingCode { get; init; }
    public double SuggestedSpacingCode { get; init; }
    public double Confidence { get; init; }
    public string ConfidenceLabel { get; init; } = string.Empty;
    public string Diagnostic { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public double MedianGapCode { get; init; }
    public double MaxDeviationCode { get; init; }
    public double StandardDeviationCode { get; init; }
    public LevelSpacingEstimateInfo[] Items { get; init; } = Array.Empty<LevelSpacingEstimateInfo>();
}

public class LevelSpacingEstimateInfo
{
    public int LevelIndex { get; init; }
    public string Label { get; init; } = string.Empty;
    public double SuggestedSpacingCode { get; init; }
    public int SampleCount { get; init; }
    public double MedianGapCode { get; init; }
    public double MaxDeviationCode { get; init; }
    public double StandardDeviationCode { get; init; }
    public double Confidence { get; init; }
    public string ConfidenceLabel { get; init; } = string.Empty;
}

public class DistributionIntegralInfo
{
    public int LevelIndex { get; init; }
    public string Label { get; init; } = string.Empty;
    public int SourceCellCount { get; init; }
    public double RawObservedIntegral { get; init; }
    public double DisplayObservedIntegral { get; init; }
    public double LeftOutOfRangeEstimate { get; init; }
    public double RightOutOfRangeEstimate { get; init; }
    public double UnclassifiedOutOfRangeEstimate { get; init; }
    public int LeftBoundaryObservedCount { get; init; }
    public int RightBoundaryObservedCount { get; init; }
    public int BothBoundaryObservedCount { get; init; }
    public int EndpointObservedCount { get; init; }
    public double ClippedIntegral => Math.Max(0, RawObservedIntegral - DisplayObservedIntegral);
    public double RawIntegralDeltaFromSource => RawObservedIntegral - SourceCellCount;
    public double DisplayIntegralDeltaFromSource => DisplayObservedIntegral - SourceCellCount;
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
