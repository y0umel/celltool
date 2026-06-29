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
    private int? _pageDataBytes;

    [ObservableProperty]
    private int? _pageRedundantBytes;

    [ObservableProperty]
    private int? _codewordsPerPage;

    [ObservableProperty]
    private double _mlcLevelSpacingMv = 145;

    [ObservableProperty]
    private double _tlcLevelSpacingMv = 80;

    [ObservableProperty]
    private double _qlcLevelSpacingMv = 40;

    [ObservableProperty]
    private string _mlcLevelSpacingCodes = "145,145";

    [ObservableProperty]
    private string _tlcLevelSpacingCodes = "80,80,80,80,80,80";

    [ObservableProperty]
    private string _qlcLevelSpacingCodes = "40,40,40,40,40,40,40,40,40,40,40,40,40,40";

    [ObservableProperty]
    private string _grayCodeOrder = "U-M-L";

    [ObservableProperty]
    private string _bitOrder = "MSB";

    [ObservableProperty]
    private string _slcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _mlcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _tlcWlEncoding = string.Empty;

    [ObservableProperty]
    private string _qlcWlEncoding = string.Empty;

    [ObservableProperty]
    private TransitionDetectionMode _transitionDetectionMode = TransitionDetectionMode.SlidingWindow;

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
    private bool _isDarkTheme;

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
            PageDataBytes = PageDataBytes,
            PageRedundantBytes = PageRedundantBytes,
            CodewordsPerPage = CodewordsPerPage,
            LevelSpacingMv = GetCurrentLevelSpacing(),
            MlcLevelSpacingMv = MlcLevelSpacingMv,
            TlcLevelSpacingMv = TlcLevelSpacingMv,
            QlcLevelSpacingMv = QlcLevelSpacingMv,
            MlcLevelSpacingCodes = MlcLevelSpacingCodes,
            TlcLevelSpacingCodes = TlcLevelSpacingCodes,
            QlcLevelSpacingCodes = QlcLevelSpacingCodes,
            GrayCodeOrder = GrayCodeOrder,
            BitOrder = BitOrder,
            SlcWlEncoding = SlcWlEncoding,
            MlcWlEncoding = MlcWlEncoding,
            TlcWlEncoding = TlcWlEncoding,
            QlcWlEncoding = QlcWlEncoding,
            TransitionDetectionMode = TransitionDetectionMode
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
        PageDataBytes = analysis.PageDataBytes;
        PageRedundantBytes = analysis.PageRedundantBytes;
        CodewordsPerPage = analysis.CodewordsPerPage;
        MlcLevelSpacingMv = analysis.MlcLevelSpacingMv > 0 ? analysis.MlcLevelSpacingMv : 145;
        TlcLevelSpacingMv = analysis.TlcLevelSpacingMv > 0 ? analysis.TlcLevelSpacingMv : 80;
        QlcLevelSpacingMv = analysis.QlcLevelSpacingMv > 0 ? analysis.QlcLevelSpacingMv : 40;
        MlcLevelSpacingCodes = string.IsNullOrWhiteSpace(analysis.MlcLevelSpacingCodes)
            ? RepeatSpacing(MlcLevelSpacingMv, 2)
            : analysis.MlcLevelSpacingCodes;
        TlcLevelSpacingCodes = string.IsNullOrWhiteSpace(analysis.TlcLevelSpacingCodes)
            ? RepeatSpacing(TlcLevelSpacingMv, 6)
            : analysis.TlcLevelSpacingCodes;
        QlcLevelSpacingCodes = string.IsNullOrWhiteSpace(analysis.QlcLevelSpacingCodes)
            ? RepeatSpacing(QlcLevelSpacingMv, 14)
            : analysis.QlcLevelSpacingCodes;
        bool hasSpacingLists = !string.IsNullOrWhiteSpace(analysis.MlcLevelSpacingCodes) ||
                               !string.IsNullOrWhiteSpace(analysis.TlcLevelSpacingCodes) ||
                               !string.IsNullOrWhiteSpace(analysis.QlcLevelSpacingCodes);
        if (analysis.LevelSpacingMv > 0 && !hasSpacingLists)
        {
            ApplyLegacyLevelSpacing(analysis.LevelSpacingMv);
        }
        GrayCodeOrder = string.IsNullOrWhiteSpace(analysis.GrayCodeOrder)
            ? "U-M-L"
            : analysis.GrayCodeOrder;
        BitOrder = string.IsNullOrWhiteSpace(analysis.BitOrder)
            ? "MSB"
            : analysis.BitOrder;
        SlcWlEncoding = analysis.SlcWlEncoding;
        MlcWlEncoding = analysis.MlcWlEncoding;
        TlcWlEncoding = analysis.TlcWlEncoding;
        QlcWlEncoding = analysis.QlcWlEncoding;
        TransitionDetectionMode = Enum.IsDefined(analysis.TransitionDetectionMode)
            ? analysis.TransitionDetectionMode
            : TransitionDetectionMode.SlidingWindow;
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

    public void SetChipDatabase(IEnumerable<ChipInfo> chips)
    {
        AllChips.Clear();
        foreach (var chip in chips)
            AllChips.Add(chip);

        RefreshManufacturers();
        RestoreChipSelection();
    }

    public void SelectManufacturer(string? manufacturer)
    {
        var normalizedManufacturer = FindManufacturerDisplayName(manufacturer);
        if (string.IsNullOrWhiteSpace(normalizedManufacturer))
            return;

        SelectedManufacturer = normalizedManufacturer;
        RefreshChipModelsForManufacturer(normalizedManufacturer);
        SelectedChip = AvailableChips.FirstOrDefault();
        ApplySelectedChipDefaults();
    }

    public void SelectChip(ChipInfo? chip)
    {
        SelectedChip = chip;
        ApplySelectedChipDefaults();
    }

    public void RestoreChipSelection()
    {
        RefreshManufacturers();
        var selectedChip = FindSavedChip();
        var manufacturer = FindManufacturerDisplayName(selectedChip?.Manufacturer ?? SelectedManufacturer);
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            manufacturer = AvailableManufacturers.FirstOrDefault() ?? string.Empty;
        }

        SelectedManufacturer = manufacturer;
        RefreshChipModelsForManufacturer(manufacturer);
        SelectedChip = selectedChip is not null && AvailableChips.Contains(selectedChip)
            ? selectedChip
            : AvailableChips.FirstOrDefault();
        ApplySelectedChipDefaults();
    }

    private double GetCurrentLevelSpacing()
    {
        return SelectedChip?.Type switch
        {
            XlcType.MLC => MlcLevelSpacingMv,
            XlcType.TLC => TlcLevelSpacingMv,
            XlcType.QLC => QlcLevelSpacingMv,
            _ => 0
        };
    }

    private void ApplyLegacyLevelSpacing(double spacing)
    {
        switch (SelectedChip?.Type)
        {
            case XlcType.MLC:
                MlcLevelSpacingMv = spacing;
                MlcLevelSpacingCodes = RepeatSpacing(spacing, 2);
                break;
            case XlcType.TLC:
                TlcLevelSpacingMv = spacing;
                TlcLevelSpacingCodes = RepeatSpacing(spacing, 6);
                break;
            case XlcType.QLC:
                QlcLevelSpacingMv = spacing;
                QlcLevelSpacingCodes = RepeatSpacing(spacing, 14);
                break;
        }
    }

    private static string RepeatSpacing(double spacing, int count) =>
        string.Join(",", Enumerable.Repeat(spacing.ToString("0.###"), count));

    private void RefreshManufacturers()
    {
        AvailableManufacturers.Clear();
        foreach (var manufacturer in AllChips
                     .Select(c => NormalizeManufacturer(c.Manufacturer))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            AvailableManufacturers.Add(manufacturer);
        }
    }

    private string FindManufacturerDisplayName(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
            return string.Empty;

        var normalizedManufacturer = NormalizeManufacturer(manufacturer);
        return AvailableManufacturers.FirstOrDefault(m =>
                   string.Equals(m, normalizedManufacturer, StringComparison.OrdinalIgnoreCase))
               ?? string.Empty;
    }

    private void RefreshChipModelsForManufacturer(string? manufacturer)
    {
        AvailableChips.Clear();
        var normalizedManufacturer = NormalizeManufacturer(manufacturer);

        foreach (var chip in AllChips
                     .Where(c => string.Equals(
                         NormalizeManufacturer(c.Manufacturer),
                         normalizedManufacturer,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => c.DieName, StringComparer.OrdinalIgnoreCase))
        {
            AvailableChips.Add(chip);
        }
    }

    private ChipInfo? FindSavedChip()
    {
        if (!string.IsNullOrWhiteSpace(SelectedManufacturer) &&
            !string.IsNullOrWhiteSpace(SelectedDieName))
        {
            var exactMatch = AllChips.FirstOrDefault(c =>
                string.Equals(c.Manufacturer, SelectedManufacturer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.DieName, SelectedDieName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
                return exactMatch;
        }

        if (!string.IsNullOrWhiteSpace(SelectedDieName))
        {
            return AllChips.FirstOrDefault(c =>
                string.Equals(c.DieName, SelectedDieName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void ApplySelectedChipDefaults()
    {
        if (SelectedChip is null)
            return;

        SelectedManufacturer = NormalizeManufacturer(SelectedChip.Manufacturer);
        SelectedDieName = SelectedChip.DieName;
        PageDataBytes = SelectedChip.PageDataBytes;
        PageRedundantBytes = SelectedChip.PageRedundantBytes;
        CodewordsPerPage = SelectedChip.FrameCount;

        if (SelectedChip.WlPerBlock is > 0 &&
            (WlCount <= 0 || WlCount > SelectedChip.WlPerBlock.Value))
        {
            WlCount = SelectedChip.WlPerBlock.Value;
        }

        string text = string.Join(",", SelectedChip.WlEncoding);
        switch (SelectedChip.Type)
        {
            case XlcType.SLC:
                SlcWlEncoding = text;
                break;
            case XlcType.MLC:
                MlcWlEncoding = text;
                break;
            case XlcType.TLC:
                TlcWlEncoding = text;
                break;
            case XlcType.QLC:
                QlcWlEncoding = text;
                break;
        }
    }

    private static string NormalizeManufacturer(string? manufacturer) =>
        string.IsNullOrWhiteSpace(manufacturer)
            ? ChipInfo.UnknownManufacturer
            : manufacturer.Trim();
}
