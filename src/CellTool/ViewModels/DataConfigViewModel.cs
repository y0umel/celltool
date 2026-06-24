using System.ComponentModel;
using CellTool.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CellTool.ViewModels;

public partial class DataConfigViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly IUserDialogService dialogs;
    private readonly GroupModelParser groupModelParser = new();

    public DataConfigViewModel()
        : this(AppServices.State, AppServices.Dialogs)
    {
    }

    public DataConfigViewModel(AppState state, IUserDialogService dialogs)
    {
        this.state = state;
        this.dialogs = dialogs;
        this.state.PropertyChanged += OnStatePropertyChanged;
        RefreshDisplayState();
    }

    public string GroupModelFilePath
    {
        get => state.GroupModelFilePath;
        set
        {
            if (state.GroupModelFilePath == value) return;
            state.GroupModelFilePath = value;
            OnPropertyChanged();
        }
    }

    public string ReferenceFilePath
    {
        get => state.ReferenceFilePath;
        set
        {
            if (state.ReferenceFilePath == value) return;
            state.ReferenceFilePath = value;
            OnPropertyChanged();
        }
    }

    public int? PageDataBytes
    {
        get => state.PageDataBytes;
        set
        {
            if (state.PageDataBytes == value) return;
            state.PageDataBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodewordBytes));
        }
    }

    public int? PageRedundantBytes
    {
        get => state.PageRedundantBytes;
        set
        {
            if (state.PageRedundantBytes == value) return;
            state.PageRedundantBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodewordBytes));
        }
    }

    public int? CodewordsPerPage
    {
        get => state.CodewordsPerPage;
        set
        {
            if (state.CodewordsPerPage == value) return;
            state.CodewordsPerPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodewordBytes));
        }
    }

    public string GrayCodeOrder
    {
        get => state.GrayCodeOrder;
        set
        {
            if (state.GrayCodeOrder == value) return;
            state.GrayCodeOrder = value;
            OnPropertyChanged();
        }
    }

    public string SlcWlEncoding
    {
        get => state.SlcWlEncoding;
        set
        {
            if (state.SlcWlEncoding == value) return;
            state.SlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string MlcWlEncoding
    {
        get => state.MlcWlEncoding;
        set
        {
            if (state.MlcWlEncoding == value) return;
            state.MlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string TlcWlEncoding
    {
        get => state.TlcWlEncoding;
        set
        {
            if (state.TlcWlEncoding == value) return;
            state.TlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public string QlcWlEncoding
    {
        get => state.QlcWlEncoding;
        set
        {
            if (state.QlcWlEncoding == value) return;
            state.QlcWlEncoding = value;
            OnPropertyChanged();
        }
    }

    public double MlcLevelSpacingMv
    {
        get => state.MlcLevelSpacingMv;
        set
        {
            if (Math.Abs(state.MlcLevelSpacingMv - value) < 0.001) return;
            state.MlcLevelSpacingMv = value;
            OnPropertyChanged();
        }
    }

    public double TlcLevelSpacingMv
    {
        get => state.TlcLevelSpacingMv;
        set
        {
            if (Math.Abs(state.TlcLevelSpacingMv - value) < 0.001) return;
            state.TlcLevelSpacingMv = value;
            OnPropertyChanged();
        }
    }

    public double QlcLevelSpacingMv
    {
        get => state.QlcLevelSpacingMv;
        set
        {
            if (Math.Abs(state.QlcLevelSpacingMv - value) < 0.001) return;
            state.QlcLevelSpacingMv = value;
            OnPropertyChanged();
        }
    }

    public int? CodewordBytes
    {
        get
        {
            if (state.PageDataBytes is not > 0 ||
                state.PageRedundantBytes is null or < 0 ||
                state.CodewordsPerPage is not > 0)
            {
                return null;
            }

            int pageTotalBytes = state.PageDataBytes.Value + state.PageRedundantBytes.Value;
            return pageTotalBytes / state.CodewordsPerPage.Value;
        }
    }
    public IReadOnlyList<string> GrayCodeOrders { get; } = new[] { "U-M-L", "U-L-M", "M-U-L", "M-L-U", "L-U-M", "L-M-U" };

    [ObservableProperty]
    private string _groupModelStatus = "未加载组模型";

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.GroupModelFilePath):
                OnPropertyChanged(nameof(GroupModelFilePath));
                break;
            case nameof(AppState.ReferenceFilePath):
                OnPropertyChanged(nameof(ReferenceFilePath));
                break;
            case nameof(AppState.PageDataBytes):
                OnPropertyChanged(nameof(PageDataBytes));
                OnPropertyChanged(nameof(CodewordBytes));
                break;
            case nameof(AppState.PageRedundantBytes):
                OnPropertyChanged(nameof(PageRedundantBytes));
                OnPropertyChanged(nameof(CodewordBytes));
                break;
            case nameof(AppState.CodewordsPerPage):
                OnPropertyChanged(nameof(CodewordsPerPage));
                OnPropertyChanged(nameof(CodewordBytes));
                break;
            case nameof(AppState.GrayCodeOrder):
                OnPropertyChanged(nameof(GrayCodeOrder));
                break;
            case nameof(AppState.SlcWlEncoding):
                OnPropertyChanged(nameof(SlcWlEncoding));
                break;
            case nameof(AppState.MlcWlEncoding):
                OnPropertyChanged(nameof(MlcWlEncoding));
                break;
            case nameof(AppState.TlcWlEncoding):
                OnPropertyChanged(nameof(TlcWlEncoding));
                break;
            case nameof(AppState.QlcWlEncoding):
                OnPropertyChanged(nameof(QlcWlEncoding));
                break;
            case nameof(AppState.MlcLevelSpacingMv):
                OnPropertyChanged(nameof(MlcLevelSpacingMv));
                break;
            case nameof(AppState.TlcLevelSpacingMv):
                OnPropertyChanged(nameof(TlcLevelSpacingMv));
                break;
            case nameof(AppState.QlcLevelSpacingMv):
                OnPropertyChanged(nameof(QlcLevelSpacingMv));
                break;
            case nameof(AppState.SelectedChip):
                OnPropertyChanged(nameof(CodewordBytes));
                break;
            case nameof(AppState.LoadedGroupModel):
                RefreshDisplayState();
                break;
        }
    }

    [RelayCommand]
    private void LoadGroupModel()
    {
        var file = dialogs.OpenFile("Select GroupModel File", "Text files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*");
        if (file is null) return;

        if (state.SelectedChip is null)
        {
            dialogs.ShowWarning("请先在首页选择芯片。");
            return;
        }

        try
        {
            GroupModelFilePath = file;
            state.LoadedGroupModel = state.SelectedChip.WlPerBlock is > 0
                ? groupModelParser.LoadFromFile(file, state.SelectedChip.WlPerBlock.Value)
                : groupModelParser.LoadFromFile(file, state.WlCount);
            RefreshDisplayState();
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"Failed to load GroupModel: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BrowseReference()
    {
        var file = dialogs.OpenFile("Select Reference File for Codeword Comparison", "Binary files (*.bin)|*.bin|All files (*.*)|*.*");
        if (file is not null)
            ReferenceFilePath = file;
    }

    public void RefreshDisplayState()
    {
        GroupModelStatus = state.LoadedGroupModel is null
            ? "未加载组模型"
            : $"Loaded {state.LoadedGroupModel.Entries.Count} WLs.";
    }
}
