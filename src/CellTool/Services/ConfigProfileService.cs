using System.IO;
using CellTool.Models;

namespace CellTool.Services;

public class ConfigProfileService
{
    private const string DefaultProfileName = "默认配置";
    private readonly ConfigPersistence persistence = new();

    public IReadOnlyList<ConfigProfileInfo> EnsureProfiles(AppConfiguration initialConfiguration)
    {
        Directory.CreateDirectory(AppPaths.ProfilesDirectory);
        var profiles = ListProfiles();
        if (profiles.Count > 0)
            return profiles;

        var path = BuildProfilePath(DefaultProfileName);
        persistence.Save(path, initialConfiguration);
        return ListProfiles();
    }

    public IReadOnlyList<ConfigProfileInfo> ListProfiles()
    {
        if (!Directory.Exists(AppPaths.ProfilesDirectory))
            return Array.Empty<ConfigProfileInfo>();

        return Directory.EnumerateFiles(AppPaths.ProfilesDirectory, "*.json")
            .Select(path => new ConfigProfileInfo
            {
                Name = Path.GetFileNameWithoutExtension(path),
                FilePath = path
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AppConfiguration Load(ConfigProfileInfo profile)
    {
        return persistence.LoadAppConfiguration(profile.FilePath);
    }

    public void Save(ConfigProfileInfo profile, AppConfiguration configuration)
    {
        persistence.Save(profile.FilePath, configuration);
    }

    public ConfigProfileInfo Create(string name, AppConfiguration configuration)
    {
        string safeName = ValidateProfileName(name);
        string path = BuildProfilePath(safeName);
        if (File.Exists(path))
            throw new InvalidOperationException("同名配置已存在。");

        persistence.Save(path, configuration);
        return new ConfigProfileInfo { Name = safeName, FilePath = path };
    }

    public ConfigProfileInfo Rename(ConfigProfileInfo profile, string newName)
    {
        string safeName = ValidateProfileName(newName);
        string newPath = BuildProfilePath(safeName);
        if (File.Exists(newPath))
            throw new InvalidOperationException("同名配置已存在。");

        File.Move(profile.FilePath, newPath);
        return new ConfigProfileInfo { Name = safeName, FilePath = newPath };
    }

    private static string BuildProfilePath(string name)
    {
        return Path.Combine(AppPaths.ProfilesDirectory, $"{name}.json");
    }

    private static string ValidateProfileName(string name)
    {
        string trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("配置名称不能为空。");

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("配置名称包含非法字符。");

        return trimmed;
    }
}
