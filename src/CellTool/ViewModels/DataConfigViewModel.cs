using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CellTool.Models;
using CellTool.Services;

namespace CellTool.ViewModels;

public partial class DataConfigViewModel : ObservableObject
{
    private readonly ExcelParser _excelParser = new();
    private readonly GroupModelParser _groupModelParser = new();
    private List<ChipInfo> _allChips = new();

    [ObservableProperty]
    private string _excelFilePath = string.Empty;

    [ObservableProperty]
    private string _groupModelFilePath = string.Empty;

    [ObservableProperty]
    private string _referenceFilePath = string.Empty;

    [ObservableProperty]
    private string _grayCodeMsb = "U";

    [ObservableProperty]
    private string _grayCodeLsb = "L";

    [ObservableProperty]
    private ChipInfo? _selectedChip;

    [ObservableProperty]
    private int _codewordBytes;

    [ObservableProperty]
    private string _chipStatus = "No chip selected";

    [ObservableProperty]
    private string _groupModelStatus = "No GroupModel loaded";

    public ObservableCollection<ChipInfo> AvailableChips { get; } = new();
    public GroupModel? LoadedGroupModel { get; private set; }

    [RelayCommand]
    private void LoadExcel()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel files (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*",
            Title = "Select Chip Database Excel"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            ExcelFilePath = dlg.FileName;
            _allChips = _excelParser.LoadDatabase(dlg.FileName);
            AvailableChips.Clear();
            foreach (var chip in _allChips)
                AvailableChips.Add(chip);

            ChipStatus = $"Loaded {_allChips.Count} chips.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load Excel: {ex.Message}",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void LoadGroupModel()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*",
            Title = "Select GroupModel File"
        };

        if (dlg.ShowDialog() != true) return;

        if (SelectedChip is null)
        {
            MessageBox.Show("Please select a chip first.",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            GroupModelFilePath = dlg.FileName;
            LoadedGroupModel = _groupModelParser.LoadFromFile(dlg.FileName, SelectedChip.WlPerBlock);
            GroupModelStatus = $"Loaded {LoadedGroupModel.Entries.Count} WLs.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load GroupModel: {ex.Message}",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void BrowseReference()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            Title = "Select Reference File for Codeword Comparison"
        };

        if (dlg.ShowDialog() == true)
            ReferenceFilePath = dlg.FileName;
    }

    partial void OnSelectedChipChanged(ChipInfo? value)
    {
        if (value is not null)
        {
            CodewordBytes = value.PageTotalBytes / value.FrameCount;
            ChipStatus = $"{value.DieName} - {value.Type}, {value.PageTotalBytes} B/page, {value.CodewordBytes} B/CW";
        }
    }
}
