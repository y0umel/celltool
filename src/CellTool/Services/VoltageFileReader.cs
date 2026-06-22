using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text.RegularExpressions;
using CellTool.Models;

namespace CellTool.Services;

public partial class VoltageFileReader
{
    [GeneratedRegex(@"^(-?\d+)(?:\.bin)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    public List<VoltageFileInfo> ScanDirectory(string directoryPath)
    {
        return ScanDirectory(directoryPath, null, null, null);
    }

    public VoltageFileScanResult ScanDirectoryDetailed(
        string directoryPath,
        int? minMv,
        int? maxMv,
        int? stepMv)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var result = new VoltageFileScanResult();
        foreach (var path in Directory.GetFiles(directoryPath).Where(IsVoltageFileCandidate))
        {
            result.TotalCandidateFiles++;
            var name = Path.GetFileName(path);
            var match = FileNamePattern().Match(name);
            if (!match.Success)
            {
                result.NameMismatchFiles++;
                continue;
            }

            int code = int.Parse(match.Groups[1].Value);
            if (minMv.HasValue && code < minMv.Value)
            {
                result.RangeFilteredFiles++;
                continue;
            }
            if (maxMv.HasValue && code > maxMv.Value)
            {
                result.RangeFilteredFiles++;
                continue;
            }
            if (stepMv.HasValue && stepMv.Value > 0 && minMv.HasValue &&
                (code - minMv.Value) % stepMv.Value != 0)
            {
                result.StepFilteredFiles++;
                continue;
            }

            result.Files.Add(new VoltageFileInfo { FilePath = path, Code = code });
        }

        result.Files.Sort();
        return result;
    }

    private static bool IsVoltageFileCandidate(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ||
            string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase);
    }

    public List<VoltageFileInfo> ScanDirectory(
        string directoryPath,
        int? minMv,
        int? maxMv,
        int? stepMv)
    {
        return ScanDirectoryDetailed(directoryPath, minMv, maxMv, stepMv).Files;
    }

    public byte[] ReadWlBytes(string filePath, GroupEntry groupEntry, int pageTotalBytes)
    {
        return ReadWlBytes(filePath, groupEntry, pageTotalBytes, 0);
    }

    public byte[] ReadWlBytes(string filePath, GroupEntry groupEntry, int pageTotalBytes, int startPage)
    {
        var pageCount = groupEntry.PageIndices.Length;
        var wlByteSize = pageCount * pageTotalBytes;
        var result = new byte[wlByteSize];

        if (pageCount == 0)
            throw new InvalidDataException($"WL {groupEntry.WlIndex} has no valid pages.");
        if (pageTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageTotalBytes), "Page size must be positive.");

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Voltage file not found: {filePath}", filePath);

        using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

        for (int i = 0; i < pageCount; i++)
        {
            int pageIndex = startPage + groupEntry.PageIndices[i];
            if (pageIndex < 0)
                throw new InvalidDataException($"WL {groupEntry.WlIndex} contains negative page index after start offset: {pageIndex}");

            long offset = (long)pageIndex * pageTotalBytes;
            if (offset + pageTotalBytes > fileInfo.Length)
            {
                throw new InvalidDataException(
                    $"File '{Path.GetFileName(filePath)}' is too small for page {pageIndex}: " +
                    $"need {offset + pageTotalBytes} bytes, got {fileInfo.Length} bytes.");
            }

            using var accessor = mmf.CreateViewAccessor(offset, pageTotalBytes, MemoryMappedFileAccess.Read);
            accessor.ReadArray(0, result, i * pageTotalBytes, pageTotalBytes);
        }

        return result;
    }
}

public class VoltageFileScanResult
{
    public List<VoltageFileInfo> Files { get; } = new();
    public int TotalCandidateFiles { get; set; }
    public int NameMismatchFiles { get; set; }
    public int RangeFilteredFiles { get; set; }
    public int StepFilteredFiles { get; set; }
}
