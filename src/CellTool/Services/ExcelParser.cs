using System.Text;
using CellTool.Models;
using OfficeOpenXml;

namespace CellTool.Services;

public class ExcelParser
{
    public List<ChipInfo> LoadDatabase(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Excel file not found: {filePath}");

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets[0];

        // Build header map: column name -> column index (1-based)
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int colCount = sheet.Dimension?.Columns ?? 1;
        for (int col = 1; col <= colCount; col++)
        {
            var header = sheet.Cells[1, col].Text.Trim();
            if (!string.IsNullOrEmpty(header))
                headerMap[header] = col;
        }

        var chips = new List<ChipInfo>();
        int rowCount = sheet.Dimension?.Rows ?? 1;
        for (int row = 2; row <= rowCount; row++)
        {
            try
            {
                var chip = ParseRow(sheet, headerMap, row);
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

    private ChipInfo? ParseRow(ExcelWorksheet sheet, Dictionary<string, int> headers, int row)
    {
        var dieName = GetString(sheet, headers, row, "die简称");
        if (string.IsNullOrEmpty(dieName))
            return null;

        string xlcStr = GetString(sheet, headers, row, "xLC");
        var type = xlcStr.ToUpperInvariant() switch
        {
            "SLC" => XlcType.SLC,
            "MLC" => XlcType.MLC,
            "TLC" => XlcType.TLC,
            "QLC" => XlcType.QLC,
            _ => throw new FormatException($"Unknown xLC type: {xlcStr}")
        };

        int pageDataBytes = GetInt(sheet, headers, row, "页 数据 Byte");
        int pageRedundantBytes = GetInt(sheet, headers, row, "页 冗余 Byte");
        int frameCount = GetInt(sheet, headers, row, "1KB Frame 个数 KB");
        int blockSizePages = GetInt(sheet, headers, row, "块大小 （页数量）");
        int wlPerBlock = GetInt(sheet, headers, row, "WL/Block");
        int[] wlEncoding = ParseIntList(GetString(sheet, headers, row, "WL编码"));

        return new ChipInfo
        {
            DieName = dieName,
            Type = type,
            PageDataBytes = pageDataBytes,
            PageRedundantBytes = pageRedundantBytes,
            FrameCount = frameCount,
            BlockSizePages = blockSizePages,
            WlPerBlock = wlPerBlock,
            WlEncoding = wlEncoding
        };
    }

    private string GetString(ExcelWorksheet sheet, Dictionary<string, int> headers, int row, string colName)
    {
        if (!headers.TryGetValue(colName, out int col))
            throw new InvalidDataException($"Column '{colName}' not found in Excel.");
        return sheet.Cells[row, col].Text.Trim();
    }

    private int GetInt(ExcelWorksheet sheet, Dictionary<string, int> headers, int row, string colName)
    {
        var text = GetString(sheet, headers, row, colName);
        if (!int.TryParse(text, out int val))
            throw new FormatException($"Column '{colName}' row {row}: cannot parse '{text}' as int.");
        return val;
    }

    private int[] ParseIntList(string text)
    {
        return text.Split(',')
                   .Select(s => int.Parse(s.Trim()))
                   .ToArray();
    }
}
