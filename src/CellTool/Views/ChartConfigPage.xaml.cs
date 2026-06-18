using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class ChartConfigPage : Page
{
    private readonly ChartConfigViewModel _vm;

    public ChartConfigPage()
    {
        InitializeComponent();
        _vm = new ChartConfigViewModel();
        DataContext = _vm;
        _vm.ChartUpdated += plot => ChartPlot.Plot = plot;
    }
}
