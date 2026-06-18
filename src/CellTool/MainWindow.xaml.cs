using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CellTool.Views;
using Wpf.Ui.Controls;

namespace CellTool;

public partial class MainWindow : UiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RootNavigation.Navigate("Home");

        RootNavigation.Navigated += OnNavigated;
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is not null)
            return;

        Page page = args.PageTag?.ToString() switch
        {
            "Home" => new HomePage(),
            "DataConfig" => new DataConfigPage(),
            "ChartConfig" => new ChartConfigPage(),
            "Automation" => new AutomationPage(),
            "Settings" => new SettingsPage(),
            _ => new HomePage()
        };

        RootNavigation.NavigateWithHierarchy(page);
    }
}
