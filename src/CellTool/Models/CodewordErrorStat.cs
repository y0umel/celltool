namespace CellTool.Models;

public class CodewordErrorStat
{
    public int MaxBitErrors { get; init; }
    public double AvgBitErrors { get; init; }
    public int TotalCodewords { get; init; }
    public int[] ErrorCounts { get; init; } = Array.Empty<int>();
}
