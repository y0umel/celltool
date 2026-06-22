namespace CellTool.Models;

public enum XlcType
{
    SLC = 1,
    MLC = 2,
    TLC = 3,
    QLC = 4
}

public class ChipInfo
{
    public const string UnknownManufacturer = "未指定厂家";

    public string Manufacturer { get; init; } = UnknownManufacturer;
    public string DieName { get; init; } = string.Empty;
    public XlcType Type { get; init; }
    public int PageDataBytes { get; init; }
    public int PageRedundantBytes { get; init; }
    public int FrameCount { get; init; }
    public int BlockSizePages { get; init; }
    public int WlPerBlock { get; init; }
    public int[] WlEncoding { get; init; } = Array.Empty<int>();

    public int PageTotalBytes => PageDataBytes + PageRedundantBytes;
    public int CodewordBytes => PageTotalBytes / FrameCount;
    public int StateCount => 1 << (int)Type;
    public int BitsPerCell => (int)Type;

    public override string ToString() => $"{Manufacturer} {DieName} ({Type}, {StateCount} states)";
}
