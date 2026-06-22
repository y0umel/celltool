namespace CellTool.Services;

public static class ChartAxisMapper
{
    public static double[] ToDisplayVoltageBins(double[] voltageCodes)
    {
        if (voltageCodes.Length == 0)
            return Array.Empty<double>();

        double lowerBoundCode = voltageCodes.Min();
        return voltageCodes
            .Select(v => ToDisplayVoltageBin(v, lowerBoundCode))
            .ToArray();
    }

    public static double ToDisplayVoltageBin(double voltageCode, double lowerBoundCode) =>
        voltageCode - lowerBoundCode;
}
