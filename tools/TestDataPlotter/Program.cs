using CellTool.Models;
using CellTool.Services;
using ScottPlot;

var dataDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : "/home/doc/workspace/testdata";
var outputPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(dataDir, "generated_right.png");

var chipDbPath = Path.Combine(dataDir, "Nand Flash Infomation.csv");
var groupModelPath = Path.Combine(dataDir, "x4-9060_GroupModel.txt");
var offsetDir = Path.Combine(dataDir, "offset_file");
var originPath = Path.Combine(dataDir, "origin_data.bin");

var chip = new ExcelParser()
    .LoadDatabase(chipDbPath)
    .First(c => c.DieName.StartsWith("X4-9060", StringComparison.OrdinalIgnoreCase));
if (chip.WlPerBlock is not > 0 || chip.PageTotalBytes is not > 0 || chip.WlEncoding.Length == 0)
    throw new InvalidDataException("Test chip entry is missing WL count, page size, or WL encoding.");

var groupModel = new GroupModelParser().LoadFromFile(groupModelPath, expectedWlCount: chip.WlPerBlock.Value);

Console.WriteLine($"Chip: {chip}");
Console.WriteLine($"Input: {offsetDir}");
Console.WriteLine($"Source: {originPath}");

var levelCurves = BuildLevelErrorCurves(
    originPath,
    offsetDir,
    groupModel.Entries[0],
    chip.PageTotalBytes.Value,
    chip.WlEncoding,
    chip.StateCount,
    grayCodeOrder: "U-M-L",
    levelSpacingCode: 80);
RenderToolStyle(outputPath, levelCurves, stateCount: chip.StateCount);

Console.WriteLine($"Wrote {outputPath}");
Console.WriteLine("Tool-style level curves:");
foreach (var (label, xs, ys) in levelCurves)
{
    int peak = FindPeakIndex(ys);
    double peakX = peak >= 0 ? xs[peak] : double.NaN;
    double peakY = peak >= 0 ? ys[peak] : 0;
    Console.WriteLine($"{label}: points={ys.Length}, peakX={peakX:F0}, peakY={peakY:F0}, sum={ys.Sum():F0}");
}

static List<(string Label, double[] Xs, double[] Ys)> BuildLevelErrorCurves(
    string originPath,
    string offsetDir,
    GroupEntry groupEntry,
    int pageTotalBytes,
    int[] wlEncoding,
    int stateCount,
    string grayCodeOrder,
    double levelSpacingCode)
{
    var reader = new VoltageFileReader();
    var files = reader.ScanDirectory(offsetDir, -128, 127, 1);
    var decoder = new GrayCodeDecoder(wlEncoding, bitsPerCell: 3, grayCodeOrder);
    var sourceBytes = reader.ReadWlBytes(originPath, groupEntry, pageTotalBytes);
    var sourceStates = decoder.DecodeWl(sourceBytes, pageTotalBytes);

    var cumulative = new double[stateCount][];
    for (int level = 0; level < stateCount; level++)
        cumulative[level] = new double[files.Count];

    for (int v = 0; v < files.Count; v++)
    {
        var currentBytes = File.ReadAllBytes(files[v].FilePath);
        var currentStates = decoder.DecodeWl(currentBytes, pageTotalBytes);
        for (int cell = 0; cell < sourceStates.Length; cell++)
        {
            int current = currentStates[cell];
            int source = sourceStates[cell];
            if (source == current || source < 0 || source >= stateCount)
                continue;

            cumulative[source][v]++;
        }
    }

    var curves = new List<(string Label, double[] Xs, double[] Ys)>();
    for (int level = 0; level < stateCount; level++)
    {
        var delta = SumCurves(
            ToIncreasingDistributionDelta(cumulative[level]),
            ToDecreasingDistributionDelta(cumulative[level]));
        delta = KeepDominantWindow(delta, minValue: 3, maxGap: 3, padding: 2);
        double levelPosition = level == stateCount - 1
            ? (stateCount - 2) * levelSpacingCode
            : level * levelSpacingCode;
        var points = BuildPoints(
            files.Select(f => (double)f.Code).ToArray(),
            delta,
            levelPosition);
        curves.Add(($"L{level}", points.Select(p => p.X).ToArray(), points.Select(p => p.Y).ToArray()));
    }

    return curves;
}

