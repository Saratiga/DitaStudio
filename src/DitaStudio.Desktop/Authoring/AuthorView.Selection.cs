using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Desktop.Authoring;

// Выделение блоков целиком: один блок (щелчок по рамке), несколько соседних (протяжка мыши от блока к блоку, Shift+щелчок) —
// контуры, Delete/Backspace, Esc, «Обернуть в…».
public sealed partial class AuthorView
{
    // Контур выделенного блока: прежняя рамка запоминается, чтобы вернуть её при снятии.
    private sealed class OutlineState
    {
        public required Border Border { get; init; }

        public Thickness Thickness { get; init; }

        public IBrush? Brush { get; init; }

        public IDisposable? Binding { get; set; }
    }

    private readonly List<OutlineState> _outlines = new();

    private void SelectOutline(Border border)
    {
        ClearOutline();
        AddOutline(border);
    }

    private void AddOutline(Border border)
    {
        if (_outlines.Any(o => ReferenceEquals(o.Border, border)))
        {
            return;
        }

        var state = new OutlineState { Border = border, Thickness = border.BorderThickness, Brush = border.BorderBrush };
        border.BorderThickness = new Thickness(2);
        border.CornerRadius = new CornerRadius(3);
        state.Binding = Themed(border, Border.BorderBrushProperty, "Accent");
        _outlines.Add(state);
    }

    private void ClearOutline()
    {
        foreach (var state in _outlines)
        {
            state.Binding?.Dispose();
            state.Border.BorderThickness = state.Thickness;
            state.Border.BorderBrush = state.Brush;
            state.Border.CornerRadius = default;
        }

        _outlines.Clear();
    }

    internal void SelectTableOutline(Border border, DitaNode table)
    {
        CurrentNode = table;
        SelectOutline(border);
        Focus();
    }

    /// <summary>Узел выделенной целиком таблицы (контур) или null.</summary>
    public DitaNode? SelectedTable => SelectedBlock is { Name: "table" or "simpletable" or "properties" or "choicetable" } table ? table : null;

    /// <summary>Выделенный целиком блок (контур вокруг него) или null: рисунок, примечание, абзац, таблица, список… Null и при выделении
    /// нескольких блоков — тогда <see cref="SelectedBlocks"/>.</summary>
    public DitaNode? SelectedBlock => _outlines.Count == 1 ? _outlines[0].Border.Tag as DitaNode : null;

    /// <summary>Выделенные целиком блоки по порядку (один или несколько соседних); пусто — блоки не выделены.</summary>
    public IReadOnlyList<DitaNode> SelectedBlocks => _outlines.Select(o => o.Border.Tag).OfType<DitaNode>().ToList();

    /// <summary>Выделяет блок целиком (контур, фокус на «Авторе»): дальше Delete и Backspace удаляют его, Esc снимает выделение.
    /// false — у узла нет собственной рамки в «Авторе».</summary>
    public bool SelectBlock(DitaNode node)
    {
        if (FindBlockBorder(node) is not { } border)
        {
            return false;
        }

        CurrentNode = node;
        HighlightBorder(border);
        SelectOutline(border);
        Focus();
        return true;
    }

    /// <summary>Выделяет блоки целиком: соседей общего родителя от первого до последнего узла и всё между ними. false — у блока нет
    /// рамки или узлы не соседние.</summary>
    public bool SelectBlocks(DitaNode first, DitaNode last)
    {
        if (BlockRanges.Between(first, last) is not { } span)
        {
            return SelectBlock(first);
        }

        return SelectSpan(span, focus: true);
    }

    private bool SelectSpan(BlockSpan span, bool focus)
    {
        var borders = span.Blocks.Select(FindBlockBorder).Where(b => b is not null).Select(b => b!).ToList();
        if (borders.Count == 0)
        {
            return false;
        }

        ClearOutline();
        foreach (var border in borders)
        {
            AddOutline(border);
        }

        if (span.Blocks.FirstOrDefault(n => n.Kind == NodeKind.Element) is { } firstElement)
        {
            CurrentNode = firstElement;
        }

        if (focus)
        {
            Focus();
        }

        return true;
    }

    /// <summary>Есть ли у узла рамка, за которую его можно выделить целиком.</summary>
    public bool CanSelectBlock(DitaNode node) => FindBlockBorder(node) is not null;

