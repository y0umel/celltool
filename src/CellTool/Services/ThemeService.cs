using System.Windows;
using System.Windows.Media;
using CellTool.ViewModels;
using Wpf.Ui.Appearance;

namespace CellTool.Services;

public static class ThemeService
{
    public static void Apply(bool isDarkTheme)
    {
        AppServices.State.IsDarkTheme = isDarkTheme;
        var theme = isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme);
        ApplyAccent(theme);
        UpdateApplicationBrushes(isDarkTheme);

        if (Application.Current?.MainWindow is not null)
            RefreshThemeBrushes(Application.Current.MainWindow);
    }

    public static void RefreshCurrentWindow()
    {
        if (Application.Current?.MainWindow is not null)
            RefreshThemeBrushes(Application.Current.MainWindow);
    }

    private static void UpdateApplicationBrushes(bool isDarkTheme)
    {
        if (Application.Current is null)
            return;

        SetBrush("CellTool.BackgroundBrush", isDarkTheme ? "#202020" : "#FAFAFA");
        SetBrush("CellTool.SurfaceBrush", isDarkTheme ? "#2B2B2B" : "#FFFFFF");
        SetBrush("CellTool.BorderBrush", isDarkTheme ? "#4A4A4A" : "#DADADA");
        SetBrush("CellTool.TextPrimaryBrush", isDarkTheme ? "#F3F3F3" : "#1A1A1A");
        SetBrush("CellTool.TextSecondaryBrush", isDarkTheme ? "#C7C7C7" : "#5A5A5A");
        SetBrush("CellTool.AccentBrush", isDarkTheme ? "#2FBFA3" : "#138A74");
    }

    private static void ApplyAccent(ApplicationTheme theme)
    {
        var accent = (Color)ColorConverter.ConvertFromString("#2FBFA3");
        ApplicationAccentColorManager.Apply(accent, theme, false, false);
    }

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private static void RefreshThemeBrushes(Window window)
    {
        if (Application.Current.TryFindResource("CellTool.BackgroundBrush") is Brush background)
            window.Background = background;

        if (Application.Current.TryFindResource("CellTool.TextPrimaryBrush") is not Brush foreground)
            return;

        foreach (var control in FindVisualChildren<System.Windows.Controls.Control>(window))
            control.Foreground = foreground;

        foreach (var textBlock in FindVisualChildren<System.Windows.Controls.TextBlock>(window))
            textBlock.Foreground = foreground;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
