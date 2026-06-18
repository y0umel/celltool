using CommunityToolkit.Mvvm.ComponentModel;

namespace CellTool.ViewModels;

public partial class AutomationViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _fileMonitorEnabled;

    [ObservableProperty]
    private bool _apiEnabled;

    [ObservableProperty]
    private int _apiPort = 5180;

    [ObservableProperty]
    private string _status = "Automation features are reserved for future implementation.";
}
