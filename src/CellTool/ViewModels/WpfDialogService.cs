using System.Windows;
using Microsoft.Win32;

namespace CellTool.ViewModels;

public class WpfDialogService : IUserDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = filter
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? SelectFolder(string title)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title
        };

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public void ShowWarning(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
