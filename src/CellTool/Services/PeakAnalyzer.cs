using CellTool.Models;

namespace CellTool.Services;

public class PeakAnalyzer
{
    private readonly int _filterWindow;

    public PeakAnalyzer(int filterWindow = 3)
    {
        _filterWindow = Math.Max(1, filterWindow);
    }

    public StatePeakInfo[] Analyze(double[][] increments, double[] voltageCodes, int totalCells, int stateCount)
    {
        var labels = Enumerable.Range(0, stateCount)
            .Select(i => $"State {i}")
            .ToArray();
        return Analyze(increments, voltageCodes, totalCells, labels, stateCount);
    }

    public StatePeakInfo[] Analyze(double[][] increments, double[] voltageCodes, int totalCells, string[] labels)
    {
        return Analyze(increments, voltageCodes, totalCells, labels, labels.Length + 1);
    }

    private StatePeakInfo[] Analyze(
        double[][] increments,
        double[] voltageCodes,
        int totalCells,
        string[] labels,
        int expectedBucketCount)
    {
        var expectedPerState = (double)totalCells / Math.Max(1, expectedBucketCount);
        var threshold = expectedPerState * 0.001;

        var results = new StatePeakInfo[increments.Length];

        for (int s = 0; s < increments.Length; s++)
        {
            var filtered = LowPassFilter(increments[s]);
            int peakIdx = FindPeakIndex(filtered);

            int? left = FindBoundary(filtered, peakIdx, -1, threshold);
            int? right = FindBoundary(filtered, peakIdx, +1, threshold);

            results[s] = new StatePeakInfo
            {
                StateIndex = s,
                Label = s < labels.Length ? labels[s] : $"Transition {s}",
                PeakCode = voltageCodes[peakIdx],
                PeakIncrementValue = filtered[peakIdx],
                LeftBoundaryCode = left.HasValue ? voltageCodes[left.Value] : null,
                RightBoundaryCode = right.HasValue ? voltageCodes[right.Value] : null,
                TotalCellCount = totalCells
            };
        }

        return results;
    }

    public double[] LowPassFilter(double[] data)
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

    public int FindPeakIndex(double[] data)
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

    public int? FindBoundary(double[] data, int startIdx, int direction, double threshold)
    {
        double sum = 0;
        int i = startIdx + direction;
        double previous = data[startIdx];

        while (i >= 0 && i < data.Length)
        {
            double current = data[i];

            if (current > previous && sum < threshold)
                return null;

            sum += current;
            if (sum >= threshold)
                return i;

            previous = current;
            i += direction;
        }

        return null;
    }
}
