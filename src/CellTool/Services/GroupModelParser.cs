using System.IO;
using System.Text;
using CellTool.Models;

namespace CellTool.Services;

public class GroupModelParser
{
    public GroupModel LoadFromFile(string filePath, int expectedWlCount)
    {
        return LoadFromFile(filePath, expectedWlCount, expectedValidPagesPerWl: null);
    }

    public GroupModel LoadFromFile(string filePath, int expectedWlCount, int? expectedValidPagesPerWl)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"GroupModel file not found: {filePath}");

        var lines = File.ReadAllLines(filePath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

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

            if (expectedValidPagesPerWl.HasValue && validPages.Count != expectedValidPagesPerWl.Value)
            {
                throw new InvalidDataException(
                    $"Line {i + 1}: expected {expectedValidPagesPerWl.Value} valid page indices, got {validPages.Count}.");
            }
            if (!expectedValidPagesPerWl.HasValue && (validPages.Count < 1 || validPages.Count > 4))
            {
                throw new InvalidDataException(
                    $"Line {i + 1}: expected 1 to 4 valid page indices for SLC/MLC/TLC/QLC mode, got {validPages.Count}.");
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
