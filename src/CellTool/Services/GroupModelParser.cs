using System.Text;
using CellTool.Models;

namespace CellTool.Services;

public class GroupModelParser
{
    public GroupModel LoadFromFile(string filePath, int expectedWlCount)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"GroupModel file not found: {filePath}");

        var lines = File.ReadAllLines(filePath, Encoding.UTF8);

        if (lines.Length != expectedWlCount)
            throw new InvalidDataException(
                $"GroupModel row count mismatch: expected {expectedWlCount}, got {lines.Length}");

        var entries = new List<GroupEntry>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            var rawValues = new int[parts.Length];
            var validPages = new List<int>();

            for (int j = 0; j < parts.Length; j++)
            {
                var trimmed = parts[j].Trim();
                if (int.TryParse(trimmed, out int val))
                {
                    rawValues[j] = val;
                    if (val >= 0)
                        validPages.Add(val);
                }
                else
                {
                    throw new FormatException(
                        $"Invalid value '{trimmed}' at line {i + 1}, column {j + 1}");
                }
            }

            entries.Add(new GroupEntry
            {
                WlIndex = i,
                RawValues = rawValues,
                PageIndices = validPages.ToArray()
            });
        }

        return new GroupModel { Entries = entries };
    }
}
