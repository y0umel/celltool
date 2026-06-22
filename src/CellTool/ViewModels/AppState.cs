using System.Collections.ObjectModel;
using CellTool.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CellTool.ViewModels;

public partial class AppState : ObservableObject
{
    [ObservableProperty]
    private string _inputDirectory = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _excelFilePath = string.Empty;

    [ObservableProperty]
    private string _groupModelFilePath = string.Empty;

    [ObservableProperty]
    private string _referenceFilePath = string.Empty;

    [ObservableProperty]
    private int _voltageMin = -128;

    [ObservableProperty]
    private int _voltageMax = 127;

    [ObservableProperty]
    private int _voltageStep = 1;

    [ObservableProperty]
    private int _wlCount = 192;

    [ObservableProperty]
    private int _startPage;

    [ObservableProperty]
    private string _grayCodeOrder = "U-M-L";

    [ObservableProperty]
    private string _slcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _mlcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _tlcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _qlcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _selectedDieName = string.Empty;

    [ObservableProperty]
    private string _selectedManufacturer = string.Empty;

    [ObservableProperty]
    private ChipInfo? _selectedChip;

    [ObservableProperty]
    private GroupModel? _loadedGroupModel;

    [ObservableProperty]
    private AnalysisResult? _lastResult;

    [ObservableProperty]
    private double[] _lastVoltages = Array.Empty<double>();

    [ObservableProperty]
    private bool _isDarkTheme = true;

    public ObservableCollection<ChipInfo> AllChips { get; } = new();
    public ObservableCollection<string> AvailableManufacturers { get; } = new();
    public ObservableCollection<ChipInfo> AvailableChips { get; } = new();

    [ObservableProperty]
    private ChartConfig _chartConfig = new();

    public event EventHandler? AnalysisUpdated;

    public AnalysisConfig CreateAnalysisConfig()
    {
        return new AnalysisConfig
        {
            InputDirectory = InputDirectory,
            OutputDirectory = OutputDirectory,
            ExcelFilePath = ExcelFilePath,
            GroupModelPath = GroupModelFilePath,
            ReferenceFilePath = ReferenceFilePath,
            Manufacturer = SelectedChip?.Manufacturer ?? SelectedManufacturer,
            DieName = SelectedChip?.DieName ?? SelectedDieName,
            VoltageMinCode = VoltageMin,
            VoltageMaxCode = VoltageMax,
            VoltageStepCode = VoltageStep,
            WlCount = WlCount,
            StartPage = StartPage,
            GrayCodeOrder = GrayCodeOrder,
            SlcWlEncoding = SlcWlEncoding,
            MlcWlEncoding = MlcWlEncoding,
            TlcWlEncoding = TlcWlEncoding,
            QlcWlEncoding = QlcWlEncoding
        };
    }

    public void ApplyConfiguration(AppConfiguration configuration)
    {
        var analysis = configuration.Analysis;
        InputDirectory = analysis.InputDirectory;
        OutputDirectory = analysis.OutputDirectory;
        ExcelFilePath = analysis.ExcelFilePath;
        GroupModelFilePath = analysis.GroupModelPath;
        ReferenceFilePath = analysis.ReferenceFilePath;
        VoltageMin = analysis.VoltageMinCode;
        VoltageMax = analysis.VoltageMaxCode;
        VoltageStep = analysis.VoltageStepCode;
        WlCount = analysis.WlCount;
        StartPage = analysis.StartPage;
        GrayCodeOrder = string.IsNullOrWhiteSpace(analysis.GrayCodeOrder)
            ? "U-M-L"
            : analysis.GrayCodeOrder;
        SlcWlEncoding = analysis.SlcWlEncoding;
        MlcWlEncoding = analysis.MlcWlEncoding;
        TlcWlEncoding = analysis.TlcWlEncoding;
        QlcWlEncoding = analysis.QlcWlEncoding;
        SelectedManufacturer = analysis.Manufacturer;
        SelectedDieName = analysis.DieName;
        IsDarkTheme = configuration.IsDarkTheme;
        ChartConfig = configuration.Chart ?? new ChartConfig();
    }

    public AppConfiguration CreateConfiguration()
    {
        return new AppConfiguration
        {
            Analysis = CreateAnalysisConfig(),
            Chart = ChartConfig,
            IsDarkTheme = IsDarkTheme
        };
    }

    public void SetLastAnalysis(AnalysisResult result)
    {
        LastResult = result;
        LastVoltages = result.VoltageCodes;
        AnalysisUpdated?.Invoke(this, EventArgs.Empty);
    }
}
