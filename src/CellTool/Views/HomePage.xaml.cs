using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = new HomeViewModel();
    }
}
