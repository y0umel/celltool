using System.IO;
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
        Save(filePath, new AppConfiguration { Analysis = config });
    }

    public AnalysisConfig Load(string filePath)
    {
        return Load<AppConfiguration>(filePath).Analysis;
    }

    /// <summary>
    /// Saves the full application configuration to JSON.
    /// </summary>
    public void Save(string filePath, AppConfiguration config)
    {
        Save(filePath, config as object);
    }

    /// <summary>
    /// Saves a JSON configuration document.
    /// </summary>
    public void Save<T>(string filePath, T config)
    {
        Save(filePath, config as object ?? throw new ArgumentNullException(nameof(config)));
    }

    /// <summary>
    /// Loads the full application configuration from JSON.
    /// </summary>
    public AppConfiguration LoadAppConfiguration(string filePath)
    {
        return Load<AppConfiguration>(filePath);
    }

    /// <summary>
    /// Loads a JSON configuration document.
    /// </summary>
    public T Load<T>(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidDataException("Failed to deserialize config file.");
    }

    private static void Save(string filePath, object config)
    {
        var json = JsonSerializer.Serialize(config, config.GetType(), JsonOptions);
        File.WriteAllText(filePath, json);
    }
}
