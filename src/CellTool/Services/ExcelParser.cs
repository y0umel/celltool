using System.Globalization;
using System.IO;
using System.Text;
using CellTool.Models;
using OfficeOpenXml;

namespace CellTool.Services;

public class ExcelParser
{
    /// <summary>
    /// Loads chip database rows from an Excel workbook or CSV file.
    /// </summary>
    public List<ChipInfo> LoadDatabase(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Chip database file not found: {filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => LoadCsvDatabase(filePath),
            ".txt" => LoadCsvDatabase(filePath),
            ".xlsx" or ".xls" => LoadExcelDatabase(filePath),
            _ => throw new NotSupportedException($"Unsupported chip database format: {extension}")
        };
    }

    private List<ChipInfo> LoadExcelDatabase(string filePath)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets[0];

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int colCount = sheet.Dimension?.Columns ?? 1;
        for (int col = 1; col <= colCount; col++)
        {
            var header = NormalizeHeader(sheet.Cells[1, col].Text);
            if (!string.IsNullOrEmpty(header))
                headerMap[header] = col;
        }

        var chips = new List<ChipInfo>();
        int rowCount = sheet.Dimension?.Rows ?? 1;
        for (int row = 2; row <= rowCount; row++)
        {
            try
            {
                var chip = ParseRow(name => GetExcelString(sheet, headerMap, row, name));
                if (chip is not null)
                    chips.Add(chip);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Skipping row {row} due to parse error: {ex.Message}");
            }
        }

