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

    public string? PromptText(string title, string label, string defaultValue = "")
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            MinWidth = 280,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label });
        panel.Children.Add(input);

        var window = new Window
        {
            Title = title,
            Content = panel,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new System.Windows.Controls.Button { Content = "确定", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 72, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        input.SelectAll();
        input.Focus();
        return window.ShowDialog() == true ? input.Text : null;
    }

    public void ShowWarning(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "CellTool") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
