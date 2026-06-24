using System.IO;
using CellTool.Models;
using ScottPlot;
using SkiaSharp;

namespace CellTool.Services;

public class ChartRenderer
{
    private static readonly string[] DefaultColors =
    {
        "#006fff", "#2ca02c", "#ffbf00", "#7a00ff",
        "#ff7f0e", "#17becf", "#8b3f00", "#ff14b8"
    };

    public Plot Render(AnalysisResult result, double[] voltageCodes, ChartConfig chartConfig)
    {
        return RenderLinear(result, chartConfig);
    }

    public Plot RenderLinear(AnalysisResult result, ChartConfig chartConfig)
    {
        var plot = CreateToolStylePlot(result, chartConfig, logScale: false);
        ApplyToolStyleLimits(plot, result, chartConfig, logScale: false);
        return plot;
    }

    public Plot RenderLog(AnalysisResult result, ChartConfig chartConfig)
    {
        var plot = CreateToolStylePlot(result, chartConfig, logScale: true);
        ApplyToolStyleLimits(plot, result, chartConfig, logScale: true);
        return plot;
    }

    public IReadOnlyList<LimitMissStat> BuildLimitMissStats(AnalysisResult result)
    {
        var stats = new List<LimitMissStat>();
        for (int i = 0; i < result.StateCount; i++)
        {
            string label = i < result.TransitionLabels.Length ? result.TransitionLabels[i] : $"L{i}";
            var integral = result.DistributionIntegrals.FirstOrDefault(x => x.LevelIndex == i);
            int total = integral?.SourceCellCount
                        ?? result.StatePeaks.FirstOrDefault(p => p.StateIndex == i)?.TotalCellCount
                        ?? 0;
            double observed = integral?.RawObservedIntegral
                              ?? (i < result.IncrementCurves.Length ? result.IncrementCurves[i].Sum() : 0);
            int missing = Math.Max(0, total - (int)Math.Round(observed));
            stats.Add(new LimitMissStat
            {
                Label = label,
                LeftOutOfRange = integral is not null
                    ? (int)Math.Round(integral.LeftOutOfRangeEstimate)
                    : (i == 0 ? missing : 0),
                RightOutOfRange = integral is not null
                    ? (int)Math.Round(integral.RightOutOfRangeEstimate)
                    : (i == result.StateCount - 1 ? missing : 0)
            });
        }

        return stats;
    }

    private static double[] GetCurveXValues(
        AnalysisResult result,
        double[] fallbackDisplayVoltages,
        int curveIndex,
        int curveLength)
    {
        if (curveIndex < result.IncrementCurveXValues.Length &&
            result.IncrementCurveXValues[curveIndex].Length == curveLength)
        {
            return result.IncrementCurveXValues[curveIndex];
        }

        if (fallbackDisplayVoltages.Length == curveLength)
            return fallbackDisplayVoltages;

        return Enumerable.Range(0, curveLength)
            .Select(i => (double)i)
            .ToArray();
    }

    /// <summary>
    /// Saves a rendered voltage distribution chart to a PNG file.
    /// </summary>
    public void SavePng(string filePath, AnalysisResult result, ChartConfig chartConfig, int width = 1536, int height = 768)
    {
        var mp = new ScottPlot.Multiplot();
        mp.AddPlots(2);
        mp.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 2, columns: 1);

