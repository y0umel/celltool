namespace CellTool.Models;

public class StatePeakInfo
{
    public int StateIndex { get; init; }
    public double PeakVoltageMv { get; init; }
    public double? LeftBoundaryMv { get; init; }
    public double? RightBoundaryMv { get; init; }
    public int TotalCellCount { get; init; }
    public double PeakIncrementValue { get; init; }

    public double? WindowWidthMv =>
        LeftBoundaryMv.HasValue && RightBoundaryMv.HasValue
            ? RightBoundaryMv.Value - LeftBoundaryMv.Value
            : null;
}
