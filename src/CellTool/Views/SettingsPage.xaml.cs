using System.Windows.Controls;
using CellTool.ViewModels;

namespace CellTool.Views;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(AppServices.State, AppServices.Dialogs);
    }
}
