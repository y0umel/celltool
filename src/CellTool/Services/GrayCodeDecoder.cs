namespace CellTool.Services;

public class GrayCodeDecoder
{
    private readonly int[] _wlEncoding;
    private readonly int[] _rawGrayToState;
    private readonly int _bitsPerCell;
    private readonly int[] _pageOrder;

    public GrayCodeDecoder(int[] wlEncoding, int bitsPerCell, string grayCodeOrder = "U-M-L")
    {
        _wlEncoding = wlEncoding;
        _bitsPerCell = bitsPerCell;
        _pageOrder = ParsePageOrder(grayCodeOrder, bitsPerCell);
        _rawGrayToState = BuildRawGrayToStateMap(wlEncoding, bitsPerCell);
    }

    public int[] DecodeWl(byte[] wlData, int pageTotalBytes)
    {
        int pageBits = pageTotalBytes * 8;
        int cellCount = pageBits;
        var states = new int[cellCount];
        int pageCount = _bitsPerCell;

        if (wlData.Length < pageCount * pageTotalBytes)
        {
            throw new ArgumentException(
                $"WL data length {wlData.Length} is smaller than expected {pageCount * pageTotalBytes}.",
                nameof(wlData));
        }

        for (int cell = 0; cell < cellCount; cell++)
        {
            int grayCode = 0;
            int byteIndex = cell / 8;
            int bitMask = 1 << (cell % 8);

            for (int pg = 0; pg < pageCount; pg++)
            {
                int pageByteIndex = _pageOrder[pg] * pageTotalBytes + byteIndex;
                int bit = (wlData[pageByteIndex] & bitMask) != 0 ? 1 : 0;
                grayCode = (grayCode << 1) | bit;
            }

            states[cell] = grayCode < _rawGrayToState.Length && _rawGrayToState[grayCode] >= 0
                ? _rawGrayToState[grayCode]
                : 0;
        }

        return states;
    }

    public int[] DecodeRawGrayWl(byte[] wlData, int pageTotalBytes)
    {
        int pageBits = pageTotalBytes * 8;
        int cellCount = pageBits;
        var grayCodes = new int[cellCount];
        int pageCount = _bitsPerCell;

        if (wlData.Length < pageCount * pageTotalBytes)
        {
            throw new ArgumentException(
                $"WL data length {wlData.Length} is smaller than expected {pageCount * pageTotalBytes}.",
                nameof(wlData));
        }

        for (int cell = 0; cell < cellCount; cell++)
        {
            int grayCode = 0;
            int byteIndex = cell / 8;
            int bitMask = 1 << (cell % 8);

            for (int pg = 0; pg < pageCount; pg++)
            {
                int pageByteIndex = _pageOrder[pg] * pageTotalBytes + byteIndex;
                int bit = (wlData[pageByteIndex] & bitMask) != 0 ? 1 : 0;
                grayCode = (grayCode << 1) | bit;
            }

            grayCodes[cell] = grayCode;
        }

        return grayCodes;
    }

    private static int[] ParsePageOrder(string order, int bitsPerCell)
    {
        var tokens = order.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < bitsPerCell)
            throw new ArgumentException($"Gray code order '{order}' must contain at least {bitsPerCell} page tokens.");

        var result = new int[bitsPerCell];
        var seen = new HashSet<int>();

        for (int i = 0; i < bitsPerCell; i++)
        {
            int index = tokens[i].ToUpperInvariant() switch
            {
                "U" or "UPPER" => 0,
                "M" or "MIDDLE" => 1,
                "L" or "LOWER" => 2,
                "P0" => 0,
                "P1" => 1,
                "P2" => 2,
                "P3" => 3,
                _ => throw new ArgumentException($"Unknown Gray code page token '{tokens[i]}'.")
            };

            if (index >= bitsPerCell)
                throw new ArgumentException($"Gray code token '{tokens[i]}' is not valid for {bitsPerCell} bits/cell.");
            if (!seen.Add(index))
                throw new ArgumentException($"Gray code order '{order}' contains duplicate page token '{tokens[i]}'.");

            result[i] = index;
        }

        return result;
    }

    private static int[] BuildRawGrayToStateMap(int[] wlEncoding, int bitsPerCell)
    {
        int stateCount = 1 << bitsPerCell;
        if (wlEncoding.Length != stateCount)
            throw new ArgumentException($"WL encoding must contain {stateCount} raw Gray codes for {bitsPerCell} bits/cell.");

        var map = Enumerable.Repeat(-1, stateCount).ToArray();
        for (int state = 0; state < wlEncoding.Length; state++)
        {
            int rawGray = wlEncoding[state];
            if (rawGray < 0 || rawGray >= stateCount)
                throw new ArgumentException($"WL encoding raw Gray code {rawGray} is outside 0..{stateCount - 1}.");
            if (map[rawGray] >= 0)
                throw new ArgumentException($"WL encoding contains duplicate raw Gray code {rawGray}.");

            map[rawGray] = state;
        }

        return map;
    }
}
