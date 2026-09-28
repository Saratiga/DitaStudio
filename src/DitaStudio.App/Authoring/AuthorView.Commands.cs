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

// Команды режима «Автор»: вставка, перемещение, удаление, объединение ячеек, пометки rev и track changes.
public sealed partial class AuthorView
{
    /// <summary>Сохраняет незаписанные правки текущего редактора в модель.</summary>
    public void FlushPendingEdits()
    {
        foreach (var editor in _order)
        {
            editor.Flush();
        }
    }

    public InlineEditor? EditorFor(DitaNode node) => _editors.TryGetValue(node, out var e) ? e : null;

    /// <summary>Вставляет элемент рядом с текущим или внутрь него.</summary>
    public bool InsertElement(string elementName)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit?.Invoke(this, $"Вставка <{elementName}>");

        var created = EditCommands.InsertAfter(CurrentNode, elementName);
        if (created is null && CurrentNode.Parent is not null)
        {
            created = EditCommands.Append(CurrentNode, elementName);
        }

        if (created is null)
        {
            var ancestor = CurrentNode.Parent;
            while (ancestor is not null && created is null)
            {
                created = EditCommands.InsertAfter(ancestor, elementName)
                          ?? EditCommands.Append(ancestor, elementName);
                ancestor = ancestor.Parent;
            }
        }

        if (created is null)
        {
            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(created), 0);
        return true;
    }

    private static DitaNode FirstEditable(DitaNode node)
    {
        foreach (var candidate in node.DescendantsAndSelf())
        {
            if (candidate.Kind != NodeKind.Element)
            {
                continue;
            }

            var def = DitaCatalog.Default.Get(candidate.Name);
            if (def is not null && def.IsMixed)
            {
                return candidate;
            }
        }

        return node;
    }

    public bool DeleteCurrent()
    {
        if (Document is null || CurrentNode is null || CurrentNode.Parent is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Удаление <{CurrentNode.Name}>");
        var parent = CurrentNode.Parent;
        var focus = EditCommands.PreviousElement(CurrentNode) ?? parent;
        EditCommands.Delete(CurrentNode);
        CurrentNode = null;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(focus), 0);
        return true;
    }

    public bool MoveCurrent(bool up)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, up ? "Перемещение вверх" : "Перемещение вниз");
        var moved = up ? EditCommands.MoveUp(CurrentNode) : EditCommands.MoveDown(CurrentNode);
        if (!moved)
        {
            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return true;
    }

    /// <summary>Объединяет выделенную ячейку CALS-таблицы со следующей в строке (colspan).</summary>
    public bool MergeCurrentCellRight()
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение ячеек по горизонтали");
        var merged = EditCommands.MergeTableCellRight(CurrentNode);
        if (merged is null)
        {
            return false;
        }

        CurrentNode = merged;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(merged), 0);
        return true;
    }

    /// <summary>Объединяет выделенную ячейку CALS-таблицы с той же колонкой строкой ниже (rowspan).</summary>
    public bool MergeCurrentCellDown()
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение ячеек по вертикали");
        var merged = EditCommands.MergeTableCellDown(CurrentNode);
        if (merged is null)
        {
            return false;
        }

        CurrentNode = merged;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(merged), 0);
        return true;
    }

    /// <summary>Переключает класс вывода (outputclass) на выделенном элементе — например,
    /// разрыв страницы перед заголовком при печати. Возвращает новое состояние (включён/выключён).</summary>
    public bool? ToggleCurrentOutputClass(string token)
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit?.Invoke(this, "Изменение оформления вывода");
        var enabled = EditCommands.ToggleOutputClassToken(CurrentNode, token);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return enabled;
    }

    /// <summary>Отмечает/снимает пометку изменения (атрибут rev) на текущем элементе — штатный
    /// DITA-механизм ревизий; при публикации рисуется полосой на полях (см. BuildClassAttr в
    /// HtmlRenderer). Не полноценный track changes — просто «здесь что-то менялось».</summary>
    public bool? ToggleCurrentRev()
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit?.Invoke(this, "Отметка изменения (rev)");
        var hasRev = !string.IsNullOrWhiteSpace(CurrentNode.GetAttribute("rev"));
        CurrentNode.SetAttribute("rev", hasRev ? null : "changed");
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return !hasRev;
    }

    /// <summary>Track changes: помечает текущий элемент как вставленный (status="new") — виден
    /// в предпросмотре подсвеченным, из итоговой публикации не исключается (это реальное
    /// содержимое, которое автор добавил). См. <see cref="TrackChanges"/>.</summary>
    public void MarkCurrentInserted()
    {
        if (Document is null || CurrentNode is null)
        {
            return;
        }

        BeforeStructuralEdit?.Invoke(this, "Пометка вставки (track changes)");
        TrackChanges.MarkInserted(CurrentNode, Environment.UserName);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
    }

    /// <summary>Track changes: помечает текущий элемент как удалённый (status="deleted") — узел
    /// физически остаётся в документе (зачёркнутым в предпросмотре) до Accept/Reject, из итоговой
    /// публикации исключается сразу. См. <see cref="TrackChanges"/>.</summary>
    public void MarkCurrentDeleted()
    {
        if (Document is null || CurrentNode is null)
        {
            return;
        }

        BeforeStructuralEdit?.Invoke(this, "Пометка удаления (track changes)");
        TrackChanges.MarkDeleted(CurrentNode, Environment.UserName);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
    }

    /// <summary>Принимает track changes-правку на текущем элементе: вставка остаётся без пометки,
    /// удаление физически убирается из документа.</summary>
    public void AcceptCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is null || !TrackChanges.IsTracked(CurrentNode))
        {
            return;
        }

        BeforeStructuralEdit?.Invoke(this, "Принятие правки (track changes)");
        var node = CurrentNode;
        var wasDeleted = TrackChanges.IsDeleted(node);
        TrackChanges.Accept(node);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(wasDeleted ? null : FirstEditable(node), 0);
    }

    /// <summary>Отклоняет track changes-правку на текущем элементе: вставка убирается из
    /// документа, удаление восстанавливается без пометки.</summary>
    public void RejectCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is null || !TrackChanges.IsTracked(CurrentNode))
        {
            return;
        }

        BeforeStructuralEdit?.Invoke(this, "Отклонение правки (track changes)");
        var node = CurrentNode;
        var wasInserted = TrackChanges.IsInserted(node);
        TrackChanges.Reject(node);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(wasInserted ? null : FirstEditable(node), 0);
    }

    public bool WrapCurrentInline(string elementName)
    {
        if (CurrentNode is null || !_editors.TryGetValue(CurrentNode, out var editor))
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Оформление <{elementName}>");
        editor.WrapSelection(elementName);
        return true;
    }

    public bool InsertInlineNode(DitaNode node)
    {
        if (CurrentNode is null || !_editors.TryGetValue(CurrentNode, out var editor))
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Вставка <{node.Name}>");
        editor.InsertInlineNode(node);
        return true;
    }
}