static (double X, double Y)[] BuildPoints(double[] voltageCodes, double[] curve, double levelPositionCode)
{
    var points = new List<(double X, double Y)>();
    for (int i = 0; i < curve.Length && i < voltageCodes.Length; i++)
    {
        if (curve[i] <= 0)
            continue;

        points.Add((levelPositionCode + voltageCodes[i], curve[i]));
    }

    return points.OrderBy(p => p.X).ToArray();
}

static double[] ToIncreasingDistributionDelta(double[] cumulativeCurve)
{
    if (cumulativeCurve.Length == 0)
        return Array.Empty<double>();

    var delta = new double[cumulativeCurve.Length];
    double maxSoFar = cumulativeCurve[0];
    for (int i = 1; i < cumulativeCurve.Length; i++)
    {
        double next = Math.Max(maxSoFar, cumulativeCurve[i]);
        delta[i] = Math.Max(0, next - maxSoFar);
        maxSoFar = next;
    }

    return delta;
}

static double[] ToDecreasingDistributionDelta(double[] cumulativeCurve)
{
    if (cumulativeCurve.Length == 0)
        return Array.Empty<double>();

    var delta = new double[cumulativeCurve.Length];
    double minSoFar = cumulativeCurve[0];
    for (int i = 1; i < cumulativeCurve.Length; i++)
    {
        double next = Math.Min(minSoFar, cumulativeCurve[i]);
        delta[i] = Math.Max(0, minSoFar - next);
        minSoFar = next;
    }

    return delta;
}

static double[] SumCurves(double[] left, double[] right)
{
    int length = Math.Max(left.Length, right.Length);
    var sum = new double[length];
    for (int i = 0; i < length; i++)
    {
        if (i < left.Length)
            sum[i] += left[i];
        if (i < right.Length)
            sum[i] += right[i];
    }

    return sum;
}

static double[] KeepDominantWindow(double[] curve, double minValue, int maxGap, int padding)
{
    var windows = new List<(int Start, int End, double Sum, double Peak)>();
    int start = -1;
    int end = -1;
    int gap = 0;
    double sum = 0;
    double peak = 0;

    for (int i = 0; i < curve.Length; i++)
    {
        if (curve[i] >= minValue)
        {
            if (start < 0)
                start = i;
            end = i;
            gap = 0;
            sum += curve[i];
            peak = Math.Max(peak, curve[i]);
            continue;
        }

        if (start >= 0)
        {
            gap++;
            if (gap > maxGap)
            {
                windows.Add((start, end, sum, peak));
                start = -1;
                end = -1;
                gap = 0;
                sum = 0;
                peak = 0;
            }
        }
    }

    if (start >= 0)
        windows.Add((start, end, sum, peak));

    if (windows.Count == 0)
        return curve;

    var best = windows
        .OrderByDescending(w => w.Peak)
        .ThenByDescending(w => w.Sum)
        .First();
    int keepStart = Math.Max(0, best.Start - padding);
    int keepEnd = Math.Min(curve.Length - 1, best.End + padding);
    var filtered = new double[curve.Length];
    for (int i = keepStart; i <= keepEnd; i++)
        filtered[i] = curve[i];

    return filtered;
}

static void RenderToolStyle(
    string outputPath,
    IReadOnlyList<(string Label, double[] Xs, double[] Ys)> curves,
    int stateCount)
{
    var mp = new ScottPlot.Multiplot();
    mp.AddPlots(2);
    mp.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 2, columns: 1);
    var linear = mp.GetPlot(0);
    var log = mp.GetPlot(1);

    ConfigurePlot(linear, title: "Vt Distribution", yLabel: "# Cells", logScale: false);
    ConfigurePlot(log, title: string.Empty, yLabel: "Log # Cells", logScale: true);

    var colors = new[]
    {
        "#006fff", "#2ca02c", "#ffbf00", "#7a00ff",
        "#ff7f0e", "#17becf", "#8b3f00", "#ff14b8"
    };

    for (int i = 0; i < curves.Count; i++)
    {
        var (label, xs, ys) = curves[i];
        if (xs.Length == 0 || ys.Length == 0)
            continue;

        var color = Color.FromHex(colors[i % colors.Length]);
        var linearScatter = linear.Add.Scatter(xs, ys);
        linearScatter.Color = color;
        linearScatter.LineWidth = 2;
        linearScatter.MarkerSize = 0;
        linearScatter.LegendText = label;

        var logYs = ys.Select(y => Math.Log10(Math.Max(1, y))).ToArray();
        var logScatter = log.Add.Scatter(xs, logYs);
        logScatter.Color = color;
        logScatter.LineWidth = 2;
        logScatter.MarkerSize = 0;
        logScatter.LegendText = label;
    }

    var valleys = FindReadValleys(curves, stateCount);
    AddReadValleyLines(linear, valleys);
    AddReadValleyLines(log, valleys);

    Console.WriteLine("0.1% valley candidates:");
    foreach (var valley in valleys)
    {
        Console.WriteLine(
            $"R{valley.BoundaryIndex + 1}: x={valley.X:F0}, cells={valley.ErrorCount:F1}, threshold={valley.Threshold:F1}");
    }

    linear.ShowLegend(Edge.Right);
    log.ShowLegend(Edge.Right);
    linear.Axes.SetLimits(-128, 640, -70, 1100);
    log.Axes.SetLimits(-128, 640, -0.05, 3.2);

    mp.SavePng(outputPath, 1536, 768);
}

