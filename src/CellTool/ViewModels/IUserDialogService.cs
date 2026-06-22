namespace CellTool.ViewModels;

public interface IUserDialogService
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string defaultFileName);
    string? SelectFolder(string title);
    void ShowWarning(string message, string title = "CellTool");
    void ShowError(string message, string title = "CellTool");
    void ShowInfo(string message, string title = "CellTool");
}
