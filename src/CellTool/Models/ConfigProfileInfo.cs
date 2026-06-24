namespace CellTool.Models;

public class ConfigProfileInfo
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;

    public override string ToString() => Name;
}
