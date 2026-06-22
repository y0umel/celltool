namespace CellTool.ViewModels;

public static class AppServices
{
    public static AppState State { get; } = new();
    public static IUserDialogService Dialogs { get; } = new WpfDialogService();
}
