using CellTool.Models;
using CellTool.Services;
using CellTool.ViewModels;
using Xunit;

namespace CellTool.Tests;

public class ParserAndAnalyzerTests
{
    [Fact]
    public void GroupModelParser_AllowsMinusOneButRequiresValidPageCount()
    {
        string file = Path.Combine(Path.GetTempPath(), $"group-{Guid.NewGuid():N}.csv");
        File.WriteAllText(file, "0,1,2,-1\n3,4,5,-1\n");

        try
        {
            var model = new GroupModelParser().LoadFromFile(file, expectedWlCount: 2, expectedValidPagesPerWl: 3);

            Assert.Equal(2, model.WlCount);
            Assert.Equal([0, 1, 2], model.Entries[0].PageIndices);
            Assert.Equal([3, 4, 5], model.Entries[1].PageIndices);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void GroupModelParser_AllowsMixedSlcMlcTlcQlcRows()
    {
        string file = Path.Combine(Path.GetTempPath(), $"group-{Guid.NewGuid():N}.csv");
        File.WriteAllText(file, "0\n1,2\n3,4,5\n6,7,8,9\n");

        try
        {
            var model = new GroupModelParser().LoadFromFile(file, expectedWlCount: 4);

            Assert.Equal([0], model.Entries[0].PageIndices);
            Assert.Equal([1, 2], model.Entries[1].PageIndices);
            Assert.Equal([3, 4, 5], model.Entries[2].PageIndices);
            Assert.Equal([6, 7, 8, 9], model.Entries[3].PageIndices);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void VoltageFileReader_ScansSortsAndFiltersByConfiguredRange()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"voltage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "20.bin"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "10.bin"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "-10"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "-127"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "25.bin"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "note.bin"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "note"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "10.csv"), [0]);

        try
        {
            var files = new VoltageFileReader().ScanDirectory(dir, minMv: -10, maxMv: 22, stepMv: 10);

            Assert.Equal([-10, 10, 20], files.Select(f => f.Code).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VoltageFileReader_SupportsExtensionlessNegativeVoltageNames()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"voltage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "-127"), [0]);

        try
        {
            var files = new VoltageFileReader().ScanDirectory(dir, minMv: -127, maxMv: -127, stepMv: 1);

            var file = Assert.Single(files);
            Assert.Equal(-127, file.Code);
            Assert.Equal("-127", Path.GetFileName(file.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GrayCodeDecoder_UsesConfiguredPageOrder()
    {
        var decoder = new GrayCodeDecoder([0, 1, 3, 2], bitsPerCell: 2, grayCodeOrder: "M-U");
        byte[] wlData = [0b0000_0010, 0b0000_0001];

        var states = decoder.DecodeWl(wlData, pageTotalBytes: 1);

        Assert.Equal(3, states[0]);
        Assert.Equal(1, states[1]);
    }

    [Fact]
    public void GrayCodeDecoder_UsesLeadingPageOrderTokensForLowerBitModes()
    {
        var decoder = new GrayCodeDecoder([0, 1], bitsPerCell: 1, grayCodeOrder: "U-M-L");
        byte[] wlData = [0b0000_0001];

        var states = decoder.DecodeWl(wlData, pageTotalBytes: 1);

        Assert.Equal(1, states[0]);
    }

    [Fact]
    public void GrayCodeDecoder_DecodesRawGrayWithoutPhysicalStateMapping()
    {
        var decoder = new GrayCodeDecoder([7, 6, 4, 0, 2, 3, 1, 5], bitsPerCell: 3, grayCodeOrder: "U-M-L");
        byte[] wlData = [0b0000_0001, 0b0000_0000, 0b0000_0001];

        var rawGrayCodes = decoder.DecodeRawGrayWl(wlData, pageTotalBytes: 1);
        var states = decoder.DecodeWl(wlData, pageTotalBytes: 1);

        Assert.Equal(5, rawGrayCodes[0]);
        Assert.Equal(7, states[0]);
    }

    [Fact]
    public void CodewordAnalyzer_ComputesAllCodewordsAndErrorRate()
    {
        byte[] reference = [0b0000_0000, 0b1111_1111, 0b1010_1010, 0b0101_0101];
        byte[] test = [0b0000_1111, 0b1111_1111, 0b1010_1010, 0b0101_0000];

        var stat = new CodewordAnalyzer().Analyze(reference, test, codewordBytes: 2);

        Assert.Equal(2, stat.TotalCodewords);
        Assert.Equal([4, 2], stat.ErrorCounts);
        Assert.Equal(6, stat.TotalBitErrors);
        Assert.Equal(6.0 / 32, stat.ErrorRate);
    }

    [Fact]
    public void ExcelParser_LoadsChipDatabaseFromCsv()
    {
        string file = Path.Combine(Path.GetTempPath(), $"chips-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            file,
            "die简称,xLC,页 数据 Byte,页 冗余 Byte,1KB Frame 个数 KB,块大小 （页数量）,WL/Block,WL编码\n" +
            "G8T22,TLC,16384,1952,16,576,192,\"7,6,4,0,2,3,1,5\"\n");

        try
        {
            var chips = new ExcelParser().LoadDatabase(file);

            Assert.Single(chips);
            Assert.Equal(ChipInfo.UnknownManufacturer, chips[0].Manufacturer);
            Assert.Equal("G8T22", chips[0].DieName);
            Assert.Equal(XlcType.TLC, chips[0].Type);
            Assert.Equal(18336, chips[0].PageTotalBytes);
            Assert.Equal([7, 6, 4, 0, 2, 3, 1, 5], chips[0].WlEncoding);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ExcelParser_LoadsManufacturerFromCsv()
    {
        string file = Path.Combine(Path.GetTempPath(), $"chips-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            file,
            "Factory,die简称,xLC,页 数据 Byte,页 冗余 Byte,1KB Frame 个数 KB,块大小 （页数量）,WL/Block,WL编码\n" +
            "YMTC,G8T22,TLC,16384,1952,16,576,192,\"7,6,4,0,2,3,1,5\"\n");

        try
        {
            var chip = Assert.Single(new ExcelParser().LoadDatabase(file));

            Assert.Equal("YMTC", chip.Manufacturer);
            Assert.Equal("G8T22", chip.DieName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ExcelParser_LoadsUnquotedWlEncodingFromCsv()
    {
        string file = Path.Combine(Path.GetTempPath(), $"chips-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            file,
            "Factory,die简称,xLC,页 数据 Byte,页 冗余 Byte,1KB Frame 个数 KB,块大小 （页数量）,WL/Block,WL编码\n" +
            "YMTC,G8T22,TLC,16384,1952,16,576,192,7,6,4,0,2,3,1,5\n");

        try
        {
            var chip = Assert.Single(new ExcelParser().LoadDatabase(file));

            Assert.Equal("YMTC", chip.Manufacturer);
            Assert.Equal([7, 6, 4, 0, 2, 3, 1, 5], chip.WlEncoding);
        }
        finally
        {
            File.Delete(file);
        }
    }


    [Fact]
    public void ExcelParser_KeepsRowsWithMissingOptionalPageFields()
    {
        string file = Path.Combine(Path.GetTempPath(), $"chips-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            file,
            "die简称,xLC,页 数据 Byte,页 冗余 Byte,1KB Frame 个数 KB,块大小 （页数量）,WL/Block,WL编码\n" +
            "Bad,TLC,,1952,16,576,192,\"7,6,4,0,2,3,1,5\"\n" +
            "G8T22,TLC,16384,1952,16,576,192,\"7,6,4,0,2,3,1,5\"\n" +
            "Incomplete,TLC,16384\n");

        try
        {
            var chips = new ExcelParser().LoadDatabase(file);

            Assert.Equal(3, chips.Count);
            Assert.Contains(chips, chip => chip.DieName == "Bad" && chip.PageDataBytes is null);
            Assert.Contains(chips, chip => chip.DieName == "G8T22" && chip.PageTotalBytes == 18336);
            Assert.Contains(chips, chip =>
                chip.DieName == "Incomplete" &&
                chip.PageDataBytes == 16384 &&
                chip.PageRedundantBytes is null &&
                chip.WlEncoding.Length == 0);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ChipDatabaseService_LoadsBundledFactoryDatabase()
    {
        string userFile = Path.Combine(Path.GetTempPath(), $"missing-chip-db-{Guid.NewGuid():N}.json");
        string bundledFile = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "CellTool",
                "Resources",
                "chip-database.default.json"));

        var chips = new ChipDatabaseService(userFile, bundledFile).Load();

        Assert.Equal(99, chips.Count);
        Assert.Contains(chips, chip =>
            chip.Manufacturer == "YMTC" &&
            chip.DieName == "X4-9060(Client)" &&
            chip.WlEncoding.SequenceEqual([7, 6, 4, 0, 2, 3, 1, 5]));
        Assert.Contains(chips, chip => chip.Manufacturer == "Toshiba" && chip.DieName == "G8T22");
        Assert.Contains(chips, chip => chip.DieName == "GF23B" && chip.WlEncoding.Length == 0);
    }

    [Fact]
    public void ChipDatabaseService_IgnoresStaleUnknownManufacturerUserDatabase()
    {
        string userFile = Path.Combine(Path.GetTempPath(), $"stale-chip-db-{Guid.NewGuid():N}.json");
        string bundledFile = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "CellTool",
                "Resources",
                "chip-database.default.json"));
        File.WriteAllText(
            userFile,
            """
            [
              {
                "manufacturer": "未指定厂家",
                "dieName": "LegacyOnly",
                "type": 3,
                "pageDataBytes": 16384,
                "pageRedundantBytes": 1952,
                "frameCount": 16,
                "blockSizePages": 576,
                "wlPerBlock": 192,
                "wlEncoding": [7, 6, 4, 0, 2, 3, 1, 5]
              }
            ]
            """);

        try
        {
            var chips = new ChipDatabaseService(userFile, bundledFile).Load();

            Assert.DoesNotContain(chips, chip => chip.DieName == "LegacyOnly");
            Assert.Contains(chips, chip => chip.Manufacturer == "YMTC" && chip.DieName == "X4-9060(Client)");
        }
        finally
        {
            File.Delete(userFile);
        }
    }

    [Fact]
    public void ChipDatabaseService_IgnoresLegacyFilteredUserDatabaseWhenBundledIsLarger()
    {
        string userFile = Path.Combine(Path.GetTempPath(), $"legacy-chip-db-{Guid.NewGuid():N}.json");
        string bundledFile = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "CellTool",
                "Resources",
                "chip-database.default.json"));
        File.WriteAllText(
            userFile,
            """
            [
              {
                "manufacturer": "YMTC",
                "dieName": "X4-9060(Client)",
                "type": 3,
                "pageDataBytes": 49152,
                "pageRedundantBytes": 6144,
                "frameCount": 48,
                "blockSizePages": 2304,
                "wlPerBlock": 192,
                "wlEncoding": [7, 6, 4, 0, 2, 3, 1, 5]
              }
            ]
            """);

        try
        {
            var chips = new ChipDatabaseService(userFile, bundledFile).Load();

            Assert.Equal(99, chips.Count);
            Assert.Contains(chips, chip => chip.DieName == "GF23B");
        }
        finally
        {
            File.Delete(userFile);
            File.Delete($"{userFile}.meta.json");
        }
    }

    [Fact]
    public void AppState_SelectManufacturerIgnoresEmptySelection()
    {
        var state = new AppState();
        state.SetChipDatabase(
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
            }
        ]);
        state.SelectManufacturer("YMTC");

        state.SelectManufacturer(null);
        state.SelectManufacturer("");
        state.SelectManufacturer("   ");

        Assert.Equal("YMTC", state.SelectedManufacturer);
        Assert.Equal("X4-9060(Client)", state.SelectedChip?.DieName);
        Assert.Equal(["X4-9060(Client)"], state.AvailableChips.Select(chip => chip.DieName).ToArray());
    }

    [Fact]
    public void AppState_SelectChipFillsAndClearsEditablePageFields()
    {
        var complete = new ChipInfo
        {
            Manufacturer = "YMTC",
            DieName = "Complete",
            Type = XlcType.TLC,
            PageDataBytes = 49152,
            PageRedundantBytes = 6144,
            FrameCount = 48,
            WlPerBlock = 192,
            WlEncoding = [7, 6, 4, 0, 2, 3, 1, 5]
        };
        var partial = new ChipInfo
        {
            Manufacturer = "YMTC",
            DieName = "Partial",
            Type = XlcType.TLC,
            WlEncoding = []
        };
        var state = new AppState();
        state.SetChipDatabase([complete, partial]);

        state.SelectChip(complete);

        Assert.Equal(49152, state.PageDataBytes);
        Assert.Equal(6144, state.PageRedundantBytes);
        Assert.Equal(48, state.CodewordsPerPage);
        Assert.Equal("7,6,4,0,2,3,1,5", state.TlcWlEncoding);

        state.SelectChip(partial);

        Assert.Null(state.PageDataBytes);
        Assert.Null(state.PageRedundantBytes);
        Assert.Null(state.CodewordsPerPage);
        Assert.Equal(string.Empty, state.TlcWlEncoding);
    }
}
