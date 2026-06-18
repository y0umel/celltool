namespace CellTool.Models;

public class VoltageFileInfo : IComparable<VoltageFileInfo>
{
    public string FilePath { get; init; } = string.Empty;
    public int OffsetMv { get; init; }

    public int CompareTo(VoltageFileInfo? other) =>
        other is null ? 1 : OffsetMv.CompareTo(other.OffsetMv);

    public override string ToString() =>
        $"{Path.GetFileName(FilePath)} ({OffsetMv} mV)";
}
