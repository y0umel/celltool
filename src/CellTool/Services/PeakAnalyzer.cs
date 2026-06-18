using CellTool.Models;

namespace CellTool.Services;

public class PeakAnalyzer
{
    private readonly int _filterWindow;

    public PeakAnalyzer(int filterWindow = 3)
    {
        _filterWindow = Math.Max(1, filterWindow);
    }

    public StatePeakInfo[] Analyze(double[][] increments, double[] voltagesMv, int totalCells, int stateCount)
    {
        int voltageCount = voltagesMv.Length;
        var expectedPerState = (double)totalCells / stateCount;
        var threshold = expectedPerState * 0.001;

        var results = new StatePeakInfo[stateCount];

        for (int s = 0; s < stateCount; s++)
        {
            var filtered = LowPassFilter(increments[s]);
            int peakIdx = FindPeakIndex(filtered);

            double? left = FindBoundary(filtered, peakIdx, -1, threshold);
            double? right = FindBoundary(filtered, peakIdx, +1, threshold);

            results[s] = new StatePeakInfo
            {
                StateIndex = s,
                PeakVoltageMv = voltagesMv[peakIdx],
                PeakIncrementValue = filtered[peakIdx],
                LeftBoundaryMv = left.HasValue ? voltagesMv[left.Value] : null,
                RightBoundaryMv = right.HasValue ? voltagesMv[right.Value] : null,
                TotalCellCount = totalCells
            };
        }

        return results;
    }

    private double[] LowPassFilter(double[] data)
    {
        var result = new double[data.Length];
        int half = _filterWindow / 2;

        for (int i = 0; i < data.Length; i++)
        {
            double sum = 0;
            int count = 0;
            for (int j = Math.Max(0, i - half); j <= Math.Min(data.Length - 1, i + half); j++)
            {
                sum += data[j];
                count++;
            }
            result[i] = sum / count;
        }

        return result;
    }

    private int FindPeakIndex(double[] data)
    {
        int peakIdx = 0;
        double peakVal = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] > peakVal)
            {
                peakVal = data[i];
                peakIdx = i;
            }
        }
        return peakIdx;
    }

    private int? FindBoundary(double[] data, int startIdx, int direction, double threshold)
    {
        double sum = 0;
        int i = startIdx;
        int? lastFallingIdx = null;
        bool wasFalling = false;

        while (i >= 0 && i < data.Length)
        {
            sum += data[i];

            bool isFalling = false;
            if (direction < 0 && i > 0)
                isFalling = data[i] < data[i - 1];
            else if (direction > 0 && i < data.Length - 1)
                isFalling = data[i] < data[i + 1];

            if (isFalling)
                lastFallingIdx = i;
            else if (wasFalling && !isFalling && sum < threshold)
                return null; // overlap: hit adjacent peak before reaching threshold

            wasFalling = isFalling;

            if (sum >= threshold && (isFalling || lastFallingIdx.HasValue))
                return lastFallingIdx ?? i;

            i += direction;
        }

        return null;
    }
}
