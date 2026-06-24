namespace CellTool.Models;

public class LimitMissStat
{
    public string Label { get; init; } = string.Empty;
    public int LeftOutOfRange { get; init; }
    public int RightOutOfRange { get; init; }
}
