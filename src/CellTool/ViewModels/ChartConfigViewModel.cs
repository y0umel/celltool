using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CellTool.Models;
using CellTool.Services;
using ScottPlot;

namespace CellTool.ViewModels;

public partial class ChartConfigViewModel : ObservableObject
{
    private readonly ChartRenderer _renderer = new();
    private AnalysisResult? _lastResult;
    private double[] _lastVoltages = Array.Empty<double>();

    [ObservableProperty]
    private string _chartTitle = "Vt Incremental Distribution";

    [ObservableProperty]
    private string _xAxisLabel = "Voltage Offset (mV)";

    [ObservableProperty]
    private string _yAxisLabel = "Cell Count";

    [ObservableProperty]
    private double _xMin;

    [ObservableProperty]
    private double _xMax = 3000;

    [ObservableProperty]
    private double _yMin;

    [ObservableProperty]
    private double _yMax = 10000;

    [ObservableProperty]
    private bool _showBoundaryLines = true;

    [ObservableProperty]
    private bool _showReadVoltage = true;

    [ObservableProperty]
    private bool _showLegend = true;

    public event Action<Plot>? ChartUpdated;

    public void SetData(AnalysisResult result, double[] voltagesMv)
    {
        _lastResult = result;
        _lastVoltages = voltagesMv;
        RefreshChart();
    }

    partial void OnChartTitleChanged(string value) => RefreshChart();
    partial void OnXAxisLabelChanged(string value) => RefreshChart();
    partial void OnYAxisLabelChanged(string value) => RefreshChart();
    partial void OnXMinChanged(double value) => RefreshChart();
    partial void OnXMaxChanged(double value) => RefreshChart();
    partial void OnYMinChanged(double value) => RefreshChart();
    partial void OnYMaxChanged(double value) => RefreshChart();
    partial void OnShowBoundaryLinesChanged(bool value) => RefreshChart();
    partial void OnShowReadVoltageChanged(bool value) => RefreshChart();
    partial void OnShowLegendChanged(bool value) => RefreshChart();

    private void RefreshChart()
    {
        if (_lastResult is null) return;

        var chartConfig = new ChartConfig
        {
            Title = ChartTitle,
            XAxisLabel = XAxisLabel,
            YAxisLabel = YAxisLabel,
            XMin = XMin,
            XMax = XMax,
            YMin = YMin,
            YMax = YMax,
            ShowBoundaryLines = ShowBoundaryLines,
            ShowReadVoltage = ShowReadVoltage,
            ShowLegend = ShowLegend
        };

        var plot = _renderer.Render(_lastResult, _lastVoltages, chartConfig);
        ChartUpdated?.Invoke(plot);
    }

    [RelayCommand]
    private void SaveChartTemplate() { /* TODO */ }

    [RelayCommand]
    private void LoadChartTemplate() { /* TODO */ }
}
