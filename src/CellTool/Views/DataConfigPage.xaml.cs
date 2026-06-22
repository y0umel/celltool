using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class DataConfigPage : Page
{
    public DataConfigPage()
    {
        InitializeComponent();
        DataContext = new DataConfigViewModel(AppServices.State, AppServices.Dialogs);
        Loaded += (_, _) =>
        {
            if (DataContext is DataConfigViewModel viewModel)
                viewModel.RefreshDisplayState();
        };
    }
}
