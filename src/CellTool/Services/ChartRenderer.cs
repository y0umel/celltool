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
        AddCurveSeries(linear, result, chartConfig, logScale: false);
        AddCurveSeries(log, result, chartConfig, logScale: true);
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
        AddCurveSeries(plot, result, chartConfig, logScale);
        AddMarkersAndLegend(plot, result, chartConfig);
        return plot;
    }

    private static void AddCurveSeries(Plot plot, AnalysisResult result, ChartConfig chartConfig, bool logScale)
    {
        for (int s = 0; s < result.IncrementCurves.Length; s++)
        {
            var curve = result.IncrementCurves[s];
            var curveX = GetCurveXValues(result, ChartAxisMapper.ToDisplayVoltageBins(result.VoltageCodes), s, curve.Length);
            if (curve.Length == 0 || curveX.Length == 0)
                continue;

            var color = Color.FromHex(s < DefaultColors.Length ? DefaultColors[s] : DefaultColors[^1]);
            string label = s < result.TransitionLabels.Length ? result.TransitionLabels[s] : $"L{s}";
            var display = BuildDisplayCurve(curveX, curve, chartConfig);
            var ys = logScale
                ? BuildLogDisplayY(display.Y)
                : display.Y;
            bool legendAdded = false;
            foreach (var segment in SplitSegments(display.X, ys))
            {
                var scatter = plot.Add.Scatter(segment.X, segment.Y);
                scatter.Color = color;
                scatter.LineWidth = 2;
                scatter.MarkerSize = 0;
                if (!legendAdded)
                {
                    scatter.LegendText = label;
                    legendAdded = true;
                }
            }
        }
    }

    internal static (double[] X, double[] Y) BuildDisplayCurve(double[] xs, double[] ys, ChartConfig chartConfig)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0)
            return (Array.Empty<double>(), Array.Empty<double>());

        var ordered = Enumerable.Range(0, count)
            .Select(i => (X: xs[i], Y: ys[i]))
            .OrderBy(p => p.X)
            .ToArray();
        var orderedX = ordered.Select(p => p.X).ToArray();
        var orderedY = ordered.Select(p => p.Y).ToArray();

        if (!chartConfig.UseSavitzkyGolaySmoothing)
            return (orderedX, orderedY);

        int window = NormalizeSavitzkyGolayWindow(chartConfig.SavitzkyGolayWindow);
        var outputX = new List<double>();
        var outputY = new List<double>();
        var segments = SplitSegments(orderedX, orderedY).ToArray();
        double typicalStep = Math.Max(1, Math.Round(EstimateTypicalStep(orderedX)));
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var smoothed = ApplySavitzkyGolayQuadratic(segment.Y, window);
            if (i > 0 && segment.X.Length > 0)
            {
                outputX.Add(Math.Round(segment.X[0] - typicalStep, 6));
                outputY.Add(0);
            }

            outputX.AddRange(segment.X);
            outputY.AddRange(smoothed.Select(y => Math.Max(0, y)));
            if (i < segments.Length - 1 && segment.X.Length > 0)
            {
                outputX.Add(Math.Round(segment.X[^1] + typicalStep, 6));
                outputY.Add(0);
            }
        }

        return (outputX.ToArray(), outputY.ToArray());
    }

    internal static double[] ApplySavitzkyGolayQuadratic(double[] values, int window)
    {
        window = NormalizeSavitzkyGolayWindow(window);
        if (values.Length < window)
            return values.ToArray();

        int radius = window / 2;
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (i < radius || i >= values.Length - radius)
            {
                result[i] = values[i];
                continue;
            }

            double sum = 0;
            for (int k = -radius; k <= radius; k++)
                sum += SavitzkyGolayQuadraticCoefficient(k, radius) * values[i + k];

            result[i] = sum;
        }

        return result;
    }

    internal static double[] BuildLogDisplayY(double[] values)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = Math.Max(0, Math.Log10(Math.Max(1, values[i])));

        return result;
    }

    private static int NormalizeSavitzkyGolayWindow(int window)
    {
        if (window < 5)
            return 5;
        if (window > 9)
            return 9;
        return window % 2 == 0 ? window + 1 : window;
    }

    private static double SavitzkyGolayQuadraticCoefficient(int offset, int radius)
    {
        int n = radius;
        double numerator = 3.0 * (3 * n * n + 3 * n - 1 - 5 * offset * offset);
        double denominator = (2 * n + 1.0) * (4 * n * n + 4 * n - 3);
        return numerator / denominator;
    }

    private static IEnumerable<(double[] X, double[] Y)> SplitSegments(double[] xs, double[] ys)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0)
            yield break;

        var segmentX = new List<double> { xs[0] };
        var segmentY = new List<double> { ys[0] };
        double typicalStep = EstimateTypicalStep(xs);
        double maxGap = Math.Max(2, typicalStep * 3);

        for (int i = 1; i < count; i++)
        {
            if (xs[i] - xs[i - 1] > maxGap)
            {
                yield return (segmentX.ToArray(), segmentY.ToArray());
                segmentX.Clear();
                segmentY.Clear();
            }

            segmentX.Add(xs[i]);
            segmentY.Add(ys[i]);
        }

        if (segmentX.Count > 0)
            yield return (segmentX.ToArray(), segmentY.ToArray());
    }

    private static double EstimateTypicalStep(double[] xs)
    {
        var diffs = new List<double>();
        for (int i = 1; i < xs.Length; i++)
        {
            double diff = xs[i] - xs[i - 1];
            if (diff > 0)
                diffs.Add(diff);
        }

        if (diffs.Count == 0)
            return 1;

        diffs.Sort();
        return diffs[diffs.Count / 2];
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

        if (chartConfig.ShowPeakOffsetAnnotations)
            AddPeakOffsetAnnotations(plot, result);

        if (chartConfig.ShowLegend)
            plot.ShowLegend(Edge.Right);
    }

    private static void AddPeakOffsetAnnotations(Plot plot, AnalysisResult result)
    {
        var annotations = BuildPeakOffsetAnnotations(result);
        foreach (var annotation in annotations)
        {
            var color = Color.FromHex(annotation.ColorHex);
            var marker = plot.Add.Marker(annotation.PeakX, annotation.PeakY);
            marker.Color = color;
            marker.MarkerSize = 6;
            marker.Shape = MarkerShape.FilledCircle;

            foreach (var readX in annotation.ReadXs)
            {
                var line = plot.Add.Line(readX, annotation.PeakY, annotation.PeakX, annotation.PeakY);
                line.Color = color;
                line.LinePattern = LinePattern.Dotted;
                line.LineWidth = 1;
            }

            double yOffset = Math.Max(20, annotation.PeakY * 0.08);
            var text = plot.Add.Text(annotation.Label, annotation.PeakX, annotation.PeakY + yOffset);
            text.LabelFontColor = color;
            text.LabelFontSize = 12;
            text.Alignment = Alignment.LowerCenter;
        }
    }

    internal static IReadOnlyList<PeakOffsetAnnotation> BuildPeakOffsetAnnotations(AnalysisResult result)
    {
        int stateCount = result.StateCount > 0 ? result.StateCount : result.IncrementCurves.Length;
        if (stateCount <= 1)
            return Array.Empty<PeakOffsetAnnotation>();

        double spacing = EstimateReadSpacing(result);
        var annotations = new List<PeakOffsetAnnotation>();
        for (int level = 0; level < result.IncrementCurves.Length && level < stateCount; level++)
        {
            var ys = result.IncrementCurves[level];
            var xs = GetCurveXValues(result, ChartAxisMapper.ToDisplayVoltageBins(result.VoltageCodes), level, ys.Length);
            if (ys.Length == 0 || xs.Length == 0)
                continue;

            int peakIndex = FindPeakIndex(ys);
            if (peakIndex < 0 || peakIndex >= xs.Length)
                continue;

            double peakX = xs[peakIndex];
            double peakY = ys[peakIndex];
            if (peakY <= 0)
                continue;

            var readXs = AdjacentReadXs(level, stateCount, spacing).ToArray();
            if (readXs.Length == 0)
                continue;

            string label = string.Join("  ", readXs.Select(readX =>
            {
                int readIndex = (int)Math.Round(readX / spacing) + 1;
                double offset = peakX - readX;
                return $"R{readIndex} {offset:+0;-0;0}";
            }));

            annotations.Add(new PeakOffsetAnnotation(
                Level: level,
                PeakX: peakX,
                PeakY: peakY,
                ReadXs: readXs,
                Label: label,
                ColorHex: level < DefaultColors.Length ? DefaultColors[level] : DefaultColors[^1]));
        }

        return annotations;
    }

    private static IEnumerable<double> AdjacentReadXs(int level, int stateCount, double spacing)
    {
        if (level <= 0)
        {
            yield return 0;
            yield break;
        }

        if (level >= stateCount - 1)
        {
            yield return (stateCount - 2) * spacing;
            yield break;
        }

        yield return (level - 1) * spacing;
        yield return level * spacing;
    }

    private static int FindPeakIndex(double[] values)
    {
        int peakIndex = -1;
        double peakValue = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > peakValue)
            {
                peakValue = values[i];
                peakIndex = i;
            }
        }

        return peakIndex;
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

        var shifts = result.StatePeaks
            .Select(p => p.AlignmentShiftMv)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DistinctBy(v => Math.Round(v, 3))
            .OrderBy(v => v)
            .ToArray();
        if (shifts.Length >= boundaryCount)
        {
            return shifts
                .Take(boundaryCount)
                .Select((x, boundary) => new ReadValley(boundary, x))
                .ToArray();
        }

        double spacing = EstimateReadSpacing(result);
        return Enumerable.Range(0, boundaryCount)
            .Select(boundary => new ReadValley(boundary, boundary * spacing))
            .ToArray();
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

    private readonly record struct ReadValley(int BoundaryIndex, double X);

    internal readonly record struct PeakOffsetAnnotation(
        int Level,
        double PeakX,
        double PeakY,
        double[] ReadXs,
        string Label,
        string ColorHex);

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
