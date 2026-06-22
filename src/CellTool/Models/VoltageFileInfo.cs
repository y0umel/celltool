using System.IO;
namespace CellTool.Models;

public class VoltageFileInfo : IComparable<VoltageFileInfo>
{
    public string FilePath { get; init; } = string.Empty;
    public int Code { get; init; }
    public int OffsetMv => Code;

    public int CompareTo(VoltageFileInfo? other) =>
        other is null ? 1 : Code.CompareTo(other.Code);

    public override string ToString() =>
        $"{Path.GetFileName(FilePath)} (code {Code})";
}
