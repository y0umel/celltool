using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace CellTool.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _aboutText = "CellTool - NAND Vt Analyzer v1.0";

    [RelayCommand]
    private void ExportConfig() { /* TODO: ConfigPersistence */ }

    [RelayCommand]
    private void ImportConfig() { /* TODO: ConfigPersistence */ }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplicationThemeManager.Apply(
            value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
