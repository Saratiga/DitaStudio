using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

// «Точка вставки» между блоками (как в Oxygen): щелчок по свободному месту под блоком ставит туда курсор-черту.
// Ввод текста создаёт там новый блок (обычно абзац), Enter — меню допустимых элементов, Ctrl+V — вставка,
// палитра и кнопки вставки ставят блок сюда же: текущим узлом становится блок над чертой, а вставка блока
// идёт «после текущего». Курсор в тексте блока, Esc и стрелки убирают черту.
public sealed partial class AuthorView
{
    /// <summary>Место между блоками: новый блок встанет в <paramref name="Parent"/> на позицию <paramref name="Index"/>,
    /// сразу после <paramref name="Anchor"/>.</summary>
    public sealed record InsertPoint(DitaNode Anchor, DitaNode Parent, int Index);

    private InsertPoint? _insertPoint;
    private Border? _insertMarker;
    private DispatcherTimer? _insertBlink;

    /// <summary>Поставленная точка вставки между блоками (или null).</summary>
    public InsertPoint? CurrentInsertPoint => _insertPoint;

    /// <summary>
    /// Ставит точку вставки по щелчку в свободное место <paramref name="point"/> (координаты панели документа): после
    /// ближайшего блока над точкой, куда можно вставить хоть какой-то блок. false — такого места нет.
    /// </summary>
    public bool PlaceInsertPointAt(Point point)
    {
        if (FindInsertPoint(point) is not { } found)
        {
            return false;
        }

        SetInsertPoint(found);
        return true;
    }

    /// <summary>Точка вставки сразу после блока <paramref name="anchor"/> (если туда можно что-нибудь вставить).</summary>
    public bool PlaceInsertPointAfter(DitaNode anchor)
    {
        if (anchor.Parent is not { } parent || !CanHoldInsertPoint(anchor))
        {
            return false;
        }

        SetInsertPoint(new InsertPoint(anchor, parent, EditCommands.ElementIndexOf(parent, anchor) + 1));
        return true;
    }

    private InsertPoint? FindInsertPoint(Point point)
    {
        if (Document is null)
        {
            return null;
        }

        // Блок над точкой: нижний край выше точки и ниже всех остальных таких. Из блоков с общим нижним краем (последний
        // абзац заметки и сама заметка, последняя строка таблицы и таблица) берётся внешний — черта после него.
        DitaNode? best = null;
        var bestBottom = double.MinValue;
        foreach (var (node, view) in _views)
        {
            if (!CanHoldInsertPoint(node) || view.GetVisualRoot() is null || view.TranslatePoint(new Point(0, 0), _panel) is not { } origin)
            {
                continue;
            }

            var bounds = new Rect(origin, view.Bounds.Size);
            if (bounds.Bottom > point.Y + 1 || bounds.Width <= 0 || point.X < bounds.Left - 40 || point.X > bounds.Right + 40)
            {
                continue;
            }

            if (bounds.Bottom > bestBottom + 1.5 ||
                (Math.Abs(bounds.Bottom - bestBottom) <= 1.5 && best is not null && best.Ancestors().Contains(node)))
            {
                best = node;
                bestBottom = bounds.Bottom;
            }
        }

        return best is { Parent: { } parent } ? new InsertPoint(best, parent, EditCommands.ElementIndexOf(parent, best) + 1) : null;
    }

