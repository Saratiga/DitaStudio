using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.Authoring;

/// <summary>
/// Структурные операции режима «Автор», которые меняют только модель (вставка, удаление,
/// перемещение, объединение ячеек, outputclass, rev, track changes) — общие для любой оболочки.
/// Наследник даёт документ, текущий узел, точку отмены и перерисовку; операции над выделенным
/// текстом (<see cref="WrapCurrentInline"/>, <see cref="InsertInlineNode"/>) — его забота.
/// Логика перенесена из WPF AuthorView.Commands без изменений.
/// </summary>
public abstract class AuthorSurfaceBase : IAuthorSurface
{
    public abstract DitaDocument? Document { get; }

    public abstract DitaNode? CurrentNode { get; protected set; }

    public bool ShowElementTags { get; set; } = true;

    /// <summary>Перед правкой — снимок для отмены (<paramref name="description"/> — её название).</summary>
    protected abstract void BeforeStructuralEdit(string description);

    /// <summary>После правки: документ изменён, представление перестраивается с курсором в <paramref name="focus"/>.</summary>
    protected abstract void AfterStructuralEdit(DitaNode? focus);

    /// <summary>Записывает в модель незаписанный ввод редактора (если есть).</summary>
    public virtual void FlushPendingEdits()
    {
    }

    public abstract void Rebuild();

    public abstract bool WrapCurrentInline(string element);

    public abstract bool InsertInlineNode(DitaNode node);

    /// <summary>Первый элемент со смешанным содержимым внутри узла — куда ставить курсор после правки.</summary>
    protected static DitaNode FirstEditable(DitaNode node)
    {
        foreach (var candidate in node.DescendantsAndSelf())
        {
            if (candidate.Kind == NodeKind.Element && DitaCatalog.Default.Get(candidate.Name) is { IsMixed: true })
            {
                return candidate;
            }
        }

        return node;
    }

    private void Changed(DitaNode? focus)
    {
        Document!.IsDirty = true;
        AfterStructuralEdit(focus);
    }

    public bool InsertElement(string name)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit($"Вставка <{name}>");

        var created = EditCommands.InsertAfter(CurrentNode, name);
        if (created is null && CurrentNode.Parent is not null)
        {
            created = EditCommands.Append(CurrentNode, name);
        }

        var ancestor = CurrentNode.Parent;
        while (created is null && ancestor is not null)
        {
            created = EditCommands.InsertAfter(ancestor, name) ?? EditCommands.Append(ancestor, name);
            ancestor = ancestor.Parent;
        }

        if (created is null)
        {
            return false;
        }

        Changed(FirstEditable(created));
        return true;
    }

    public bool DeleteCurrent()
    {
        if (Document is null || CurrentNode?.Parent is not { } parent)
        {
            return false;
        }

        BeforeStructuralEdit($"Удаление <{CurrentNode.Name}>");
        var focus = EditCommands.PreviousElement(CurrentNode) ?? parent;
        EditCommands.Delete(CurrentNode);
        CurrentNode = null;
        Changed(FirstEditable(focus));
        return true;
    }

    public bool MoveCurrent(bool up)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit(up ? "Перемещение вверх" : "Перемещение вниз");
        if (!(up ? EditCommands.MoveUp(CurrentNode) : EditCommands.MoveDown(CurrentNode)))
        {
            return false;
        }

        Changed(FirstEditable(CurrentNode));
        return true;
    }

    public bool MergeCurrentCellRight() => MergeCell(EditCommands.MergeTableCellRight, "Объединение ячеек по горизонтали");

    public bool MergeCurrentCellDown() => MergeCell(EditCommands.MergeTableCellDown, "Объединение ячеек по вертикали");

    private bool MergeCell(Func<DitaNode, DitaNode?> merge, string description)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit(description);
        if (merge(CurrentNode) is not { } merged)
        {
            return false;
        }

        CurrentNode = merged;
        Changed(FirstEditable(merged));
        return true;
    }

    public bool? ToggleCurrentOutputClass(string className)
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit("Изменение оформления вывода");
        var enabled = EditCommands.ToggleOutputClassToken(CurrentNode, className);
        Changed(FirstEditable(CurrentNode));
        return enabled;
    }

    public bool? ToggleCurrentRev()
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit("Отметка изменения (rev)");
        var hasRev = !string.IsNullOrWhiteSpace(CurrentNode.GetAttribute("rev"));
        CurrentNode.SetAttribute("rev", hasRev ? null : "changed");
        Changed(FirstEditable(CurrentNode));
        return !hasRev;
    }

    public void MarkCurrentInserted() => Track("Пометка вставки (track changes)", node => TrackChanges.MarkInserted(node, Environment.UserName));

    public void MarkCurrentDeleted() => Track("Пометка удаления (track changes)", node => TrackChanges.MarkDeleted(node, Environment.UserName));

    private void Track(string description, Action<DitaNode> mark)
    {
        if (Document is null || CurrentNode is null)
        {
            return;
        }

        BeforeStructuralEdit(description);
        mark(CurrentNode);
        Changed(FirstEditable(CurrentNode));
    }

    public void AcceptCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is not { } node || !TrackChanges.IsTracked(node))
        {
            return;
        }

        BeforeStructuralEdit("Принятие правки (track changes)");
        var wasDeleted = TrackChanges.IsDeleted(node);
        TrackChanges.Accept(node);
        Changed(wasDeleted ? null : FirstEditable(node));
    }

    public void RejectCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is not { } node || !TrackChanges.IsTracked(node))
        {
            return;
        }

        BeforeStructuralEdit("Отклонение правки (track changes)");
        var wasInserted = TrackChanges.IsInserted(node);
        TrackChanges.Reject(node);
        Changed(wasInserted ? null : FirstEditable(node));
    }
}
