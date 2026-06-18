using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CellTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public ObservableCollection<string> LogEntries { get; } = new();

    public void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogEntries.Add($"[{timestamp}] {message}");
    }
}
