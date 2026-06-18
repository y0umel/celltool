using CellTool.Models;

namespace CellTool.Services;

public class CodewordAnalyzer
{
    public CodewordErrorStat Analyze(byte[] referenceData, byte[] testData, int codewordBytes, int frameCount)
    {
        int totalCodewords = frameCount;
        var errorCounts = new int[totalCodewords];
        int maxErrors = 0;
        int totalErrors = 0;

        for (int cw = 0; cw < totalCodewords; cw++)
        {
            int startByte = cw * codewordBytes;
            int bitErrors = CountBitDifferences(referenceData, testData, startByte, codewordBytes);
            errorCounts[cw] = bitErrors;
            totalErrors += bitErrors;
            if (bitErrors > maxErrors)
                maxErrors = bitErrors;
        }

        return new CodewordErrorStat
        {
            MaxBitErrors = maxErrors,
            AvgBitErrors = totalCodewords > 0 ? (double)totalErrors / totalCodewords : 0,
            TotalCodewords = totalCodewords,
            ErrorCounts = errorCounts
        };
    }

    private int CountBitDifferences(byte[] a, byte[] b, int startByte, int byteCount)
    {
        int differences = 0;
        for (int i = 0; i < byteCount; i++)
        {
            int idx = startByte + i;
            byte diff = (byte)(a[idx] ^ b[idx]);
            // Count set bits
            while (diff != 0)
            {
                differences += diff & 1;
                diff >>= 1;
            }
        }
        return differences;
    }
}
