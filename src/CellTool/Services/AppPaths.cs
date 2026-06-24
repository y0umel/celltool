using System.IO;

namespace CellTool.Services;

public static class AppPaths
{
    public static string AppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CellTool");

    public static string ProfilesDirectory => Path.Combine(AppDataDirectory, "profiles");

    public static string UserChipDatabasePath => Path.Combine(AppDataDirectory, "chip-database.json");
}
