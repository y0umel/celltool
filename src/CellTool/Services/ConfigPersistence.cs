using System.Text.Json;
using CellTool.Models;

namespace CellTool.Services;

public class ConfigPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Save(string filePath, AnalysisConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public AnalysisConfig Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AnalysisConfig>(json, JsonOptions)
               ?? throw new InvalidDataException("Failed to deserialize config file.");
    }
}
