using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class DataConfigPage : Page
{
    public DataConfigPage()
    {
        InitializeComponent();
        DataContext = new DataConfigViewModel();
    }
}
