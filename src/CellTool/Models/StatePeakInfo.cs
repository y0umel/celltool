namespace CellTool.Models;

public class StatePeakInfo
{
    public int StateIndex { get; init; }
    public int TransitionIndex => StateIndex;
    public string Label { get; init; } = string.Empty;
    public double PeakCode { get; init; }
    public double PeakVoltageMv => PeakCode;
    public double? LeftBoundaryCode { get; init; }
    public double? LeftBoundaryMv => LeftBoundaryCode;
    public double? RightBoundaryCode { get; init; }
    public double? RightBoundaryMv => RightBoundaryCode;
    public int TotalCellCount { get; init; }
    public double PeakIncrementValue { get; init; }
    public double? AlignmentShiftMv { get; init; }
    public double? AlignmentScore { get; init; }
    public string ObservationSources { get; init; } = string.Empty;

    public double? WindowWidthCode =>
        LeftBoundaryCode.HasValue && RightBoundaryCode.HasValue
            ? RightBoundaryCode.Value - LeftBoundaryCode.Value
            : null;
    public double? WindowWidthMv => WindowWidthCode;
}
