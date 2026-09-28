using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.Authoring;

// Выделение блоков и структурные правки (Enter, Backspace, Tab) по контент-модели.
public sealed partial class AuthorView
{
    private void AttachSelection(Border border, DitaNode node)
    {
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Border)
            {
                CurrentNode = node;
                HighlightBorder(border);
            }
        };
    }

    private void HighlightCurrent(InlineEditor editor)
    {
        var parent = VisualTreeHelper.GetParent(editor);
        while (parent is not null && parent is not Border)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is Border border)
        {
            HighlightBorder(border);
        }
    }

    private void HighlightBorder(Border border)
    {
        if (_currentBorder is not null && !ReferenceEquals(_currentBorder, border))
        {
            _currentBorder.BorderBrush = Brushes.Transparent;
        }

        if (border.BorderThickness.Left >= 2 && border.BorderThickness.Top == 0)
        {
            border.BorderBrush = SelectedBorder;
            _currentBorder = border;
        }
    }

    private static string FormatAttributes(DitaNode node)
    {
        if (node.Attributes.Count == 0)
        {
            return string.Empty;
        }

        return " " + string.Join(" ", node.Attributes.Select(a => $"{a.Name}=\"{a.Value}\""));
    }

    // ------------------------------------------------------- структурные правки

    private void OnStructureRequested(object? sender, StructureRequestEventArgs e)
    {
        if (Document is null)
        {
            return;
        }

        switch (e.Request)
        {
            case StructureRequest.Split:
                e.Handled = HandleSplit(e.Node, e.CaretOffset);
                break;

            case StructureRequest.MergeWithPrevious:
                e.Handled = HandleMerge(e.Node);
                break;

            case StructureRequest.Indent:
                e.Handled = HandleIndent(e.Node, outdent: false);
                break;

            case StructureRequest.Outdent:
                e.Handled = HandleIndent(e.Node, outdent: true);
                break;

            case StructureRequest.NextBlock:
                e.Handled = MoveFocus(e.Node, 1);
                break;

            case StructureRequest.PreviousBlock:
                e.Handled = MoveFocus(e.Node, -1);
                break;

            case StructureRequest.DeleteForward:
                e.Handled = false;
                break;
        }
    }

    private bool HandleSplit(DitaNode node, int caretOffset)
    {
        if (Document is null)
        {
            return false;
        }

        // В шаге и элементе списка Enter создаёт следующий шаг/пункт.
        var target = node;
        if (node.Name == "cmd" && node.Parent is { Name: "step" or "substep" })
        {
            target = node.Parent;
        }

        BeforeStructuralEdit?.Invoke(this, "Разделение блока");

        DitaNode? created;
        if (ReferenceEquals(target, node))
        {
            created = EditCommands.SplitBlock(node, caretOffset);
            if (created is null)
            {
                return false;
            }
        }
        else
        {
            created = EditCommands.InsertAfter(target, target.Name);
            if (created is null)
            {
                return false;
            }

            created = created.FirstElement("cmd") ?? created;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(created, 0);
        return true;
    }

    private bool HandleMerge(DitaNode node)
    {
        if (Document is null)
        {
            return false;
        }

        var previous = EditCommands.PreviousElement(node);
        if (previous is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение блоков");

        var offset = previous.InnerText.Length;
        var merged = EditCommands.MergeWithPrevious(node);
        if (merged is null)
        {
            // Пустой блок просто удаляем.
            if (node.InnerText.Length == 0 && EditCommands.Delete(node))
            {
                Document.IsDirty = true;
                DocumentModified?.Invoke(this, EventArgs.Empty);
                Rebuild(previous, previous.InnerText.Length);
                return true;
            }

            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(merged, offset);
        return true;
    }

    private bool HandleIndent(DitaNode node, bool outdent)
    {
        if (Document is null)
        {
            return false;
        }

        var item = node.Name is "li" or "step" or "substep" ? node : node.Parent;
        if (item is null || item.Name is not ("li" or "step" or "substep"))
        {
            return false;
        }

        var list = item.Parent;
        if (list is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, outdent ? "Уменьшение уровня" : "Увеличение уровня");

        var applied = outdent ? OutdentItem(item, list) : IndentItem(item, list);
        if (!applied)
        {
            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        var focus = item.FirstElement("cmd") ?? item;
        Rebuild(focus, 0);
        return true;
    }

    /// <summary>Переносит <paramref name="item"/> в substeps/ul/ol предыдущего элемента списка.</summary>
    private bool IndentItem(DitaNode item, DitaNode list)
    {
        var previous = EditCommands.PreviousElement(item);
        if (previous is null)
        {
            return false;
        }

        var nestedName = list.Name switch
        {
            "steps" or "steps-unordered" => "substeps",
            "substeps" => "substeps",
            "ol" => "ol",
            _ => "ul"
        };

        var itemName = nestedName == "substeps" ? "substep" : "li";
        var nested = previous.ElementChildren().FirstOrDefault(c => c.Name == nestedName);
        if (nested is null)
        {
            nested = EditCommands.Append(previous, nestedName);
            if (nested is null)
            {
                return false;
            }

            foreach (var auto in nested.Children.ToList())
            {
                nested.Remove(auto);
            }
        }

        item.RemoveSelf();
        nested.Add(item);
        if (item.Name != itemName)
        {
            EditCommands.ChangeElementName(item, itemName);
        }

        return true;
    }

    /// <summary>Переносит <paramref name="item"/> из вложенного <paramref name="list"/> на уровень внешнего списка.</summary>
    private static bool OutdentItem(DitaNode item, DitaNode list)
    {
        var grandItem = list.Parent;
        if (grandItem is null || grandItem.Name is not ("li" or "step" or "substep"))
        {
            return false;
        }

        var outerList = grandItem.Parent;
        if (outerList is null)
        {
            return false;
        }

        var index = outerList.IndexOf(grandItem) + 1;
        item.RemoveSelf();

        var outerItemName = outerList.Name is "steps" or "steps-unordered" ? "step" : "li";
        outerList.Insert(index, item);
        if (item.Name != outerItemName)
        {
            EditCommands.ChangeElementName(item, outerItemName);
        }

        if (list.Children.Count == 0)
        {
            list.RemoveSelf();
        }

        return true;
    }

    private bool MoveFocus(DitaNode node, int direction)
    {
        if (!_editors.TryGetValue(node, out var editor))
        {
            return false;
        }

        var index = _order.IndexOf(editor) + direction;
        if (index < 0 || index >= _order.Count)
        {
            return false;
        }

        var next = _order[index];
        next.Focus();
        if (direction > 0)
        {
            next.PlaceCaretAt(0);
        }
        else
        {
            next.PlaceCaretAtEnd();
        }

        return true;
    }
}