        var linear = mp.GetPlot(0);
        var log = mp.GetPlot(1);
        ConfigureToolStylePlot(linear, chartConfig.Title, chartConfig.YAxisLabel, logScale: false);
        ConfigureToolStylePlot(log, string.Empty, $"Log {chartConfig.YAxisLabel}", logScale: true);
        AddCurveSeries(linear, result, logScale: false);
        AddCurveSeries(log, result, logScale: true);
        AddMarkersAndLegend(linear, result, chartConfig);
        AddMarkersAndLegend(log, result, chartConfig);
        ApplyToolStyleLimits(linear, result, chartConfig, logScale: false);
        ApplyToolStyleLimits(log, result, chartConfig, logScale: true);
        mp.SavePng(filePath, width, height);
        DrawLimitMissTable(filePath, BuildLimitMissStats(result));
    }

    private static Plot CreateToolStylePlot(AnalysisResult result, ChartConfig chartConfig, bool logScale)
    {
        var plot = new Plot();
        ConfigureToolStylePlot(
            plot,
            logScale ? string.Empty : chartConfig.Title,
            logScale ? $"Log {chartConfig.YAxisLabel}" : chartConfig.YAxisLabel,
            logScale);
        AddCurveSeries(plot, result, logScale);
        AddMarkersAndLegend(plot, result, chartConfig);
        return plot;
    }

    private static void AddCurveSeries(Plot plot, AnalysisResult result, bool logScale)
    {
        for (int s = 0; s < result.IncrementCurves.Length; s++)
        {
            var curve = result.IncrementCurves[s];
            var curveX = GetCurveXValues(result, ChartAxisMapper.ToDisplayVoltageBins(result.VoltageCodes), s, curve.Length);
            if (curve.Length == 0 || curveX.Length == 0)
                continue;

            var color = Color.FromHex(s < DefaultColors.Length ? DefaultColors[s] : DefaultColors[^1]);
            string label = s < result.TransitionLabels.Length ? result.TransitionLabels[s] : $"L{s}";
            var ys = logScale
                ? curve.Select(y => Math.Log10(Math.Max(1, y))).ToArray()
                : curve;
            var scatter = plot.Add.Scatter(curveX, ys);
            scatter.Color = color;
            scatter.LineWidth = 2;
            scatter.MarkerSize = 0;
            scatter.LegendText = label;
        }
    }

    private static void AddMarkersAndLegend(
        Plot plot,
        AnalysisResult result,
        ChartConfig chartConfig)
    {
        if (chartConfig.ShowBoundaryLines)
            AddReadValleyLines(plot, result);

        if (chartConfig.ShowReadVoltage)
        {
            foreach (var kvp in result.BestReadVoltages)
            {
                foreach (var v in kvp.Value)
                {
                    var line = plot.Add.VerticalLine(v);
                    line.Color = Colors.Red;
                    line.LinePattern = LinePattern.Solid;
                    line.LineWidth = 1;
                    line.LegendText = "最佳读取Code";
                }
            }
        }

        if (chartConfig.ShowLegend)
            plot.ShowLegend(Edge.Right);
    }

    private static void ConfigureToolStylePlot(Plot plot, string title, string yLabel, bool logScale)
    {
        plot.Title(title);
        plot.YLabel(yLabel);
        plot.XLabel("Vt");
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;
        plot.Grid.MajorLineColor = Colors.White.WithAlpha(0);
        plot.Grid.MinorLineColor = Colors.White.WithAlpha(0);
        plot.Axes.Bottom.Label.Alignment = Alignment.MiddleRight;

        if (logScale)
        {
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                LabelFormatter = value => value switch
                {
                    0 => "1",
                    1 => "10",
                    2 => "100",
                    3 => "1000",
                    _ => string.Empty
                }
            };
        }
    }

    private static void AddReadValleyLines(Plot plot, AnalysisResult result)
    {
        foreach (var valley in FindReadValleys(result))
        {
            var line = plot.Add.VerticalLine(valley.X);
            line.Color = Colors.Red;
            line.LinePattern = LinePattern.Dashed;
            line.LineWidth = 1;
        }
    }

    private static IReadOnlyList<ReadValley> FindReadValleys(AnalysisResult result)
    {
        int boundaryCount = Math.Max(0, result.StateCount - 1);
        if (boundaryCount == 0)
            return Array.Empty<ReadValley>();

        double spacing = EstimateReadSpacing(result);
        var valleys = new List<ReadValley>(boundaryCount);

        for (int boundary = 0; boundary < boundaryCount; boundary++)
        {
            int leftIndex = boundary;
            int rightIndex = boundary + 1;
            double nominal = boundary * spacing;
            double searchStart = nominal - spacing / 2;
            double searchEnd = nominal + spacing / 2;

            var leftX = GetCurveXValues(result, Array.Empty<double>(), leftIndex, GetCurveLength(result, leftIndex));
            var leftY = GetCurveValues(result, leftIndex);
            var rightX = GetCurveXValues(result, Array.Empty<double>(), rightIndex, GetCurveLength(result, rightIndex));
            var rightY = GetCurveValues(result, rightIndex);

            double leftPeakX = FindPeakX(leftX, leftY);
            double rightPeakX = FindPeakX(rightX, rightY);
            if (!double.IsNaN(leftPeakX) && !double.IsNaN(rightPeakX) && !leftPeakX.Equals(rightPeakX))
            {
                double peakStart = Math.Min(leftPeakX, rightPeakX);
                double peakEnd = Math.Max(leftPeakX, rightPeakX);
                double constrainedStart = Math.Max(searchStart, peakStart);
                double constrainedEnd = Math.Min(searchEnd, peakEnd);
                if (constrainedStart <= constrainedEnd)
                {
                    searchStart = constrainedStart;
                    searchEnd = constrainedEnd;
                }
            }

            double threshold = EstimateBoundaryThreshold(result, leftIndex, rightIndex);
            valleys.Add(FindReadValley(boundary, nominal, searchStart, searchEnd, leftX, leftY, rightX, rightY, threshold));
        }

        return valleys;
    }

    private static ReadValley FindReadValley(
        int boundary,
        double nominal,
        double searchStart,
        double searchEnd,
        double[] leftX,
        double[] leftY,
        double[] rightX,
        double[] rightY,
        double threshold)
    {
        var leftMap = BuildPointMap(leftX, leftY);
        var rightMap = BuildPointMap(rightX, rightY);
        int start = (int)Math.Ceiling(searchStart);
        int end = (int)Math.Floor(searchEnd);
        if (start > end)
        {
            start = end = (int)Math.Round(nominal);
        }

        double bestX = nominal;
        double bestValue = double.PositiveInfinity;
        double bestDistance = double.PositiveInfinity;
        bool foundUnderThreshold = false;

        for (int x = start; x <= end; x++)
        {
            double value = GetY(leftMap, x) + GetY(rightMap, x);
            double distance = Math.Abs(x - nominal);
            bool underThreshold = value <= threshold;

            if (underThreshold)
            {
                if (!foundUnderThreshold || distance < bestDistance ||
                    (Math.Abs(distance - bestDistance) < 0.001 && value < bestValue))
                {
                    bestX = x;
                    bestValue = value;
                    bestDistance = distance;
                    foundUnderThreshold = true;
                }

                continue;
            }

            if (!foundUnderThreshold &&
                (value < bestValue || (Math.Abs(value - bestValue) < 0.001 && distance < bestDistance)))
            {
                bestX = x;
                bestValue = value;
                bestDistance = distance;
            }
        }

        return new ReadValley(boundary, bestX, bestValue, threshold);
    }

    private static double EstimateReadSpacing(AnalysisResult result)
    {
        var shifts = result.StatePeaks
            .Select(p => p.AlignmentShiftMv)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DistinctBy(v => Math.Round(v, 3))
            .OrderBy(v => v)
            .ToArray();

        var diffs = new List<double>();
        for (int i = 1; i < shifts.Length; i++)
        {
            double diff = shifts[i] - shifts[i - 1];
            if (diff > 0)
                diffs.Add(diff);
        }

        if (diffs.Count > 0)
            return diffs.OrderBy(v => v).ElementAt(diffs.Count / 2);

        return result.StateCount switch
        {
            4 => 145,
            8 => 80,
            16 => 40,
            _ => 80
        };
    }

    private static double EstimateBoundaryThreshold(AnalysisResult result, int leftIndex, int rightIndex)
    {
        int leftCount = GetStateCellCount(result, leftIndex);
        int rightCount = GetStateCellCount(result, rightIndex);
        double expected = Math.Max(leftCount, rightCount);
        if (expected <= 0 && result.StateCount > 0)
            expected = (double)result.TotalCells / result.StateCount;

        return Math.Max(1, expected * 0.001);
    }

    private static int GetStateCellCount(AnalysisResult result, int stateIndex)
    {
        return result.StatePeaks
            .FirstOrDefault(p => p.StateIndex == stateIndex)
            ?.TotalCellCount ?? 0;
    }

    private static double[] GetCurveValues(AnalysisResult result, int curveIndex)
    {
        return curveIndex >= 0 && curveIndex < result.IncrementCurves.Length
            ? result.IncrementCurves[curveIndex]
            : Array.Empty<double>();
    }

    private static int GetCurveLength(AnalysisResult result, int curveIndex)
    {
        return curveIndex >= 0 && curveIndex < result.IncrementCurves.Length
            ? result.IncrementCurves[curveIndex].Length
            : 0;
    }

    private static Dictionary<int, double> BuildPointMap(double[] xs, double[] ys)
    {
        var map = new Dictionary<int, double>();
        int count = Math.Min(xs.Length, ys.Length);
        for (int i = 0; i < count; i++)
        {
            int x = (int)Math.Round(xs[i]);
            map[x] = map.TryGetValue(x, out double current)
                ? Math.Max(current, ys[i])
                : ys[i];
        }

        return map;
    }

    private static double GetY(Dictionary<int, double> values, int x)
    {
        return values.TryGetValue(x, out double value) ? value : 0;
    }

    private static double FindPeakX(double[] xs, double[] ys)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0)
            return double.NaN;

        int peak = 0;
        for (int i = 1; i < count; i++)
        {
            if (ys[i] > ys[peak])
                peak = i;
        }

        return xs[peak];
    }

    private static void ApplyToolStyleLimits(Plot plot, AnalysisResult result, ChartConfig chartConfig, bool logScale)
    {
        var allX = result.IncrementCurveXValues.SelectMany(x => x).ToArray();
        var allY = result.IncrementCurves.SelectMany(y => y).ToArray();
        if (chartConfig.XMin < chartConfig.XMax)
        {
            plot.Axes.SetLimitsX(chartConfig.XMin, chartConfig.XMax);
        }
        else if (allX.Length > 0)
        {
            double minX = Math.Min(-128, Math.Floor(allX.Min() / 16) * 16);
            double maxX = Math.Max(640, Math.Ceiling(allX.Max() / 16) * 16);
            plot.Axes.SetLimitsX(minX, maxX);
        }

        if (logScale)
        {
            plot.Axes.SetLimitsY(-0.05, 3.2);
        }
        else if (allY.Length > 0)
        {
            double maxY = Math.Max(1000, Math.Ceiling(allY.Max() / 100) * 100 + 100);
            if (chartConfig.YMin < chartConfig.YMax)
                plot.Axes.SetLimitsY(chartConfig.YMin, chartConfig.YMax);
            else
                plot.Axes.SetLimitsY(0, maxY);
        }
    }

    private readonly record struct ReadValley(int BoundaryIndex, double X, double ErrorCount, double Threshold);

    private static void DrawLimitMissTable(string filePath, IReadOnlyList<LimitMissStat> stats)
    {
        if (stats.Count == 0)
            return;

        using var bitmap = SKBitmap.Decode(filePath);
        using var canvas = new SKCanvas(bitmap);
        using var border = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true };
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont { Size = 14 };

        float cellW = 48;
        float cellH = 20;
        float tableW = cellW * 3;
        float tableH = cellH * (stats.Count + 1);
        float left = bitmap.Width - tableW - 22;
        float top = bitmap.Height / 2f - tableH / 2f - 8;

        DrawCell(canvas, border, text, font, left, top, cellW, cellH, string.Empty);
        DrawCell(canvas, border, text, font, left + cellW, top, cellW, cellH, "LOR");
        DrawCell(canvas, border, text, font, left + cellW * 2, top, cellW, cellH, "ROR");

        for (int i = 0; i < stats.Count; i++)
        {
            float y = top + cellH * (i + 1);
            DrawCell(canvas, border, text, font, left, y, cellW, cellH, stats[i].Label);
            DrawCell(canvas, border, text, font, left + cellW, y, cellW, cellH, stats[i].LeftOutOfRange.ToString());
            DrawCell(canvas, border, text, font, left + cellW * 2, y, cellW, cellH, stats[i].RightOutOfRange.ToString());
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
        data.SaveTo(stream);
    }

    private static void DrawCell(
        SKCanvas canvas,
        SKPaint border,
        SKPaint text,
        SKFont font,
        float x,
        float y,
        float width,
        float height,
        string value)
    {
        canvas.DrawRect(x, y, width, height, border);
        if (string.IsNullOrEmpty(value))
            return;

        var bounds = new SKRect();
        font.MeasureText(value, out bounds, text);
        float textX = x + (width - bounds.Width) / 2 - bounds.Left;
        float textY = y + (height - bounds.Height) / 2 - bounds.Top;
        canvas.DrawText(value, textX, textY, SKTextAlign.Left, font, text);
    }
}
