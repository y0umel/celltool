using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class AutomationPage : Page
{
    public AutomationPage()
    {
        InitializeComponent();
        DataContext = new AutomationViewModel(AppServices.State);
    }
}
