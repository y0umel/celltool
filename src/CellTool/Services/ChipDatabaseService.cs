using System.IO;
using System.Text.Json;
using CellTool.Models;

namespace CellTool.Services;

public class ChipDatabaseService
{
    private const int CurrentUserDatabaseVersion = 2;

    private static readonly string DefaultBundledDatabasePath =
        Path.Combine(AppContext.BaseDirectory, "Resources", "chip-database.default.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ExcelParser parser = new();
    private readonly string userDatabasePath;
    private readonly string bundledDatabasePath;

    public ChipDatabaseService(string? userDatabasePath = null, string? bundledDatabasePath = null)
    {
        this.userDatabasePath = userDatabasePath ?? AppPaths.UserChipDatabasePath;
        this.bundledDatabasePath = bundledDatabasePath ?? DefaultBundledDatabasePath;
    }

    public IReadOnlyList<ChipInfo> Load()
    {
        var userChips = File.Exists(userDatabasePath)
            ? NormalizeChips(LoadJson(userDatabasePath))
            : Array.Empty<ChipInfo>();
        var bundledChips = File.Exists(bundledDatabasePath)
            ? NormalizeChips(LoadJson(bundledDatabasePath))
            : Array.Empty<ChipInfo>();

        if (ShouldUseUserDatabase(userChips, bundledChips))
            return userChips;

        if (bundledChips.Length > 0)
            return bundledChips;

        if (userChips.Length > 0)
            return userChips;

        return FallbackChips();
    }

    public IReadOnlyList<ChipInfo> ImportFromFile(string sourcePath)
    {
        var chips = NormalizeChips(parser.LoadDatabase(sourcePath));
        if (chips.Length == 0)
            throw new InvalidDataException("厂家表中没有可用芯片记录。");

        var targetDirectory = Path.GetDirectoryName(userDatabasePath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        SaveJson(userDatabasePath, chips);
        SaveMetadata();
        return chips;
    }

    private bool ShouldUseUserDatabase(ChipInfo[] userChips, ChipInfo[] bundledChips)
    {
        if (!HasSpecificManufacturer(userChips))
            return false;

        if (IsCurrentUserDatabase())
            return true;

        return !LooksLikeLegacyFilteredSubset(userChips, bundledChips);
    }

    private bool IsCurrentUserDatabase()
    {
        var metadataPath = UserDatabaseMetadataPath();
        if (!File.Exists(metadataPath))
            return false;

        try
        {
            var metadata = JsonSerializer.Deserialize<ChipDatabaseMetadata>(
                File.ReadAllText(metadataPath),
                JsonOptions);
            return metadata?.Version == CurrentUserDatabaseVersion;
        }
        catch
        {
            return false;
        }
    }

    private void SaveMetadata()
    {
        var metadata = new ChipDatabaseMetadata { Version = CurrentUserDatabaseVersion };
        File.WriteAllText(UserDatabaseMetadataPath(), JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private string UserDatabaseMetadataPath() => $"{userDatabasePath}.meta.json";

    private static bool LooksLikeLegacyFilteredSubset(ChipInfo[] userChips, ChipInfo[] bundledChips)
    {
        if (userChips.Length == 0 ||
            bundledChips.Length <= userChips.Length ||
            userChips.Any(chip =>
                chip.PageDataBytes is not > 0 ||
                chip.PageRedundantBytes is null or < 0 ||
                chip.FrameCount is not > 0 ||
                chip.WlPerBlock is not > 0 ||
                chip.WlEncoding.Length == 0))
        {
            return false;
        }

        var bundledKeys = bundledChips
            .Select(ChipKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return userChips.All(chip => bundledKeys.Contains(ChipKey(chip)));
    }

    private static string ChipKey(ChipInfo chip) =>
        $"{NormalizeManufacturer(chip.Manufacturer)}\n{chip.DieName.Trim()}";

    private static IReadOnlyList<ChipInfo> LoadJson(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ChipInfo>>(json, JsonOptions)
               ?? new List<ChipInfo>();
    }

    private static void SaveJson(string path, IReadOnlyList<ChipInfo> chips)
    {
        var json = JsonSerializer.Serialize(chips, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static ChipInfo[] NormalizeChips(IEnumerable<ChipInfo> chips)
    {
        return chips
            .Where(IsUsableChip)
            .GroupBy(
                chip => (NormalizeManufacturer(chip.Manufacturer), chip.DieName.Trim()),
                StringTupleComparer.OrdinalIgnoreCase)
            .Select(group => NormalizeChip(group.First()))
            .OrderBy(chip => chip.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(chip => chip.DieName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUsableChip(ChipInfo chip)
    {
        return !string.IsNullOrWhiteSpace(chip.DieName) &&
               (int)chip.Type >= (int)XlcType.SLC &&
               (int)chip.Type <= (int)XlcType.QLC;
    }

    private static bool HasSpecificManufacturer(IEnumerable<ChipInfo> chips) =>
        chips.Any(chip =>
            !string.Equals(
                NormalizeManufacturer(chip.Manufacturer),
                ChipInfo.UnknownManufacturer,
                StringComparison.OrdinalIgnoreCase));

    private static ChipInfo NormalizeChip(ChipInfo chip)
    {
        return new ChipInfo
        {
            Manufacturer = NormalizeManufacturer(chip.Manufacturer),
            DieName = chip.DieName.Trim(),
            Type = chip.Type,
            PageDataBytes = chip.PageDataBytes,
            PageRedundantBytes = chip.PageRedundantBytes,
            FrameCount = chip.FrameCount,
            BlockSizePages = chip.BlockSizePages,
            WlPerBlock = chip.WlPerBlock,
            WlEncoding = chip.WlEncoding ?? Array.Empty<int>()
        };
    }

    private static string NormalizeManufacturer(string? manufacturer) =>
        string.IsNullOrWhiteSpace(manufacturer)
            ? ChipInfo.UnknownManufacturer
            : manufacturer.Trim();

    private static IReadOnlyList<ChipInfo> FallbackChips()
    {
        return
        [
            new ChipInfo
            {
                Manufacturer = "YMTC",
                DieName = "X4-9060(Client)",
                Type = XlcType.TLC,
                PageDataBytes = 49152,
                PageRedundantBytes = 6144,
                FrameCount = 48,
                BlockSizePages = 2304,
                WlPerBlock = 192,
                WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
            },
            new ChipInfo
            {
                Manufacturer = "Toshiba",
                DieName = "G8T22",
                Type = XlcType.TLC,
                PageDataBytes = 16384,
                PageRedundantBytes = 1952,
                FrameCount = 16,
                BlockSizePages = 576,
                WlPerBlock = 192,
                WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
            },
            new ChipInfo
            {
                Manufacturer = "Toshiba",
                DieName = "G7D22",
                Type = XlcType.MLC,
                PageDataBytes = 16384,
                PageRedundantBytes = 1280,
                FrameCount = 16,
                BlockSizePages = 384,
                WlPerBlock = 192,
                WlEncoding = [3, 1, 0, 2]
            }
        ];
    }

    private sealed class ChipDatabaseMetadata
    {
        public int Version { get; init; }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string First, string Second)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals((string First, string Second) x, (string First, string Second) y) =>
            string.Equals(x.First, y.First, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Second, y.Second, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string First, string Second) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.First),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Second));
    }
}
