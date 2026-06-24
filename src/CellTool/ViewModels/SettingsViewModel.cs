using System.Collections.ObjectModel;
using CellTool.Models;
using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CellTool.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly ConfigPersistence persistence = new();
    private readonly ConfigProfileService profileService = new();
    private readonly ChipDatabaseService chipDatabaseService = new();
    private bool loadingProfile;
    private ConfigProfileInfo? currentProfile;

    public SettingsViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public SettingsViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        _isDarkTheme = state.IsDarkTheme;
        RefreshProfiles();
        RefreshChipDatabaseStatus();
    }

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _aboutText = "CellTool - NAND Vt 分析器 v1.0";

    [ObservableProperty]
    private ConfigProfileInfo? _selectedProfile;

    [ObservableProperty]
    private string _chipDatabaseStatus = string.Empty;

    public ObservableCollection<ConfigProfileInfo> Profiles { get; } = new();

    partial void OnSelectedProfileChanged(ConfigProfileInfo? value)
    {
        if (loadingProfile || value is null)
            return;

        try
        {
            if (currentProfile is not null)
                profileService.Save(currentProfile, state.CreateConfiguration());

            var config = profileService.Load(value);
            state.ApplyConfiguration(config);
            state.RestoreChipSelection();
            IsDarkTheme = state.IsDarkTheme;
            currentProfile = value;
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"切换配置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        var name = dialogs.PromptText("新建配置", "配置名称:", "新配置");
        if (name is null) return;

        try
        {
            var profile = profileService.Create(name, state.CreateConfiguration());
            RefreshProfiles(profile.Name);
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"新建配置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile is null)
            return;

        var name = dialogs.PromptText("重命名配置", "配置名称:", SelectedProfile.Name);
        if (name is null) return;

        try
        {
            var profile = profileService.Rename(SelectedProfile, name);
            RefreshProfiles(profile.Name);
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"重命名配置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is null)
            return;

        try
        {
            profileService.Save(SelectedProfile, state.CreateConfiguration());
            dialogs.ShowInfo("配置已保存。");
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"保存配置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void UpdateChipDatabase()
    {
        var file = dialogs.OpenFile(
            "更新厂家表",
            "Chip database (*.csv;*.txt;*.xlsx;*.xls)|*.csv;*.txt;*.xlsx;*.xls|CSV files (*.csv;*.txt)|*.csv;*.txt|Excel files (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*");
        if (file is null) return;

        try
        {
            var chips = chipDatabaseService.ImportFromFile(file);
            state.SetChipDatabase(chips);
            RefreshChipDatabaseStatus();
            dialogs.ShowInfo($"厂家数据库已更新，共 {chips.Count} 个芯片。");
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"更新厂家数据库失败: {ex.Message}");
        }
    }

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
            state.RestoreChipSelection();
            IsDarkTheme = state.IsDarkTheme;
            dialogs.ShowInfo("配置已导入。");
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"导入配置失败: {ex.Message}");
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        state.IsDarkTheme = value;
        ThemeService.Apply(value);
    }

    private void RefreshProfiles(string? selectedName = null)
    {
        loadingProfile = true;
        try
        {
            var profiles = profileService.EnsureProfiles(state.CreateConfiguration());
            Profiles.Clear();
            foreach (var profile in profiles)
                Profiles.Add(profile);

            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault();
            currentProfile = SelectedProfile;
        }
        finally
        {
            loadingProfile = false;
        }
    }

    private void RefreshChipDatabaseStatus()
    {
        ChipDatabaseStatus = $"当前芯片数: {state.AllChips.Count}";
    }
}
