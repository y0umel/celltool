namespace CellTool.Models;

public class GroupModel
{
    public List<GroupEntry> Entries { get; init; } = new();
    public int WlCount => Entries.Count;
}

public class GroupEntry
{
    public int WlIndex { get; init; }
    public int[] RawValues { get; init; } = Array.Empty<int>();
    public int[] PageIndices { get; init; } = Array.Empty<int>();
}
