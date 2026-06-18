using System.IO.MemoryMappedFiles;
using System.Text.RegularExpressions;
using CellTool.Models;

namespace CellTool.Services;

public partial class VoltageFileReader
{
    [GeneratedRegex(@"^(\d+)\.bin$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    public List<VoltageFileInfo> ScanDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var files = new List<VoltageFileInfo>();
        foreach (var path in Directory.GetFiles(directoryPath, "*.bin"))
        {
            var name = Path.GetFileName(path);
            var match = FileNamePattern().Match(name);
            if (!match.Success) continue;

            int offset = int.Parse(match.Groups[1].Value) * 10; // offset in mV
            files.Add(new VoltageFileInfo { FilePath = path, OffsetMv = offset });
        }

        files.Sort();
        return files;
    }

    public byte[] ReadWlBytes(string filePath, GroupEntry groupEntry, int pageTotalBytes)
    {
        var pageCount = groupEntry.PageIndices.Length;
        var wlByteSize = pageCount * pageTotalBytes;
        var result = new byte[wlByteSize];

        using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

        for (int i = 0; i < pageCount; i++)
        {
            int pageIndex = groupEntry.PageIndices[i];
            long offset = (long)pageIndex * pageTotalBytes;

            using var accessor = mmf.CreateViewAccessor(offset, pageTotalBytes, MemoryMappedFileAccess.Read);
            accessor.ReadArray(0, result, i * pageTotalBytes, pageTotalBytes);
        }

        return result;
    }
}
