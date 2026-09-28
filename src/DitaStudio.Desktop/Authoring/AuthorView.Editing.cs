using Avalonia;
using Avalonia.Controls;
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