        return chips;
    }

    private List<ChipInfo> LoadCsvDatabase(string filePath)
    {
        var lines = File.ReadAllLines(filePath, DetectEncoding(filePath))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
            return new List<ChipInfo>();

        var headers = ParseCsvLine(lines[0])
            .Select(NormalizeHeader)
            .ToArray();

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(headers[i]))
                headerMap[headers[i]] = i;
        }

        var chips = new List<ChipInfo>();
        for (int row = 1; row < lines.Length; row++)
        {
            var fields = ParseCsvLine(lines[row]);
            if (TryParseCsvRow(fields, headerMap, out var chip))
                chips.Add(chip);
        }

        return chips;
    }

    private static bool TryParseCsvRow(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers,
        out ChipInfo chip)
    {
        chip = default!;

        if (!TryGetCsvString(fields, headers, "die简称", out var dieName) ||
            string.IsNullOrWhiteSpace(dieName))
        {
            return false;
        }

        if (!TryGetCsvString(fields, headers, "xLC", out var xlcStr) ||
            !TryParseXlcType(xlcStr, out var type) ||
            !TryGetCsvInt(fields, headers, "页 数据 Byte", out int pageDataBytes) ||
            !TryGetCsvInt(fields, headers, "页 冗余 Byte", out int pageRedundantBytes) ||
            !TryGetCsvInt(fields, headers, "1KB Frame 个数 KB", out int frameCount) ||
            !TryGetCsvInt(fields, headers, "块大小 （页数量）", out int blockSizePages) ||
            !TryGetCsvInt(fields, headers, "WL/Block", out int wlPerBlock) ||
            !TryGetCsvIntList(fields, headers, "WL编码", out var wlEncoding))
        {
            return false;
        }

        chip = new ChipInfo
        {
            Manufacturer = ReadCsvManufacturer(fields, headers),
            DieName = dieName.Trim(),
            Type = type,
            PageDataBytes = pageDataBytes,
            PageRedundantBytes = pageRedundantBytes,
            FrameCount = frameCount,
            BlockSizePages = blockSizePages,
            WlPerBlock = wlPerBlock,
            WlEncoding = wlEncoding
        };

        return true;
    }

    private ChipInfo? ParseRow(Func<string, string> getString)
    {
        var dieName = getString("die简称");
        if (string.IsNullOrWhiteSpace(dieName))
            return null;

        string xlcStr = getString("xLC").Trim();
        if (!TryParseXlcType(xlcStr, out var type))
            throw new FormatException($"Unknown xLC type: {xlcStr}");

        int pageDataBytes = ParseInt(getString("页 数据 Byte"), "页 数据 Byte");
        int pageRedundantBytes = ParseInt(getString("页 冗余 Byte"), "页 冗余 Byte");
        int frameCount = ParseInt(getString("1KB Frame 个数 KB"), "1KB Frame 个数 KB");
        int blockSizePages = ParseInt(getString("块大小 （页数量）"), "块大小 （页数量）");
        int wlPerBlock = ParseInt(getString("WL/Block"), "WL/Block");
        int[] wlEncoding = ParseIntList(getString("WL编码"));

        return new ChipInfo
        {
            Manufacturer = ReadManufacturer(getString),
            DieName = dieName.Trim(),
            Type = type,
            PageDataBytes = pageDataBytes,
            PageRedundantBytes = pageRedundantBytes,
            FrameCount = frameCount,
            BlockSizePages = blockSizePages,
            WlPerBlock = wlPerBlock,
            WlEncoding = wlEncoding
        };
    }

    private static string GetExcelString(
        ExcelWorksheet sheet,
        Dictionary<string, int> headers,
        int row,
        string colName)
    {
        var key = NormalizeHeader(colName);
        if (!headers.TryGetValue(key, out int col))
            throw new InvalidDataException($"Column '{colName}' not found in chip database.");

        return sheet.Cells[row, col].Text.Trim();
    }

    private static string GetCsvString(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers,
        int row,
        string colName)
    {
        var key = NormalizeHeader(colName);
        if (!headers.TryGetValue(key, out int col))
            throw new InvalidDataException($"Column '{colName}' not found in chip database.");
        if (col >= fields.Count)
            throw new InvalidDataException($"Column '{colName}' missing at CSV row {row}.");

        return fields[col].Trim();
    }

    private static bool TryGetCsvString(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers,
        string colName,
        out string value)
    {
        value = string.Empty;
        var key = NormalizeHeader(colName);
        if (!headers.TryGetValue(key, out int col) || col >= fields.Count)
            return false;

        value = fields[col].Trim();
        return true;
    }

    private static bool TryGetCsvInt(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers,
        string colName,
        out int value)
    {
        value = 0;
        return TryGetCsvString(fields, headers, colName, out var text) &&
               TryParseInt(text, out value);
    }

    private static bool TryGetCsvIntList(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers,
        string colName,
        out int[] value)
    {
        value = Array.Empty<int>();
        var key = NormalizeHeader(colName);
        if (!headers.TryGetValue(key, out int col) || col >= fields.Count)
            return false;

        var sourceFields = string.Equals(colName, "WL编码", StringComparison.OrdinalIgnoreCase)
            ? fields.Skip(col)
            : [fields[col]];

        var parsed = new List<int>();
        foreach (var sourceField in sourceFields)
        {
            var parts = sourceField.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    value = parsed.ToArray();
                    return value.Length > 0;
                }

                parsed.Add(number);
            }
        }

        value = parsed.ToArray();
        return value.Length > 0;
    }

    private static string ReadCsvManufacturer(
        IReadOnlyList<string> fields,
        Dictionary<string, int> headers)
    {
        foreach (var columnName in ManufacturerColumnNames)
        {
            if (TryGetCsvString(fields, headers, columnName, out var manufacturer) &&
                !string.IsNullOrWhiteSpace(manufacturer))
            {
                return manufacturer.Trim();
            }
        }

        return ChipInfo.UnknownManufacturer;
    }

    private static string ReadManufacturer(Func<string, string> getString)
    {
        foreach (var columnName in ManufacturerColumnNames)
        {
            try
            {
                var manufacturer = getString(columnName);
                if (!string.IsNullOrWhiteSpace(manufacturer))
                    return manufacturer.Trim();
            }
            catch (InvalidDataException)
            {
                // Manufacturer is optional for backward compatibility with older chip tables.
            }
        }

        return ChipInfo.UnknownManufacturer;
    }

    private static int ParseInt(string text, string colName)
    {
        if (TryParseInt(text, out int val))
            return val;

        throw new FormatException($"Column '{colName}': cannot parse '{text}' as int.");
    }

    private static bool TryParseInt(string text, out int value)
    {
        var normalized = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseXlcType(string text, out XlcType type)
    {
        type = XlcType.SLC;
        switch (text.Trim().ToUpperInvariant())
        {
            case "SLC":
            case "1":
            case "1LC":
                type = XlcType.SLC;
                return true;
            case "MLC":
            case "2":
            case "2LC":
                type = XlcType.MLC;
                return true;
            case "TLC":
            case "3":
            case "3LC":
                type = XlcType.TLC;
                return true;
            case "QLC":
            case "4":
            case "4LC":
                type = XlcType.QLC;
                return true;
            default:
                return false;
        }
    }

    private static int[] ParseIntList(string text)
    {
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
                   .ToArray();
    }

    private static string NormalizeHeader(string text)
    {
        return text.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }

    private static Encoding DetectEncoding(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Span<byte> bom = stackalloc byte[3];
        int read = stream.Read(bom);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        return Encoding.UTF8;
    }

    private static readonly string[] ManufacturerColumnNames =
    [
        "厂家",
        "厂商",
        "供应商",
        "Factory",
        "Manufacturer",
        "Vendor"
    ];
}
