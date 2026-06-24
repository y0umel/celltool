using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class ChartConfigPage : Page
{
    private readonly ChartConfigViewModel _vm;

    public ChartConfigPage()
    {
        InitializeComponent();
        _vm = new ChartConfigViewModel(AppServices.State, AppServices.Dialogs);
        DataContext = _vm;
        _vm.ChartUpdated += (linear, log) =>
        {
            LinearChartPlot.Reset(linear);
            LinearChartPlot.Refresh();
            LogChartPlot.Reset(log);
            LogChartPlot.Refresh();
        };
        Loaded += (_, _) => _vm.RefreshPreview();
    }
}
