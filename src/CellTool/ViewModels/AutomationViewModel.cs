using CommunityToolkit.Mvvm.ComponentModel;

namespace CellTool.ViewModels;

public partial class AutomationViewModel : ObservableObject
{
    public AutomationViewModel()
        : this(AppServices.State)
    {
    }

    public AutomationViewModel(AppState state)
    {
    }

    [ObservableProperty]
    private bool _fileMonitorEnabled;

    [ObservableProperty]
    private bool _apiEnabled;

    [ObservableProperty]
    private int _apiPort = 5180;

    [ObservableProperty]
    private string _status = "自动化功能为接口预留：HTTP API、文件监控暂未实现。";

    partial void OnFileMonitorEnabledChanged(bool value)
    {
        if (value)
        {
            FileMonitorEnabled = false;
            Status = "文件监控为预留接口，当前版本不启动后台监控。";
        }
    }

    partial void OnApiEnabledChanged(bool value)
    {
        if (value)
        {
            ApiEnabled = false;
            Status = "HTTP API 为预留接口，当前版本不启动 Kestrel 服务。";
        }
    }
}
