using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DitaStudio.Desktop.Authoring;

/// <summary>Строка подсказки: элемент (null — действие Enter «как обычно»), подпись и описание.</summary>
public sealed record ElementSuggestion(string? Element, string Title, string Description)
{
    public override string ToString() => Title;
}

/// <summary>
/// Панель у курсора по Enter в конце блока (как в Oxygen): фильтр, список допустимых элементов и
/// описание выбранного справа. Первая строка — прежнее действие Enter, поэтому двойной Enter
/// работает как раньше. Enter или двойной щелчок — выбрать, Esc — закрыть, набор — фильтр.
/// </summary>
public sealed class ElementSuggestions : Popup
{
    private readonly IReadOnlyList<ElementSuggestion> _all;
    private readonly Action<ElementSuggestion> _apply;
    private readonly TextBox _filter = new() { Watermark = "Фильтр…", Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 4) };
    private readonly ListBox _list = new() { Width = 300, Height = 220 };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Width = 240, Margin = new Thickness(10, 2, 2, 2) };
    private bool _applied;

    public ElementSuggestions(Control target, Rect caret, IReadOnlyList<ElementSuggestion> items, Action<ElementSuggestion> apply)
    {
        _all = items;
        _apply = apply;
        PlacementTarget = target;
        PlacementRect = caret;
        Placement = PlacementMode.BottomEdgeAlignedLeft;
        IsLightDismissEnabled = true;

        var left = new DockPanel { Children = { _filter, _list } };
        DockPanel.SetDock(_filter, Dock.Top);
        var description = new ScrollViewer { Content = _description, Height = 250 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), Children = { left, description } };
        Grid.SetColumn(description, 1);

        var border = new Border { Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = grid };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SurfaceAlt"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Line"));
        Child = border;

        _list.ItemsSource = items;
        _list.SelectedIndex = 0;
        _list.SelectionChanged += (_, _) => _description.Text = (_list.SelectedItem as ElementSuggestion)?.Description ?? string.Empty;
        _description.Text = items.FirstOrDefault()?.Description ?? string.Empty;
        _list.DoubleTapped += (_, _) => Apply();
        _filter.TextChanged += (_, _) => Filter();
        _filter.AddHandler(KeyDownEvent, OnFilterKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.AddHandler(KeyDownEvent, OnFilterKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => _filter.Focus();
        Closed += (_, _) =>
        {
            if (!_applied)
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Закрыта без выбора (Esc, щелчок мимо).</summary>
    public event EventHandler? Cancelled;

    public IReadOnlyList<ElementSuggestion> Visible => _list.ItemsSource as IReadOnlyList<ElementSuggestion> ?? _all;

    public ElementSuggestion? Selected => _list.SelectedItem as ElementSuggestion;


    /// <summary>Выбирает текущую строку (для клавиатуры и тестов).</summary>
    public void Apply()
    {
        if (Selected is not { } item || _applied)
        {
            return;
        }

        _applied = true;
        IsOpen = false;
        _apply(item);
    }

    /// <summary>Выбирает строку списка (для тестов).</summary>
    public void Select(ElementSuggestion item) => _list.SelectedItem = item;

    /// <summary>Закрывает без выбора.</summary>
    public void Cancel() => IsOpen = false;

    /// <summary>Фильтр по имени элемента и подписи.</summary>
    public void Filter(string text)
    {
        _filter.Text = text;
        Filter();
    }

    private void Filter()
    {
        var text = (_filter.Text ?? string.Empty).Trim();
        var visible = text.Length == 0
            ? _all
            : _all.Where(s => s.Element is { } name && name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                              s.Title.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = visible;
        _list.SelectedIndex = visible.Count > 0 ? 0 : -1;
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Apply();
                e.Handled = true;
                break;
            case Key.Escape:
                IsOpen = false;
                e.Handled = true;
                break;
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.ItemCount - 1);
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
        }
    }
}
