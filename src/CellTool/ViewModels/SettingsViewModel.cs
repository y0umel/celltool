using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace CellTool.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly ConfigPersistence persistence = new();

    public SettingsViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public SettingsViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        _isDarkTheme = state.IsDarkTheme;
    }

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _aboutText = "CellTool - NAND Vt 分析器 v1.0";

    [RelayCommand]
    private void ExportConfig()
    {
        var file = dialogs.SaveFile("导出配置", "JSON files (*.json)|*.json|All files (*.*)|*.*", "celltool-config.json");
        if (file is null) return;

        try
        {
            persistence.Save(file, state.CreateConfiguration());
            dialogs.ShowInfo("配置已导出。");
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"导出配置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportConfig()
    {
        var file = dialogs.OpenFile("导入配置", "JSON files (*.json)|*.json|All files (*.*)|*.*");
        if (file is null) return;

        try
        {
            var config = persistence.LoadAppConfiguration(file);
            state.ApplyConfiguration(config);
            IsDarkTheme = state.IsDarkTheme;
            dialogs.ShowInfo("配置已导入。芯片数据库和 GroupModel 如需使用，请在数据配置页重新加载确认。");
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"导入配置失败: {ex.Message}");
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.Apply(value);
    }
}