    /// <summary>После узла может стоять точка вставки: он блочный, не часть таблицы и не сноска, а после него допустим блок.</summary>
    private static bool CanHoldInsertPoint(DitaNode node)
    {
        var catalog = DitaCatalog.Default;
        if (node.Kind != NodeKind.Element || node.Parent is not { } parent || node.Name == "fn" ||
            TablePartParents.Contains(parent.Name) || catalog.Get(parent.Name) is not { } parentDef || parentDef.IsTopicType ||
            catalog.Get(node.Name) is not { } def || def.Display is DisplayKind.Inline or DisplayKind.Empty)
        {
            return false;
        }

        return catalog.InsertableAt(parent, EditCommands.ElementIndexOf(parent, node) + 1)
            .Any(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty));
    }

    private void SetInsertPoint(InsertPoint point)
    {
        // Курсор уходит из текста: выделения и рамка выбранного блока снимаются, текущим становится блок над чертой —
        // так палитра и команды вставки ставят новый блок после него.
        foreach (var editor in _order)
        {
            if (!editor.TextArea.Selection.IsEmpty)
            {
                editor.TextArea.ClearSelection();
            }
        }

        ClearHighlight();
        _insertPoint = point;
        _activeEditor = null;
        Focus();
        CurrentNode = point.Anchor;
        ShowInsertMarker();
    }

    /// <summary>Убирает точку вставки (курсор ушёл в текст, документ перестроен, Esc).</summary>
    public void ClearInsertPoint()
    {
        if (_insertPoint is null)
        {
            return;
        }

        _insertPoint = null;
        _insertBlink?.Stop();
        if (_insertMarker is not null)
        {
            _insertMarker.IsVisible = false;
        }
    }

    private void ShowInsertMarker()
    {
        if (_insertMarker is null)
        {
            _insertMarker = new Border
            {
                Height = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Tag = "insert-point"
            };
            Themed(_insertMarker, Border.BackgroundProperty, "Accent");
            _root.Children.Add(_insertMarker);
            _scroll.ScrollChanged += (_, _) => PositionInsertMarker();
            _panel.LayoutUpdated += (_, _) => PositionInsertMarker();
            _insertBlink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _insertBlink.Tick += (_, _) =>
            {
                if (_insertMarker is not null)
                {
                    _insertMarker.Opacity = IsKeyboardFocusWithin && _insertMarker.Opacity > 0.5 ? 0.15 : 1;
                }
            };
        }

        _insertMarker.Opacity = 1;
        _insertMarker.IsVisible = true;
        _insertBlink?.Start();
        PositionInsertMarker();
    }

    private void PositionInsertMarker()
    {
        if (_insertPoint is null || _insertMarker is null)
        {
            return;
        }

        if (!_views.TryGetValue(_insertPoint.Anchor, out var view) || view.GetVisualRoot() is null ||
            view.TranslatePoint(new Point(0, view.Bounds.Height), _root) is not { } bottom)
        {
            _insertMarker.IsVisible = false;
            return;
        }

        // Черта — посередине промежутка до следующего блока (не больше 6 px ниже блока).
        var y = bottom.Y + 3;
        _insertMarker.Margin = new Thickness(bottom.X, y, 0, 0);
        _insertMarker.Width = Math.Max(24, view.Bounds.Width);
        _insertMarker.IsVisible = y >= 0 && y <= _root.Bounds.Height;
    }

    /// <summary>Элемент, который создаёт ввод текста в точке вставки: абзац, а где его нельзя — первый допустимый блок с текстом.</summary>
    private static string? DefaultBlockAt(InsertPoint point)
    {
        var allowed = DitaCatalog.Default.InsertableAt(point.Parent, point.Index)
            .Where(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty)).ToList();
        foreach (var preferred in new[] { "p", "li", "step", "dd", "entry" })
        {
            if (allowed.Any(d => d.Name == preferred))
            {
                return preferred;
            }
        }

        return allowed.FirstOrDefault(d => d.IsMixed)?.Name ?? allowed.FirstOrDefault()?.Name;
    }

    /// <summary>Ввод в точке вставки: новый блок (абзац) с этим текстом, курсор — за текстом. false — вставить нечего.</summary>
    public bool TypeAtInsertPoint(string text)
    {
        if (_insertPoint is not { } point || DefaultBlockAt(point) is not { } element)
        {
            return false;
        }

        ClearInsertPoint();
        BeforeStructuralEdit?.Invoke(this, Loc.T("Author_Insert0", element));
        if (EditCommands.InsertInto(point.Parent, element, point.Index) is not { } created)
        {
            return false;
        }

        Modified();
        var focus = created.DescendantsAndSelf().FirstOrDefault(n => n.Kind == NodeKind.Element && DitaCatalog.Default.Get(n.Name) is { IsMixed: true }) ?? created;
        // Курсор ставится после перестройки (отложенно) — сразу за набранным текстом.
        RefreshChildren(point.Parent, Array.Empty<DitaNode>(), focus, text.Length);
        if (EditorFor(focus) is { } editor && text.Length > 0)
        {
            // В той же обработке, что снимок вставки: набранное входит в шаг отмены «Вставить …».
            editor.Document.Insert(0, text);
        }

        return true;
    }

    private bool InsertPointKey(KeyEventArgs e)
    {
        if (_insertPoint is not { } point)
        {
            return false;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Escape:
                ClearInsertPoint();
                return true;

            case Key.Enter when !ctrl:
                var rect = _insertMarker is { IsVisible: true } marker
                    ? new Rect(marker.Margin.Left, marker.Margin.Top, 1, 2)
                    : new Rect(0, 0, 1, 1);
                return OpenInsertMenu(point.Anchor, null, _root, rect, () => Focus());

            case Key.Up or Key.Left:
                // Курсор в конец блока над чертой.
                ClearInsertPoint();
                if (_order.LastOrDefault(ed => ReferenceEquals(ed.Node, point.Anchor) || ed.Node.Ancestors().Contains(point.Anchor)) is { } above)
                {
                    above.FocusEditor(above.PlainTextLength);
                }

                return true;

            case Key.Down or Key.Right:
                // Курсор в начало первого блока под чертой.
                ClearInsertPoint();
                var after = _order.SkipWhile(ed => !(ReferenceEquals(ed.Node, point.Anchor) || ed.Node.Ancestors().Contains(point.Anchor)))
                    .SkipWhile(ed => ReferenceEquals(ed.Node, point.Anchor) || ed.Node.Ancestors().Contains(point.Anchor))
                    .FirstOrDefault(ed => ed.Node.Name != "fn");
                (after ?? _order.LastOrDefault())?.FocusEditor(0);
                return true;

            case Key.V when ctrl:
                _ = PasteAtInsertPointAsync();
                return true;

            default:
                return false;
        }
    }

    private async Task PasteAtInsertPointAsync()
    {
        if (_insertPoint is not { } point)
        {
            return;
        }

        var text = SystemClipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        text ??= BlockClipboard.LastCopiedText;
        if (BlockClipboard.Parse(text) is not null)
        {
            ClearInsertPoint();
            await PasteBlocksAsync(point.Anchor);
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            TypeAtInsertPoint(string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim());
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!e.Handled && _insertPoint is not null && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]) &&
            TypeAtInsertPoint(e.Text))
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);
    }
}
