using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Presentation;
using DitaStudio.Core.Localization;

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
    private readonly TextBox _filter = new() { Watermark = Loc.T("Author_Filter"), Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 4) };
    private readonly ListBox _list = new();
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 2, 2) };
    private readonly Grid _grid = new();
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
        var description = new ScrollViewer { Content = _description, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _grid.ColumnDefinitions = new ColumnDefinitions("1.25*,*");
        _grid.Children.Add(left);
        _grid.Children.Add(description);
        Grid.SetColumn(description, 1);

        // Размер — тот, что пользователь выбрал в прошлый раз; ручка в правом нижнем углу растягивает окно.
        (_grid.Width, _grid.Height) = PopupSizeSettings.Load();
        var gripMark = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 14,2 L 2,14 M 14,7 L 7,14 M 14,12 L 12,14"),
            Stroke = Brushes.Gray,
            StrokeThickness = 1.2,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        Grid.SetColumnSpan(gripMark, 2);
        _grid.Children.Add(gripMark);
        foreach (var corner in new[] { PopupCorner.BottomRight, PopupCorner.BottomLeft, PopupCorner.TopRight, PopupCorner.TopLeft })
        {
            var grip = CreateGrip(corner);
            Grid.SetColumnSpan(grip, 2);
            _grid.Children.Add(grip);
        }

        var grid = _grid;

        var border = new Border { Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = grid };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SurfaceAlt"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Line"));
        Child = border;

        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemTemplate = new FuncDataTemplate<ElementSuggestion>((item, _) => new TextBlock { Text = item?.Title, TextWrapping = TextWrapping.Wrap });
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

    /// <summary>Размер окна подсказки (ширина, высота), px.</summary>
    public (double Width, double Height) ContentSize => (_grid.Width, _grid.Height);

    /// <summary>Растягивает окно на <paramref name="dx"/> и <paramref name="dy"/> в пределах допустимого; запоминает только по
    /// окончании перетаскивания (см. ручку), а здесь — только меняет.</summary>
    public void ResizeBy(double dx, double dy) =>
        (_grid.Width, _grid.Height) = PopupSizeSettings.Clamp(_grid.Width + dx, _grid.Height + dy);

    /// <summary>Угол окна, за который его растягивают.</summary>
    public enum PopupCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Растягивает окно за угол: <paramref name="dx"/>, <paramref name="dy"/> — сдвиг этого угла. За правые и нижние углы окно растёт
    /// в ту же сторону; за левые и верхние — в сторону сдвига, а противоположный угол остаётся на месте (окно перемещается).
    /// Размер ограничен (<see cref="PopupSizeSettings.Clamp"/>); возвращает фактическое изменение размера (ширина, высота).
    /// </summary>
    public (double Width, double Height) ResizeFromCorner(PopupCorner corner, double dx, double dy)
    {
        var left = corner is PopupCorner.TopLeft or PopupCorner.BottomLeft;
        var top = corner is PopupCorner.TopLeft or PopupCorner.TopRight;
        var (oldWidth, oldHeight) = (_grid.Width, _grid.Height);
        (_grid.Width, _grid.Height) = PopupSizeSettings.Clamp(oldWidth + (left ? -dx : dx), oldHeight + (top ? -dy : dy));
        var (grewX, grewY) = (_grid.Width - oldWidth, _grid.Height - oldHeight);
        if (left)
        {
            HorizontalOffset -= grewX;
        }

        if (top)
        {
            VerticalOffset -= grewY;
        }

        return (grewX, grewY);
    }

    // Ручка угла: невидимая, с нужным курсором. Сдвиг считается по экранным координатам указателя, а не по DragDelta ручки:
    // при растягивании за левый или верхний угол окно (а с ним и ручка) едет под указателем, и относительный сдвиг «дрожал» бы.
    private Control CreateGrip(PopupCorner corner)
    {
        var grip = new Border
        {
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            HorizontalAlignment = corner is PopupCorner.TopLeft or PopupCorner.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = corner is PopupCorner.TopLeft or PopupCorner.TopRight ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Cursor = new Cursor(corner switch
            {
                PopupCorner.TopLeft => StandardCursorType.TopLeftCorner,
                PopupCorner.TopRight => StandardCursorType.TopRightCorner,
                PopupCorner.BottomLeft => StandardCursorType.BottomLeftCorner,
                _ => StandardCursorType.BottomRightCorner
            }),
            Tag = corner
        };
        ToolTip.SetTip(grip, Loc.T("Author_DragToResizeTheWindow"));

        PixelPoint? last = null;
        PixelPoint Screen(PointerEventArgs e)
        {
            var root = TopLevel.GetTopLevel(grip)!;
            return root.PointToScreen(e.GetPosition(root));
        }

        grip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(grip) is not null)
            {
                last = Screen(e);
                e.Pointer.Capture(grip);
                e.Handled = true;
            }
        };
        grip.PointerMoved += (_, e) =>
        {
            if (last is not { } from || TopLevel.GetTopLevel(grip) is not { } root)
            {
                return;
            }

            var now = Screen(e);
            ResizeFromCorner(corner, (now.X - from.X) / root.RenderScaling, (now.Y - from.Y) / root.RenderScaling);
            last = now;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (last is null)
            {
                return;
            }

            last = null;
            e.Pointer.Capture(null);
            PopupSizeSettings.Save(_grid.Width, _grid.Height);
        };
        return grip;
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
