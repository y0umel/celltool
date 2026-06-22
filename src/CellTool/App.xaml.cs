using System.Windows;
using CellTool.Services;
using CellTool.ViewModels;
using Wpf.Ui.Appearance;

namespace CellTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        SystemThemeWatcher.Watch(window);
        ThemeService.Apply(AppServices.State.IsDarkTheme);
        window.Show();
    }
}
