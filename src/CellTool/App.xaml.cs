using System.Windows;
using Wpf.Ui.Appearance;

namespace CellTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SystemThemeWatcher.Watch(this);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }
}
