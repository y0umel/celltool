using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CellTool.Models;
using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CellTool.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly AnalysisEngine engine = new();
    private readonly VoltageFileReader voltageFileReader = new();
    private readonly CsvExporter csvExporter = new();
    private readonly ChartRenderer chartRenderer = new();
    private CancellationTokenSource? cts;

    public HomeViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public HomeViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        this.state.PropertyChanged += OnStatePropertyChanged;
    }

    public string InputDirectory
    {
        get => state.InputDirectory;
        set
        {
            if (state.InputDirectory == value) return;
            state.InputDirectory = value;
            OnPropertyChanged();
        }
    }

    public string OutputDirectory
    {
        get => state.OutputDirectory;
        set
        {
            if (state.OutputDirectory == value) return;
            state.OutputDirectory = value;
            OnPropertyChanged();
        }
    }

    public int VoltageMin
    {
        get => state.VoltageMin;
        set
        {
            if (state.VoltageMin == value) return;
            state.VoltageMin = value;
            OnPropertyChanged();
        }
    }

    public int VoltageMax
    {
        get => state.VoltageMax;
        set
        {
            if (state.VoltageMax == value) return;
            state.VoltageMax = value;
            OnPropertyChanged();
        }
    }

    public int VoltageStep
    {
        get => state.VoltageStep;
        set
        {
            if (state.VoltageStep == value) return;
            state.VoltageStep = value;
            OnPropertyChanged();
        }
    }

    public int WlCount
    {
        get => state.WlCount;
        set
        {
            if (state.WlCount == value) return;
            state.WlCount = value;
            OnPropertyChanged();
        }
    }

    public int StartPage
    {
        get => state.StartPage;
        set
        {
            if (state.StartPage == value) return;
            state.StartPage = value;
            OnPropertyChanged();
        }
    }

    public string SelectedManufacturer
    {
        get => state.SelectedManufacturer;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (state.SelectedManufacturer == value) return;
            state.SelectManufacturer(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableChips));
            OnPropertyChanged(nameof(SelectedChip));
            OnPropertyChanged(nameof(WlCount));
            OnPropertyChanged(nameof(ChipStatus));
        }
    }

    public ChipInfo? SelectedChip
    {
        get => state.SelectedChip;
        set
        {
            if (state.SelectedChip == value) return;
            state.SelectChip(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedManufacturer));
            OnPropertyChanged(nameof(WlCount));
            OnPropertyChanged(nameof(ChipStatus));
        }
    }

    public ObservableCollection<string> AvailableManufacturers => state.AvailableManufacturers;
    public ObservableCollection<ChipInfo> AvailableChips => state.AvailableChips;

    public IReadOnlyList<TransitionDetectionModeOption> TransitionDetectionModeOptions { get; } =
    [
        new(TransitionDetectionMode.SlidingWindow, "滑动窗口", "当前稳定长度/support 逻辑"),
        new(TransitionDetectionMode.StepFit, "阶跃拟合", "测试入口，当前回退到滑动窗口"),
        new(TransitionDetectionMode.BayesianChangePoint, "贝叶斯变点", "测试入口，当前回退到滑动窗口")
    ];

    public TransitionDetectionModeOption? SelectedTransitionDetectionMode
    {
        get => TransitionDetectionModeOptions.FirstOrDefault(option => option.Mode == state.TransitionDetectionMode)
               ?? TransitionDetectionModeOptions[0];
        set
        {
            if (value is null || state.TransitionDetectionMode == value.Mode)
                return;

            state.TransitionDetectionMode = value.Mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransitionDetectionModeDescription));
        }
    }

    public string TransitionDetectionModeDescription =>
        SelectedTransitionDetectionMode?.Description ?? string.Empty;

    public string ChipStatus => state.SelectedChip is null
        ? "未选择芯片"
        : $"{state.SelectedChip.Manufacturer} / {state.SelectedChip.DieName} - {state.SelectedChip.Type}, {FormatOptional(state.SelectedChip.PageTotalBytes)} B/page, {FormatOptional(state.SelectedChip.CodewordBytes)} B/CW, WL/Block {FormatOptional(state.SelectedChip.WlPerBlock)}, WL编码 {FormatEncoding(state.SelectedChip.WlEncoding)}";

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public ObservableCollection<string> LogEntries { get; } = new();

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.InputDirectory):
                OnPropertyChanged(nameof(InputDirectory));
                break;
            case nameof(AppState.OutputDirectory):
                OnPropertyChanged(nameof(OutputDirectory));
                break;
            case nameof(AppState.VoltageMin):
                OnPropertyChanged(nameof(VoltageMin));
                break;
            case nameof(AppState.VoltageMax):
                OnPropertyChanged(nameof(VoltageMax));
                break;
            case nameof(AppState.VoltageStep):
                OnPropertyChanged(nameof(VoltageStep));
                break;
            case nameof(AppState.WlCount):
                OnPropertyChanged(nameof(WlCount));
                break;
            case nameof(AppState.StartPage):
                OnPropertyChanged(nameof(StartPage));
                break;
            case nameof(AppState.SelectedManufacturer):
                OnPropertyChanged(nameof(SelectedManufacturer));
                OnPropertyChanged(nameof(AvailableChips));
                break;
            case nameof(AppState.SelectedChip):
                OnPropertyChanged(nameof(SelectedChip));
                OnPropertyChanged(nameof(ChipStatus));
                break;
            case nameof(AppState.SelectedDieName):
                OnPropertyChanged(nameof(ChipStatus));
                break;
            case nameof(AppState.TransitionDetectionMode):
                OnPropertyChanged(nameof(SelectedTransitionDetectionMode));
                OnPropertyChanged(nameof(TransitionDetectionModeDescription));
                break;
        }
    }

    private static string FormatOptional(int? value) =>
        value.HasValue ? value.Value.ToString() : "未配置";

    private static string FormatEncoding(int[] encoding) =>
        encoding.Length > 0 ? string.Join(",", encoding) : "未配置";

    [RelayCommand]
    private void BrowseInputDirectory()
    {
        var path = dialogs.SelectFolder("选择输入目录");
        if (path is not null)
            InputDirectory = path;
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var path = dialogs.SelectFolder("选择输出目录");
        if (path is not null)
            OutputDirectory = path;
    }

    [RelayCommand]
    private async Task StartAnalysisAsync()
    {
        if (string.IsNullOrWhiteSpace(InputDirectory) || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            dialogs.ShowWarning("请选择输入和输出目录。");
            return;
        }

        if (state.SelectedChip is null || state.LoadedGroupModel is null)
        {
            dialogs.ShowWarning("请先在首页选择芯片，并在数据配置中加载组模型。");
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
        IsAnalyzing = true;
        Progress = 0;
        LogEntries.Clear();
        Log("正在开始分析...");

        cts = new CancellationTokenSource();

        try
        {
            var config = state.CreateAnalysisConfig();
            Log($"使用 {state.SelectedChip.Type} 手动L间距: {config.GetLevelSpacingMv(state.SelectedChip.Type):F2} code。");
            var scan = voltageFileReader.ScanDirectoryDetailed(
                config.InputDirectory,
                config.VoltageMinMv,
                config.VoltageMaxMv,
                config.VoltageStepMv);

            Log($"扫描到 {scan.TotalCandidateFiles} 个电压档位候选文件；可用 {scan.Files.Count} 个，文件名不匹配 {scan.NameMismatchFiles} 个，范围过滤 {scan.RangeFilteredFiles} 个，步长过滤 {scan.StepFilteredFiles} 个。");
            if (scan.Files.Count == 0)
            {
                throw new InvalidDataException(
                    $"没有匹配的电压扫描文件。请检查输入目录、文件名和电压档位范围。文件名应类似 0、10、-127，也兼容 0.bin、10.bin、-127.bin；数字表示 code，1 code = 10mV。当前范围 {config.VoltageMinCode}..{config.VoltageMaxCode} code，步长 {config.VoltageStepCode} code。");
            }

            var progress = new Progress<(double p, string msg)>(update =>
            {
                Progress = update.p * 100;
                StatusMessage = update.msg;
                Log(update.msg);
            });

            var result = await engine.RunAsync(
                config,
                state.SelectedChip,
                state.LoadedGroupModel,
                progress,
                cts.Token);

            state.SetLastAnalysis(result);
            ExportResults(result);

            Log($"Analysis complete. {result.TotalCells} cells, {result.VoltageCount} voltage codes.");
            Log("结果已导出到输出目录。");
        }
        catch (OperationCanceledException)
        {
            Log("分析已取消。");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            dialogs.ShowError($"Analysis failed: {ex.Message}");
        }
        finally
        {
            IsAnalyzing = false;
            cts?.Dispose();
            cts = null;
        }
    }

    [RelayCommand]
    private void CancelAnalysis() => cts?.Cancel();

    public void Log(string message)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        Application.Current?.Dispatcher.Invoke(() =>
            LogEntries.Add($"[{ts}] {message}"));
    }

    private void ExportResults(AnalysisResult result)
    {
        string peakPath = Path.Combine(OutputDirectory, "peaks.csv");
        string bestPath = Path.Combine(OutputDirectory, "best_voltages.csv");
        string summaryPath = Path.Combine(OutputDirectory, "analysis_summary.csv");
        string chartPath = Path.Combine(OutputDirectory, "vt_distribution.png");

        csvExporter.ExportPeakReport(peakPath, result.StatePeaks, result.VoltageCodes);
        csvExporter.ExportBestVoltages(bestPath, result.BestReadVoltages, result.TransitionLabels);
        csvExporter.ExportSummary(summaryPath, result);

        if (result.BestVoltageErrors is not null)
        {
            csvExporter.ExportCodewordErrors(
                Path.Combine(OutputDirectory, "codeword_errors_best_voltage.csv"),
                result.BestVoltageErrors);
        }

        if (result.ZeroOffsetErrors is not null)
        {
            csvExporter.ExportCodewordErrors(
                Path.Combine(OutputDirectory, "codeword_errors_zero_offset.csv"),
                result.ZeroOffsetErrors);
        }

        chartRenderer.SavePng(chartPath, result, state.ChartConfig);
    }
}

public sealed record TransitionDetectionModeOption(
    TransitionDetectionMode Mode,
    string DisplayName,
    string Description);
