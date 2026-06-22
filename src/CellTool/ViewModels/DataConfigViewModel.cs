using System.Collections.ObjectModel;
using System.ComponentModel;
using CellTool.Models;
using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CellTool.ViewModels;

public partial class DataConfigViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly ExcelParser excelParser = new();
    private readonly GroupModelParser groupModelParser = new();
    private bool suppressSelectionUpdates;

    public DataConfigViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public DataConfigViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        this.state.PropertyChanged += OnStatePropertyChanged;
        RefreshDisplayState();
    }

    public string ExcelFilePath
    {
        get => state.ExcelFilePath;
        set
        {
            if (state.ExcelFilePath == value) return;
            state.ExcelFilePath = value;
            OnPropertyChanged();
        }
    }

    public string GroupModelFilePath
    {
        get => state.GroupModelFilePath;
        set
        {
            if (state.GroupModelFilePath == value) return;
            state.GroupModelFilePath = value;
            OnPropertyChanged();
        }
    }

    public string ReferenceFilePath
    {
        get => state.ReferenceFilePath;
        set
        {
            if (state.ReferenceFilePath == value) return;
            state.ReferenceFilePath = value;
            OnPropertyChanged();
        }
    }

    public string GrayCodeOrder
    {
        get => state.GrayCodeOrder;
        set
        {
            if (state.GrayCodeOrder == value) return;
            state.GrayCodeOrder = value;
            OnPropertyChanged();
        }
    }

    public string SlcWlEncoding
    {
        get => state.SlcWlEncoding;
        set
        {
            if (state.SlcWlEncoding == value) return;
            state.SlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string MlcWlEncoding
    {
        get => state.MlcWlEncoding;
        set
        {
            if (state.MlcWlEncoding == value) return;
            state.MlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string TlcWlEncoding
    {
        get => state.TlcWlEncoding;
        set
        {
            if (state.TlcWlEncoding == value) return;
            state.TlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string QlcWlEncoding
    {
        get => state.QlcWlEncoding;
        set
        {
            if (state.QlcWlEncoding == value) return;
            state.QlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public ChipInfo? SelectedChip
    {
        get => state.SelectedChip;
        set
        {
            if (state.SelectedChip == value) return;
            state.SelectedChip = value;
            OnPropertyChanged();
            OnSelectedChipChanged(value);
        }
    }

    public string SelectedManufacturer
    {
        get => state.SelectedManufacturer;
        set
        {
            if (state.SelectedManufacturer == value) return;
            state.SelectedManufacturer = value;
            OnPropertyChanged();
            OnSelectedManufacturerChanged(value);
        }
    }

    [ObservableProperty]
    private int _codewordBytes;

    [ObservableProperty]
    private string _chipStatus = "未选择芯片";

    [ObservableProperty]
    private string _groupModelStatus = "未加载组模型";

    public ObservableCollection<string> AvailableManufacturers => state.AvailableManufacturers;
    public ObservableCollection<ChipInfo> AvailableChips => state.AvailableChips;
    public IReadOnlyList<string> GrayCodeOrders { get; } = new[] { "U-M-L", "U-L-M", "M-U-L", "M-L-U", "L-U-M", "L-M-U" };

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.ExcelFilePath):
                OnPropertyChanged(nameof(ExcelFilePath));
                break;
            case nameof(AppState.GroupModelFilePath):
                OnPropertyChanged(nameof(GroupModelFilePath));
                break;
            case nameof(AppState.ReferenceFilePath):
                OnPropertyChanged(nameof(ReferenceFilePath));
                break;
            case nameof(AppState.GrayCodeOrder):
                OnPropertyChanged(nameof(GrayCodeOrder));
                break;
            case nameof(AppState.SlcWlEncoding):
                OnPropertyChanged(nameof(SlcWlEncoding));
                break;
            case nameof(AppState.MlcWlEncoding):
                OnPropertyChanged(nameof(MlcWlEncoding));
                break;
            case nameof(AppState.TlcWlEncoding):
                OnPropertyChanged(nameof(TlcWlEncoding));
                break;
            case nameof(AppState.QlcWlEncoding):
                OnPropertyChanged(nameof(QlcWlEncoding));
                break;
            case nameof(AppState.SelectedChip):
                OnPropertyChanged(nameof(SelectedChip));
                break;
            case nameof(AppState.SelectedManufacturer):
                OnPropertyChanged(nameof(SelectedManufacturer));
                if (!suppressSelectionUpdates &&
                    !string.Equals(state.SelectedChip?.Manufacturer, state.SelectedManufacturer, StringComparison.OrdinalIgnoreCase))
                {
                    OnSelectedManufacturerChanged(state.SelectedManufacturer);
                }
                break;
            case nameof(AppState.SelectedDieName):
                if (!suppressSelectionUpdates &&
                    !string.Equals(state.SelectedChip?.DieName, state.SelectedDieName, StringComparison.OrdinalIgnoreCase))
                {
                    RestoreChipSelection();
                }
                break;
        }
    }

    [RelayCommand]
    private void LoadExcel()
    {
        var file = dialogs.OpenFile(
            "Select Chip Database",
            "Chip database (*.csv;*.txt;*.xlsx;*.xls)|*.csv;*.txt;*.xlsx;*.xls|CSV files (*.csv;*.txt)|*.csv;*.txt|Excel files (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*");
        if (file is null) return;

        try
        {
            LoadExcelFromPath(file);
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"Failed to load Excel: {ex.Message}");
        }
    }

    [RelayCommand]
    private void LoadGroupModel()
    {
        var file = dialogs.OpenFile("Select GroupModel File", "Text files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*");
        if (file is null) return;

        if (SelectedChip is null)
        {
            dialogs.ShowWarning("请先选择芯片。");
            return;
        }

        try
        {
            LoadGroupModelFromPath(file);
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"Failed to load GroupModel: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BrowseReference()
    {
        var file = dialogs.OpenFile("Select Reference File for Codeword Comparison", "Binary files (*.bin)|*.bin|All files (*.*)|*.*");
        if (file is not null)
            ReferenceFilePath = file;
    }

    public void LoadExcelFromPath(string filePath)
    {
        ExcelFilePath = filePath;
        state.AllChips.Clear();
        foreach (var chip in excelParser.LoadDatabase(filePath))
            state.AllChips.Add(chip);

        RefreshManufacturers();

        ChipStatus = $"Loaded {state.AllChips.Count} chips.";

        RestoreChipSelection();
    }

    public void LoadGroupModelFromPath(string filePath)
    {
        if (SelectedChip is null)
            throw new InvalidOperationException("Please select a chip first.");

        GroupModelFilePath = filePath;
        state.LoadedGroupModel = groupModelParser.LoadFromFile(
            filePath,
            SelectedChip.WlPerBlock);
        GroupModelStatus = $"Loaded {state.LoadedGroupModel.Entries.Count} WLs.";
    }

    public void RefreshDisplayState()
    {
        if (state.AllChips.Count > 0)
        {
            RefreshManufacturers();
            RestoreChipSelection();
        }
        else if (state.SelectedChip is not null)
        {
            OnSelectedChipChanged(state.SelectedChip);
        }
        else
        {
            CodewordBytes = 0;
            ChipStatus = "未选择芯片";
        }

        GroupModelStatus = state.LoadedGroupModel is null
            ? "未加载组模型"
            : $"Loaded {state.LoadedGroupModel.Entries.Count} WLs.";
    }

    private void OnSelectedChipChanged(ChipInfo? value)
    {
        if (value is not null)
        {
            CodewordBytes = value.CodewordBytes;
            bool chipChanged =
                !string.Equals(state.SelectedManufacturer, value.Manufacturer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.SelectedDieName, value.DieName, StringComparison.OrdinalIgnoreCase);
            state.SelectedManufacturer = value.Manufacturer;
            state.SelectedDieName = value.DieName;
            SetEncodingForChipType(value, chipChanged);
            ChipStatus = $"{value.Manufacturer} / {value.DieName} - {value.Type}, {value.PageTotalBytes} B/page, {value.CodewordBytes} B/CW";
            if (state.WlCount <= 0 || state.WlCount > value.WlPerBlock)
                state.WlCount = value.WlPerBlock;
        }
        else
        {
            CodewordBytes = 0;
            ChipStatus = "未选择芯片";
        }
    }

    private void OnSelectedManufacturerChanged(string? value)
    {
        if (suppressSelectionUpdates)
            return;

        RefreshChipModelsForManufacturer(value);
        SelectedChip = AvailableChips.FirstOrDefault();
    }

    private void RefreshManufacturers()
    {
        AvailableManufacturers.Clear();
        foreach (var manufacturer in state.AllChips
                     .Select(c => NormalizeManufacturer(c.Manufacturer))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            AvailableManufacturers.Add(manufacturer);
        }
    }

    private void RestoreChipSelection()
    {
        suppressSelectionUpdates = true;
        try
        {
            var selectedChip = FindSavedChip();
            var manufacturer = NormalizeManufacturer(selectedChip?.Manufacturer);
            if (string.IsNullOrWhiteSpace(manufacturer) ||
                !AvailableManufacturers.Contains(manufacturer))
            {
                manufacturer = AvailableManufacturers.FirstOrDefault() ?? string.Empty;
            }

            state.SelectedManufacturer = manufacturer;
            OnPropertyChanged(nameof(SelectedManufacturer));
            RefreshChipModelsForManufacturer(manufacturer);
            SelectedChip = selectedChip is not null && AvailableChips.Contains(selectedChip)
                ? selectedChip
                : AvailableChips.FirstOrDefault();
            OnSelectedChipChanged(SelectedChip);
        }
        finally
        {
            suppressSelectionUpdates = false;
        }
    }

    private ChipInfo? FindSavedChip()
    {
        if (!string.IsNullOrWhiteSpace(state.SelectedManufacturer) &&
            !string.IsNullOrWhiteSpace(state.SelectedDieName))
        {
            var exactMatch = state.AllChips.FirstOrDefault(c =>
                string.Equals(c.Manufacturer, state.SelectedManufacturer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.DieName, state.SelectedDieName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
                return exactMatch;
        }

        if (!string.IsNullOrWhiteSpace(state.SelectedDieName))
        {
            return state.AllChips.FirstOrDefault(c =>
                string.Equals(c.DieName, state.SelectedDieName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void RefreshChipModelsForManufacturer(string? manufacturer)
    {
        AvailableChips.Clear();
        var normalizedManufacturer = NormalizeManufacturer(manufacturer);

        foreach (var chip in state.AllChips
                     .Where(c => string.Equals(
                         NormalizeManufacturer(c.Manufacturer),
                         normalizedManufacturer,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => c.DieName, StringComparer.OrdinalIgnoreCase))
        {
            AvailableChips.Add(chip);
        }
    }

    private static string NormalizeManufacturer(string? manufacturer) =>
        string.IsNullOrWhiteSpace(manufacturer)
            ? ChipInfo.UnknownManufacturer
            : manufacturer.Trim();

    private void SetEncodingForChipType(ChipInfo chip, bool overwrite)
    {
        var text = string.Join(",", chip.WlEncoding);
        switch (chip.Type)
        {
            case XlcType.SLC:
                if (overwrite || string.IsNullOrWhiteSpace(SlcWlEncoding))
                    SlcWlEncoding = text;
                break;
            case XlcType.MLC:
                if (overwrite || string.IsNullOrWhiteSpace(MlcWlEncoding))
                    MlcWlEncoding = text;
                break;
            case XlcType.TLC:
                if (overwrite || string.IsNullOrWhiteSpace(TlcWlEncoding))
                    TlcWlEncoding = text;
                break;
            case XlcType.QLC:
                if (overwrite || string.IsNullOrWhiteSpace(QlcWlEncoding))
                    QlcWlEncoding = text;
                break;
        }
    }
}