    private Border? FindBlockBorder(DitaNode node)
    {
        var border = ViewFor(node) is { } view
            ? (view as Border ?? view.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, node)))
            : null;
        border ??= this.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, node));
        return border is not null && ReferenceEquals(border.Tag, node) ? border : null;
    }

    // ---------------------------------------------------------------- протяжка мыши от блока к блоку

    private BlockEditor? _dragEditor;
    private bool _rangeDragging;

    private void InitializeBlockSelection()
    {
        AddHandler(PointerPressedEvent, OnPointerPressedForBlocks, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMovedForBlocks, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedForBlocks, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPointerPressedForBlocks(object? sender, PointerPressedEventArgs e)
    {
        _dragEditor = null;
        _rangeDragging = false;
        var point = e.GetCurrentPoint(this);

        // Правая кнопка внутри выделенных блоков — меню выделения (а не меню текста одного блока: оно сняло бы выделение).
        if (point.Properties.IsRightButtonPressed && _outlines.Count > 0 && IsOverOutlined(e.GetPosition(this)))
        {
            e.Handled = true;
            ShowSelectionMenu();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || (e.Source as Visual)?.FindAncestorOfType<BlockEditor>(includeSelf: true) is not { } editor)
        {
            return;
        }

        // Shift+щелчок в другом блоке: диапазон от выделенного (или текущего) блока до этого.
        var anchor = _outlines.Count > 0 ? SelectedBlocks.FirstOrDefault() : _activeEditor is { } active && _order.Contains(active) ? active.Node : null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && anchor is not null && !ReferenceEquals(anchor, editor.Node) &&
            BlockRanges.Between(anchor, editor.Node) is { } span && SelectSpan(span, focus: true))
        {
            e.Handled = true;
            return;
        }

        _dragEditor = editor;
    }

    private void OnPointerMovedForBlocks(object? sender, PointerEventArgs e)
    {
        if (_dragEditor is not { } start || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        AutoScrollWhileDragging(e);
        var target = EditorNear(e.GetPosition(this));
        var span = target is null || ReferenceEquals(target.Node, start.Node) ? null : BlockRanges.Between(start.Node, target.Node);
        if (span is null)
        {
            // Курсор вернулся в начальный блок (или ушёл в ячейки другой строки) — обычное выделение текста.
            if (_rangeDragging)
            {
                _rangeDragging = false;
                ClearOutline();
            }

            return;
        }

        if (!_rangeDragging)
        {
            // Протяжка вышла за пределы блока: выделение текста внутри снимается, дальше выделяются блоки целиком.
            _rangeDragging = true;
            foreach (var editor in _order)
            {
                editor.TextArea.ClearSelection();
            }
        }

        SelectSpan(span, focus: false);
        e.Handled = true;
    }

    private void OnPointerReleasedForBlocks(object? sender, PointerReleasedEventArgs e)
    {
        var finished = _rangeDragging;
        _dragEditor = null;
        _rangeDragging = false;
        if (finished && _outlines.Count > 0)
        {
            Focus(); // клавиши (Delete, Esc, Enter) теперь относятся к выделенным блокам
        }
    }

    // Блок, над которым (или ближе всего по вертикали) стоит курсор: «Автор» — одна колонка блоков.
    private BlockEditor? EditorNear(Point position)
    {
        BlockEditor? best = null;
        var bestDistance = double.MaxValue;
        foreach (var editor in _order)
        {
            if (editor.Bounds.Height <= 0 || editor.TranslatePoint(new Point(0, 0), this) is not { } top)
            {
                continue;
            }

            var distance = position.Y < top.Y ? top.Y - position.Y : position.Y > top.Y + editor.Bounds.Height ? position.Y - (top.Y + editor.Bounds.Height) : 0;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = editor;
            }
        }

        return best;
    }

    // Протяжка у верхнего или нижнего края окна прокручивает «Автор» — можно выделить блоки за пределами экрана.
    private void AutoScrollWhileDragging(PointerEventArgs e)
    {
        var y = e.GetPosition(_scroll).Y;
        var height = _scroll.Bounds.Height;
        if (y < 0 || y > height)
        {
            var step = Math.Min(60, y < 0 ? -y : y - height) + 6;
            _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, _scroll.Offset.Y + (y < 0 ? -step : step)));
        }
    }

    private bool IsOverOutlined(Point position) =>
        _outlines.Any(o => o.Border.TranslatePoint(new Point(0, 0), this) is { } top &&
                           new Rect(top, o.Border.Bounds.Size).Contains(position));

    // ---------------------------------------------------------------- клавиши и меню выделенных блоков

    /// <summary>Удаляет выделенные блоки (один шаг отмены). У ячейки таблицы очищает содержимое — сама ячейка остаётся.</summary>
    public bool DeleteSelectedBlocks()
    {
        var nodes = SelectedBlocks;
        if (nodes.Count == 0)
        {
            return false;
        }

        ClearOutline(); // рамки остались бы на уже удалённых блоках
        if (nodes.Count > 1)
        {
            return Surface.DeleteBlocks(nodes);
        }

        CurrentNode = nodes[0];
        if (nodes[0].Name is "entry" or "stentry")
        {
            ClearCell(nodes[0]);
            return true;
        }

        return Surface.DeleteCurrent();
    }

    /// <summary>Диапазон, к которому относится «Обернуть в…»: выделенные блоки или (если выделения нет) блок под курсором.</summary>
    private BlockSpan? WrapSpan()
    {
        var nodes = _outlines.Count > 0 ? SelectedBlocks : CurrentNode is { } current ? new[] { current } : Array.Empty<DitaNode>();
        if (nodes.Count == 0 || nodes[0].Parent is not { } parent)
        {
            return null;
        }

        var indexes = nodes.Select(n => parent.IndexOf(n)).Where(i => i >= 0).ToList();
        return indexes.Count == 0 ? null : new BlockSpan(parent, indexes.Min(), indexes.Max());
    }

    /// <summary>Допустимые обёртки для выделенных блоков (или блока под курсором) — то, что покажет «Обернуть в…».</summary>
    public IReadOnlyList<string> WrapOptions() =>
        WrapSpan() is { } span ? BlockRanges.WrapCandidates(span.Parent, span.First, span.Last).Select(d => d.Name).ToList() : Array.Empty<string>();

    /// <summary>Окно «Обернуть в…» — то же, что подсказка по Enter: фильтр, список допустимых элементов, описание. false — обернуть нечего.</summary>
    public bool ShowWrapMenu()
    {
        if (WrapSpan() is not { } span)
        {
            return false;
        }

        var candidates = BlockRanges.WrapCandidates(span.Parent, span.First, span.Last);
        if (candidates.Count == 0)
        {
            return false;
        }

        var items = candidates
            .OrderBy(d => string.IsNullOrEmpty(d.Description) ? d.Name : d.Description, StringComparer.CurrentCulture)
            .Select(d => new ElementSuggestion(d.Name, $"{Describe(d.Name)}  <{d.Name}>", $"<{d.Name}>\n\n{d.Description}\n\nСодержимое: {d.ModelText}"))
            .ToList();
        var first = span.Blocks.Select(FindBlockBorder).FirstOrDefault(b => b is not null);
        Control target = first ?? (Control)this;
        var rect = new Rect(0, 0, target.Bounds.Width, Math.Max(1, target.Bounds.Height));
        var popup = new ElementSuggestions(target, rect, items, chosen => WrapSelection(span, chosen.Element!));
        popup.Cancelled += (_, _) => Focus();
        popup.Closed += (_, _) =>
        {
            _root.Children.Remove(popup);
            Suggestions = null;
        };
        _root.Children.Add(popup);
        Suggestions = popup;
        popup.IsOpen = true;
        return true;
    }

    /// <summary>Оборачивает диапазон блоков в элемент <paramref name="element"/> (один шаг отмены); обёртка остаётся выделенной.</summary>
    public bool WrapSelection(BlockSpan span, string element)
    {
        ClearOutline();
        if (Surface.WrapBlocks(span.Parent, span.First, span.Last, element) is not { } wrapper)
        {
            return false;
        }

        SelectBlock(wrapper);
        return true;
    }

    /// <summary>Оборачивает выделенные блоки (или блок под курсором) в <paramref name="element"/> — без окна выбора (для клавиш и тестов).</summary>
    public bool WrapSelection(string element) => WrapSpan() is { } span && WrapSelection(span, element);

    private void ShowSelectionMenu()
    {
        var wrap = new MenuItem { Header = "Обернуть в…", InputGesture = new KeyGesture(Key.Enter), IsEnabled = WrapOptions().Count > 0 };
        wrap.Click += (_, _) => ShowWrapMenu();
        var delete = new MenuItem { Header = "Удалить выделенное", InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => DeleteSelectedBlocks();
        var clear = new MenuItem { Header = "Снять выделение", InputGesture = new KeyGesture(Key.Escape) };
        clear.Click += (_, _) => Deselect();
        new ContextMenu { ItemsSource = new List<Control> { wrap, delete, new Separator(), clear } }.Open(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _outlines.Count == 0)
        {
            return;
        }

        // Выделены блоки целиком (а не текст в них): Delete и Backspace удаляют, Enter открывает «Обернуть в…», Esc снимает выделение.
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                e.Handled = true;
                DeleteSelectedBlocks();
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                e.Handled = ShowWrapMenu();
                break;
            case Key.Escape:
                e.Handled = true;
                Deselect();
                break;
        }
    }
}
