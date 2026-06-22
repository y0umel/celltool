using CellTool.Models;
using System.IO;

namespace CellTool.Services;

public class CodewordAnalyzer
{
    public CodewordErrorStat Analyze(byte[] referenceData, byte[] testData, int codewordBytes, int frameCount)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");
        if (referenceData.Length % (codewordBytes * frameCount) != 0)
        {
            throw new InvalidDataException(
                $"Data length {referenceData.Length} is not an integral number of pages for " +
                $"{frameCount} frames and {codewordBytes} bytes/codeword.");
        }

        return Analyze(referenceData, testData, codewordBytes);
    }

    public CodewordErrorStat Analyze(byte[] referenceData, byte[] testData, int codewordBytes)
    {
        if (codewordBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(codewordBytes), "Codeword size must be positive.");
        if (referenceData.Length != testData.Length)
        {
            throw new ArgumentException(
                $"Reference and test data lengths differ: {referenceData.Length} vs {testData.Length}.");
        }
        if (referenceData.Length % codewordBytes != 0)
        {
            throw new InvalidDataException(
                $"Data length {referenceData.Length} is not divisible by codeword size {codewordBytes}.");
        }

        int totalCodewords = referenceData.Length / codewordBytes;
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
            TotalBitErrors = totalErrors,
            ErrorRate = referenceData.Length > 0 ? (double)totalErrors / (referenceData.Length * 8) : 0,
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
