using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

// Структурные правки по клавишам (Enter, Backspace, Delete, Tab) и переходы между блоками.
public sealed partial class AuthorView
{
    private void OnStructureRequested(object? sender, StructureRequestEventArgs e)
    {
        if (Document is null)
        {
            return;
        }

        if (sender is not BlockEditor editor)
        {
            return;
        }

        e.Handled = e.Request switch
        {
            StructureRequest.Split => HandleSplit(editor, e.CaretOffset),
            StructureRequest.MergeWithPrevious => HandleMerge(e.Node),
            StructureRequest.DeleteForward => HandleDeleteForward(e.Node),
            StructureRequest.Indent => HandleIndent(e.Node, outdent: false),
            StructureRequest.Outdent => HandleIndent(e.Node, outdent: true),
            StructureRequest.NextBlock => MoveFocus(editor, 1),
            StructureRequest.PreviousBlock => MoveFocus(editor, -1),
            StructureRequest.InsertAfterBlock => ShowInsertMenuAfter(editor),
            _ => false
        };
    }

    /// <summary>Правка детей <paramref name="parent"/>; <paramref name="changed"/> — блоки с новым содержимым.</summary>
    private void Edited(string description, DitaNode parent, DitaNode[] changed, Func<DitaNode?> edit, Func<DitaNode, int>? caret = null)
    {
        BeforeStructuralEdit?.Invoke(this, description);
        var focus = edit();
        Modified();
        RefreshChildren(parent, changed, focus, focus is null || caret is null ? 0 : caret(focus));
    }

    /// <summary>Подсказка по Enter в конце блока включена («Структура → Подсказка по Enter»).</summary>
    public static bool EnterSuggestionsEnabled { get; set; } = true;

    /// <summary>Открытая сейчас подсказка по Enter (или null).</summary>
    public ElementSuggestions? Suggestions { get; private set; }

    private bool HandleSplit(BlockEditor editor, int caretOffset)
    {
        if (editor.Node.Name == "fn")
        {
            return true; // текст сноски — один абзац: Enter в нём ничего не делает
        }

        if (EnterSuggestionsEnabled && caretOffset >= editor.Content.Length && editor.Content.IsLastSegment)
        {
            ShowSuggestions(editor);
            return true;
        }

        return SplitNow(editor, caretOffset);
    }

    /// <summary>
    /// Подсказка по Enter: первой строкой — прежнее действие Enter, дальше элементы, допустимые
    /// после текущего блока, и (если блок последний в родителе) — после родителя.
    /// </summary>
    private void ShowSuggestions(BlockEditor editor)
    {
        var node = editor.Node;
        var catalog = DitaCatalog.Default;
        var items = new List<ElementSuggestion>();
        var anchors = new Dictionary<ElementSuggestion, DitaNode>();
        var defaultTitle = node.Name == "cmd" && node.Parent is { Name: "step" or "substep" }
            ? "Следующий шаг"
            : Describe(node.Name);
        items.Add(new ElementSuggestion(null, defaultTitle + " — как обычно",
            "То, что Enter делал без подсказки: следующий блок того же вида. Двойной Enter — сразу он."));

        var anchor = node;
        for (var level = 0; level < 3 && anchor.Parent is { } parent; level++)
        {
            var where = level == 0 ? string.Empty : $" — после <{anchor.Name}>";
            foreach (var def in catalog.InsertableAt(parent, EditCommands.ElementIndexOf(parent, anchor) + 1)
                         .Where(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty))
                         .DistinctBy(d => d.Name)
                         .OrderBy(d => string.IsNullOrEmpty(d.Description) ? d.Name : d.Description, StringComparer.CurrentCulture))
            {
                var item = new ElementSuggestion(def.Name, $"{Describe(def.Name)}  <{def.Name}>{where}",
                    $"<{def.Name}>\n\n{def.Description}\n\nСодержимое: {def.ModelText}");
                items.Add(item);
                anchors[item] = anchor;
            }

            // Выше поднимаемся, только если блок — последний в родителе (конец списка, раздела).
            if (EditCommands.NextElement(anchor) is not null || catalog.Get(parent.Name)?.IsTopicType == true)
            {
                break;
            }

            anchor = parent;
        }

