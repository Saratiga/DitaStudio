using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace DitaStudio.Desktop;

/// <summary>
/// Главное окно. На этапе 1 миграции — только разметка и оформление; обработчики
/// переносятся вместе с ViewModel'ями (этапы 2–3), пока это заглушки.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ThemeMenuItem.IsChecked ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnNewDocument(object? sender, RoutedEventArgs e)
    {
    }

    private void OnFocusSearch(object? sender, RoutedEventArgs e)
    {
        BottomTabs.SelectedIndex = 1;
        SearchBox.Focus();
    }

    private void OnFocusPalette(object? sender, RoutedEventArgs e)
    {
        RightTabs.SelectedIndex = 1;
        PaletteFilter.Focus();
    }

    private void OnProjectFileMove(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapAddTopicref(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapAddTopichead(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapMoveUp(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapMoveDown(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapIndent(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapOutdent(object? sender, RoutedEventArgs e)
    {
    }

    private void OnMapDelete(object? sender, RoutedEventArgs e)
    {
    }

    private void OnEditRelTable(object? sender, RoutedEventArgs e)
    {
    }
}
