namespace CellTool.Services;

public class GrayCodeDecoder
{
    private readonly int[] _wlEncoding;
    private readonly int[] _rawGrayToState;
    private readonly int _bitsPerCell;
    private readonly int[] _roleSlotByRawBit;
    private readonly bool _msbFirst;

    public GrayCodeDecoder(int[] wlEncoding, int bitsPerCell, string pageSlotRoleOrder = "U-M-L", string bitOrder = "MSB")
    {
        _wlEncoding = wlEncoding;
        _bitsPerCell = bitsPerCell;
        _roleSlotByRawBit = BuildRoleSlotByRawBit(pageSlotRoleOrder, bitsPerCell);
        _msbFirst = ParseBitOrder(bitOrder);
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
            int bitMask = BitMask(cell, _msbFirst);

            for (int rawBit = pageCount - 1; rawBit >= 0; rawBit--)
            {
                int pageByteIndex = _roleSlotByRawBit[rawBit] * pageTotalBytes + byteIndex;
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
            int bitMask = BitMask(cell, _msbFirst);

            for (int rawBit = pageCount - 1; rawBit >= 0; rawBit--)
            {
                int pageByteIndex = _roleSlotByRawBit[rawBit] * pageTotalBytes + byteIndex;
                int bit = (wlData[pageByteIndex] & bitMask) != 0 ? 1 : 0;
                grayCode = (grayCode << 1) | bit;
            }

            grayCodes[cell] = grayCode;
        }

        return grayCodes;
    }

    private static int[] BuildRoleSlotByRawBit(string order, int bitsPerCell)
    {
        var tokens = order.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < bitsPerCell)
            throw new ArgumentException($"Page slot role order '{order}' must contain at least {bitsPerCell} page tokens.");

        var roleSlotByRawBit = Enumerable.Repeat(-1, bitsPerCell).ToArray();
        var seenRawBits = new HashSet<int>();

        for (int slot = 0; slot < bitsPerCell; slot++)
        {
            int rawBit = tokens[slot].ToUpperInvariant() switch
            {
                "U" or "UPPER" => 0,
                "M" or "MIDDLE" => 1,
                "L" or "LOWER" => 2,
                "P0" => 0,
                "P1" => 1,
                "P2" => 2,
                "P3" => 3,
                _ => throw new ArgumentException($"Unknown page slot role token '{tokens[slot]}'.")
            };

            if (rawBit >= bitsPerCell)
                throw new ArgumentException($"Page slot role token '{tokens[slot]}' is not valid for {bitsPerCell} bits/cell.");
            if (!seenRawBits.Add(rawBit))
                throw new ArgumentException($"Page slot role order '{order}' contains duplicate token '{tokens[slot]}'.");

            roleSlotByRawBit[rawBit] = slot;
        }

        return roleSlotByRawBit;
    }

    private static bool ParseBitOrder(string bitOrder)
    {
        return bitOrder.Trim().ToUpperInvariant() switch
        {
            "" or "MSB" or "MSB-FIRST" or "MSBFIRST" => true,
            "LSB" or "LSB-FIRST" or "LSBFIRST" => false,
            _ => throw new ArgumentException($"Unknown bit order '{bitOrder}'.")
        };
    }

    private static int BitMask(int cell, bool msbFirst)
    {
        int bitIndex = cell % 8;
        return msbFirst
            ? 1 << (7 - bitIndex)
            : 1 << bitIndex;
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
