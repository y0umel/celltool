using CellTool.Models;
using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;
using System.ComponentModel;

namespace CellTool.ViewModels;

public partial class ChartConfigViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly ChartRenderer renderer = new();
    private bool applyingConfig;

    public ChartConfigViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public ChartConfigViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        ApplyConfig(state.ChartConfig);
        state.AnalysisUpdated += (_, _) => RefreshChart();
        state.PropertyChanged += OnStatePropertyChanged;
    }

    [ObservableProperty]
    private string _chartTitle = "Vt Incremental Distribution";

    [ObservableProperty]
    private string _xAxisLabel = "Voltage Code from Lower Bound (1 code = 10mV)";

    [ObservableProperty]
    private string _yAxisLabel = "Cell Count";

    [ObservableProperty]
    private double _xMin;

    [ObservableProperty]
    private double _xMax;

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
        state.LastResult = result;
        state.LastVoltages = voltagesMv;
        RefreshChart();
    }

    partial void OnChartTitleChanged(string value) => SaveAndRefresh();
    partial void OnXAxisLabelChanged(string value) => SaveAndRefresh();
    partial void OnYAxisLabelChanged(string value) => SaveAndRefresh();
    partial void OnXMinChanged(double value) => SaveAndRefresh();
    partial void OnXMaxChanged(double value) => SaveAndRefresh();
    partial void OnYMinChanged(double value) => SaveAndRefresh();
    partial void OnYMaxChanged(double value) => SaveAndRefresh();
    partial void OnShowBoundaryLinesChanged(bool value) => SaveAndRefresh();
    partial void OnShowReadVoltageChanged(bool value) => SaveAndRefresh();
    partial void OnShowLegendChanged(bool value) => SaveAndRefresh();

    [RelayCommand]
    private void SaveChartTemplate()
    {
        var file = dialogs.SaveFile("保存图表模板", "JSON files (*.json)|*.json|All files (*.*)|*.*", "chart-template.json");
        if (file is null) return;

        try
        {
            new ConfigPersistence().Save(file, BuildConfig());
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"保存图表模板失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void LoadChartTemplate()
    {
        var file = dialogs.OpenFile("加载图表模板", "JSON files (*.json)|*.json|All files (*.*)|*.*");
        if (file is null) return;

        try
        {
            var config = new ConfigPersistence().Load<ChartConfig>(file);
            ApplyConfig(config);
            SaveAndRefresh();
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"加载图表模板失败: {ex.Message}");
        }
    }

    private void SaveAndRefresh()
    {
        if (applyingConfig)
            return;

        state.ChartConfig = BuildConfig();
        RefreshChart();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppState.ChartConfig))
            return;

        ApplyConfig(state.ChartConfig);
        RefreshChart();
    }

    private void RefreshChart()
    {
        if (state.LastResult is null) return;

        var plot = renderer.Render(state.LastResult, state.LastVoltages, BuildConfig());
        ChartUpdated?.Invoke(plot);
    }

    public void RefreshPreview() => RefreshChart();

    private ChartConfig BuildConfig()
    {
        return new ChartConfig
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
    }

    private void ApplyConfig(ChartConfig config)
    {
        applyingConfig = true;
        try
        {
            ChartTitle = config.Title;
            XAxisLabel = config.XAxisLabel;
            YAxisLabel = config.YAxisLabel;
            XMin = config.XMin;
            XMax = config.XMax;
            YMin = config.YMin;
            YMax = config.YMax;
            ShowBoundaryLines = config.ShowBoundaryLines;
            ShowReadVoltage = config.ShowReadVoltage;
            ShowLegend = config.ShowLegend;
        }
        finally
        {
            applyingConfig = false;
        }
    }
}
