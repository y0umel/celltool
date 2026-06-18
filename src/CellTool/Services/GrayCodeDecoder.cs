namespace CellTool.Services;

public class GrayCodeDecoder
{
    private readonly int[] _wlEncoding;
    private readonly int _bitsPerCell;

    public GrayCodeDecoder(int[] wlEncoding, int bitsPerCell)
    {
        _wlEncoding = wlEncoding;
        _bitsPerCell = bitsPerCell;
    }

    public int[] DecodeWl(byte[] wlData, int pageTotalBytes)
    {
        int pageBits = pageTotalBytes * 8;
        int cellCount = pageBits;
        var states = new int[cellCount];
        int pageCount = _bitsPerCell;

        for (int cell = 0; cell < cellCount; cell++)
        {
            int grayCode = 0;
            int byteIndex = cell / 8;
            int bitMask = 1 << (cell % 8);

            for (int pg = 0; pg < pageCount; pg++)
            {
                int pageByteIndex = pg * pageTotalBytes + byteIndex;
                int bit = (wlData[pageByteIndex] & bitMask) != 0 ? 1 : 0;
                grayCode = (grayCode << 1) | bit;
            }

            int binary = GrayToBinary(grayCode);
            states[cell] = binary < _wlEncoding.Length ? _wlEncoding[binary] : 0;
        }

        return states;
    }

    private int GrayToBinary(int gray)
    {
        int binary = gray;
        int mask = gray >> 1;
        while (mask != 0)
        {
            binary ^= mask;
            mask >>= 1;
        }
        int maxVal = (1 << _bitsPerCell) - 1;
        return binary & maxVal;
    }
}