static void ConfigurePlot(Plot plot, string title, string yLabel, bool logScale)
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

static void AddReadValleyLines(Plot plot, IReadOnlyList<(int BoundaryIndex, double X, double ErrorCount, double Threshold)> valleys)
{
    foreach (var valley in valleys)
    {
        var line = plot.Add.VerticalLine(valley.X);
        line.Color = Colors.Red;
        line.LinePattern = LinePattern.Dashed;
        line.LineWidth = 1;
    }
}

static IReadOnlyList<(int BoundaryIndex, double X, double ErrorCount, double Threshold)> FindReadValleys(
    IReadOnlyList<(string Label, double[] Xs, double[] Ys)> curves,
    int stateCount)
{
    const double spacing = 80;
    int boundaryCount = Math.Max(0, stateCount - 1);
    var valleys = new List<(int BoundaryIndex, double X, double ErrorCount, double Threshold)>(boundaryCount);

    for (int boundary = 0; boundary < boundaryCount; boundary++)
    {
        var left = boundary < curves.Count
            ? curves[boundary]
            : (Label: string.Empty, Xs: Array.Empty<double>(), Ys: Array.Empty<double>());
        var right = boundary + 1 < curves.Count
            ? curves[boundary + 1]
            : (Label: string.Empty, Xs: Array.Empty<double>(), Ys: Array.Empty<double>());
        double nominal = boundary * spacing;
        double searchStart = nominal - spacing / 2;
        double searchEnd = nominal + spacing / 2;
        double leftPeak = FindPeakX(left.Xs, left.Ys);
        double rightPeak = FindPeakX(right.Xs, right.Ys);

        if (!double.IsNaN(leftPeak) && !double.IsNaN(rightPeak) && Math.Abs(leftPeak - rightPeak) > 0.001)
        {
            double peakStart = Math.Min(leftPeak, rightPeak);
            double peakEnd = Math.Max(leftPeak, rightPeak);
            double constrainedStart = Math.Max(searchStart, peakStart);
            double constrainedEnd = Math.Min(searchEnd, peakEnd);
            if (constrainedStart <= constrainedEnd)
            {
                searchStart = constrainedStart;
                searchEnd = constrainedEnd;
            }
        }

        double threshold = Math.Max(1, Math.Max(left.Ys.Sum(), right.Ys.Sum()) * 0.001);
        valleys.Add(FindReadValley(boundary, nominal, searchStart, searchEnd, left.Xs, left.Ys, right.Xs, right.Ys, threshold));
    }

    return valleys;
}

static (int BoundaryIndex, double X, double ErrorCount, double Threshold) FindReadValley(
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
        start = end = (int)Math.Round(nominal);

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

    return (boundary, bestX, bestValue, threshold);
}

static Dictionary<int, double> BuildPointMap(double[] xs, double[] ys)
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

static double GetY(Dictionary<int, double> values, int x)
{
    return values.TryGetValue(x, out double value) ? value : 0;
}

static double FindPeakX(double[] xs, double[] ys)
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

static int FindPeakIndex(double[] values)
{
    if (values.Length == 0)
        return -1;

    int peak = 0;
    for (int i = 1; i < values.Length; i++)
    {
        if (values[i] > values[peak])
            peak = i;
    }

    return peak;
}