        var caret = editor.TextArea.Caret.CalculateCaretRectangle();
        var textView = editor.TextArea.TextView;
        var rect = new Rect(caret.X - textView.ScrollOffset.X, caret.Y - textView.ScrollOffset.Y, 1, caret.Height);
        var popup = new ElementSuggestions(textView, rect, items, chosen =>
        {
            if (chosen.Element is null)
            {
                SplitNow(editor, editor.Content.Length);
            }
            else
            {
                InsertAfterBlock(anchors[chosen], chosen.Element);
            }
        });
        popup.Cancelled += (_, _) => editor.FocusEditor(editor.CaretOffset);
        popup.Closed += (_, _) =>
        {
            _root.Children.Remove(popup);
            Suggestions = null;
        };
        _root.Children.Add(popup);
        Suggestions = popup;
        popup.IsOpen = true;
    }

    // ---- «Вставить после блока»: Ctrl+Enter и двойной щелчок по свободному месту

    /// <summary>Предки, внутри которых нового блока не бывает: строки и ячейки таблицы предлагать не нужно —
    /// выход из таблицы делается по самой таблице.</summary>
    private static readonly HashSet<string> TablePartParents = new() { "tgroup", "tbody", "thead", "row", "strow", "sthead" };

    private sealed record InsertSlot(DitaNode Parent, int Index, bool Inside);

    /// <summary>Открытое сейчас меню «вставить после блока» (или null).</summary>
    public ElementSuggestions? InsertMenu => Suggestions;

    private bool ShowInsertMenuAfter(BlockEditor editor)
    {
        var caret = editor.TextArea.Caret.CalculateCaretRectangle();
        var textView = editor.TextArea.TextView;
        var rect = new Rect(caret.X - textView.ScrollOffset.X, caret.Y - textView.ScrollOffset.Y, 1, caret.Height);
        return OpenInsertMenu(editor.Node, null, textView, rect, () => editor.FocusEditor(editor.CaretOffset));
    }

    /// <summary>
    /// Двойной щелчок по свободному месту (под последним блоком, между блоками, в пустом контейнере):
    /// то же меню допустимых элементов, что и по Enter, для места после ближайшего блока над точкой.
    /// <paramref name="point"/> — в координатах панели документа. false — вставлять некуда.
    /// </summary>
    public bool ShowInsertMenuAt(Point point)
    {
        if (Document is null || _order.Count == 0)
        {
            return false;
        }

        // Пустой контейнер (div без содержимого): меню предлагает вставить внутрь.
        DitaNode? emptyContainer = null;
        var smallest = double.MaxValue;
        foreach (var (node, view) in _views)
        {
            if (node.ElementChildren().Any() || !string.IsNullOrWhiteSpace(node.InnerText) || view.GetVisualRoot() is null || DitaCatalog.Default.Get(node.Name) is null ||
                view.TranslatePoint(new Point(0, 0), _panel) is not { } origin)
            {
                continue;
            }

            var bounds = new Rect(origin, view.Bounds.Size);
            if (bounds.Contains(point) && bounds.Width * bounds.Height < smallest)
            {
                smallest = bounds.Width * bounds.Height;
                emptyContainer = node;
            }
        }

        // Ближайший блок над точкой (а если точка выше всех — первый).
        BlockEditor? above = null;
        var aboveTop = double.MinValue;
        foreach (var editor in _order)
        {
            if (editor.GetVisualRoot() is null || editor.Node.Name == "fn" || editor.TranslatePoint(new Point(0, 0), _panel) is not { } top)
            {
                continue;
            }

            if (top.Y <= point.Y && top.Y >= aboveTop)
            {
                above = editor;
                aboveTop = top.Y;
            }
        }

        above ??= _order.FirstOrDefault(e => e.Node.Name != "fn");
        if (above is null && emptyContainer is null)
        {
            return false;
        }

        return OpenInsertMenu(above?.Node, emptyContainer, _panel, new Rect(point, new Size(1, 1)), () => above?.FocusEditor(above.CaretOffset));
    }

    private bool OpenInsertMenu(DitaNode? anchor, DitaNode? emptyContainer, Control target, Rect rect, Action cancelled)
    {
        var catalog = DitaCatalog.Default;
        var items = new List<ElementSuggestion>();
        var slots = new Dictionary<ElementSuggestion, InsertSlot>();

        void Offer(DitaNode parent, int index, string where, bool inside)
        {
            foreach (var def in catalog.InsertableAt(parent, index)
                         .Where(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty))
                         .DistinctBy(d => d.Name)
                         .OrderBy(d => string.IsNullOrEmpty(d.Description) ? d.Name : d.Description, StringComparer.CurrentCulture))
            {
                var item = new ElementSuggestion(def.Name, $"{Describe(def.Name)}  <{def.Name}> — {where}",
                    $"<{def.Name}>\n\n{def.Description}\n\nСодержимое: {def.ModelText}");
                items.Add(item);
                slots[item] = new InsertSlot(parent, index, inside);
            }
        }

        if (emptyContainer is not null)
        {
            Offer(emptyContainer, 0, $"внутрь <{emptyContainer.Name}>", inside: true);
        }

        var level = anchor;
        for (var depth = 0; depth < 6 && level?.Parent is { } parent; depth++)
        {
            if (!TablePartParents.Contains(parent.Name))
            {
                Offer(parent, EditCommands.ElementIndexOf(parent, level) + 1, depth == 0 ? "после этого блока" : $"после <{level.Name}>", inside: false);
            }

            if (catalog.Get(parent.Name)?.IsTopicType == true)
            {
                break;
            }

            level = parent;
        }

        if (items.Count == 0)
        {
            return false;
        }

        var popup = new ElementSuggestions(target, rect, items, chosen =>
        {
            if (chosen.Element is not null && slots.TryGetValue(chosen, out var slot))
            {
                InsertAtSlot(slot, chosen.Element);
            }
        });
        popup.Cancelled += (_, _) => cancelled();
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

    private void InsertAtSlot(InsertSlot slot, string element)
    {
        BeforeStructuralEdit?.Invoke(this, $"Вставка <{element}>");
        if (EditCommands.InsertInto(slot.Parent, element, slot.Index) is not { } created)
        {
            return;
        }

        Modified();
        var focus = created.DescendantsAndSelf().FirstOrDefault(n => n.Kind == NodeKind.Element && DitaCatalog.Default.Get(n.Name) is { IsMixed: true }) ?? created;
        RefreshChildren(slot.Parent, Array.Empty<DitaNode>(), focus);
    }

    private void OnScrollPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_panel).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Двойной щелчок в тексте блока выделяет слово — меню только по свободному месту.
        for (var source = e.Source as Avalonia.Visual; source is not null; source = Avalonia.VisualTree.VisualExtensions.GetVisualParent(source))
        {
            if (source is BlockEditor or Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Popup)
            {
                return;
            }
        }

        // Щелчок по свободному месту снимает выделение: рамку выбранного блока и выделенный текст.
        if (e.ClickCount == 1)
        {
            Deselect();
            return;
        }

        if (e.ClickCount == 2 && ShowInsertMenuAt(e.GetPosition(_panel)))
        {
            e.Handled = true;
        }
    }

    /// <summary>Снимает выделение: подсветку рамки выбранного блока и выделенный текст во всех редакторах;
    /// текущим остаётся блок, в котором стоит курсор.</summary>
    public void Deselect()
    {
        ClearHighlight();
        foreach (var editor in _order)
        {
            if (!editor.TextArea.Selection.IsEmpty)
            {
                editor.TextArea.ClearSelection();
            }
        }

        CurrentNode = _activeEditor is not null && _order.Contains(_activeEditor) ? _activeEditor.Node : null;
        if (_activeEditor is not null)
        {
            HighlightEditor(_activeEditor);
        }
    }

    /// <summary>Узел, чья рамка подсвечена как выбранная (для тестов).</summary>
    public DitaNode? HighlightedNode => _currentBorder?.Tag as DitaNode;

    private static string Describe(string name) =>
        DitaCatalog.Default.Get(name)?.Description is { Length: > 0 } description ? description : name;

    /// <summary>Новый блок после <paramref name="anchor"/>, курсор — в его первое текстовое место.</summary>
    private void InsertAfterBlock(DitaNode anchor, string element)
    {
        BeforeStructuralEdit?.Invoke(this, $"Вставка <{element}>");
        if (EditCommands.InsertAfter(anchor, element) is not { } created)
        {
            return;
        }

        Modified();
        var focus = created.DescendantsAndSelf().FirstOrDefault(n => n.Kind == NodeKind.Element && DitaCatalog.Default.Get(n.Name) is { IsMixed: true }) ?? created;
        RefreshChildren(anchor.Parent!, Array.Empty<DitaNode>(), focus);
    }

    private bool SplitNow(BlockEditor editor, int caretOffset)
    {
        var node = editor.Node;
        // В шаге Enter создаёт следующий шаг, а не второй cmd.
        if (node.Name == "cmd" && node.Parent is { Name: "step" or "substep" } step)
        {
            BeforeStructuralEdit?.Invoke(this, "Новый шаг");
            if (EditCommands.InsertAfter(step, step.Name) is not { } createdStep)
            {
                return false;
            }

            Modified();
            RebuildAround(step.Parent!, createdStep.FirstElement("cmd") ?? createdStep);
            return true;
        }

        if (node.Parent is not { } parent)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Разделение блока");
        if (BlockOperations.SplitBlock(editor.Content, caretOffset) is not { } created)
        {
            return false;
        }

        Modified();
        RefreshChildren(parent, new[] { node }, created);
        return true;
    }

    private bool HandleMerge(DitaNode node)
    {
        if (node.Name == "fn")
        {
            return false; // Backspace в начале текста сноски не сливает её с соседями
        }

        if (EditCommands.PreviousElement(node) is not { } previous)
        {
            return false;
        }

        var offset = InlineContent.FromNode(previous).Length;
        if (previous.Name != node.Name)
        {
            // Пустой блок после элемента другого типа просто удаляется.
            if (InlineContent.FromNode(node).Length > 0)
            {
                return false;
            }

            Edited("Удаление пустого блока", node.Parent!, new[] { node }, () => EditCommands.Delete(node) ? previous : node, focus => ReferenceEquals(focus, previous) ? offset : 0);
            return true;
        }

        Edited("Объединение блоков", node.Parent!, new[] { previous, node }, () => EditCommands.MergeWithPrevious(node), _ => offset);
        return true;
    }

    /// <summary>Delete в конце блока — присоединяет следующий блок того же типа.</summary>
    private bool HandleDeleteForward(DitaNode node)
    {
        if (EditCommands.NextElement(node) is not { } next || next.Name != node.Name)
        {
            return false;
        }

        var offset = InlineContent.FromNode(node).Length;
        Edited("Объединение блоков", next.Parent!, new[] { node, next }, () => EditCommands.MergeWithPrevious(next), _ => offset);
        return true;
    }

    private bool HandleIndent(DitaNode node, bool outdent)
    {
        if (BlockOperations.ListItemFor(node) is not { } item)
        {
            return false;
        }

        // Затрагивается список пункта (Tab) или внешний список (Shift+Tab).
        var changed = outdent ? item.Parent?.Parent?.Parent ?? item : item.Parent!;
        BeforeStructuralEdit?.Invoke(this, outdent ? "Уменьшение уровня" : "Увеличение уровня");
        if (!(outdent ? BlockOperations.OutdentItem(item) : BlockOperations.IndentItem(item)))
        {
            return false;
        }

        Modified();
        RebuildAround(changed, ReferenceEquals(item, node) ? node : item.FirstElement("cmd") ?? node);
        return true;
    }

    private bool MoveFocus(BlockEditor editor, int direction)
    {
        var index = _order.IndexOf(editor) + direction;
        if (index < 0 || index >= _order.Count)
        {
            return false;
        }

        var next = _order[index];
        next.FocusEditor(direction > 0 ? 0 : next.PlainTextLength);
        next.BringIntoView();
        return true;
    }
}
