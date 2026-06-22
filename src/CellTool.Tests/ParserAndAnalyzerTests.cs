using CellTool.Models;
using CellTool.Services;
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
    public void PeakAnalyzer_BoundaryReturnsNullWhenCurveRisesBeforeThreshold()
    {
        var analyzer = new PeakAnalyzer(filterWindow: 1);
        double[] data = [10, 8, 5, 6, 9];

        var boundary = analyzer.FindBoundary(data, startIdx: 0, direction: 1, threshold: 20);

        Assert.Null(boundary);
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
    public void ExcelParser_SkipsIncompleteCsvRowsWithoutThrowing()
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

            Assert.Single(chips);
            Assert.Equal("G8T22", chips[0].DieName);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
