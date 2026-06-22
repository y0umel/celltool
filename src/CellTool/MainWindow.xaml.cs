using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CellTool.Services;
using CellTool.Views;


namespace CellTool;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ContentFrame.Navigated += (_, _) =>
            Dispatcher.BeginInvoke(ThemeService.RefreshCurrentWindow, DispatcherPriority.Loaded);
        Loaded += (_, _) => Navigate(new HomePage());
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) =>
        Navigate(new HomePage());

    private void DataConfigNav_Click(object sender, RoutedEventArgs e) =>
        Navigate(new DataConfigPage());

    private void ChartConfigNav_Click(object sender, RoutedEventArgs e) =>
        Navigate(new ChartConfigPage());

    private void AutomationNav_Click(object sender, RoutedEventArgs e) =>
        Navigate(new AutomationPage());

    private void SettingsNav_Click(object sender, RoutedEventArgs e) =>
        Navigate(new SettingsPage());

    private void Navigate(Page page)
    {
        ContentFrame.Navigate(page);
    }
}
