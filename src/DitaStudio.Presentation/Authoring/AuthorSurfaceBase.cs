using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
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

    /// <summary>
    /// После правки: документ изменён, представление обновляется с курсором в <paramref name="focus"/>.
    /// <paramref name="parent"/> — элемент, у которого менялись дети (null — неизвестно, перестроить
    /// всё), <paramref name="changed"/> — его дети с изменённым содержимым или атрибутами;
    /// добавленные, удалённые и переставленные дети определяются по самому родителю.
    /// </summary>
    protected abstract void AfterStructuralEdit(DitaNode? focus, DitaNode? parent, IReadOnlyList<DitaNode> changed);

    /// <summary>Записывает в модель незаписанный ввод редактора (если есть).</summary>
    public virtual void FlushPendingEdits()
    {
    }

    public abstract void Rebuild();

    public abstract bool WrapCurrentInline(string element);

    public abstract bool InsertInlineNode(DitaNode node);

    /// <summary>
    /// Размер или цвет выделенного текста: весь текст блока — классом на самом блоке, часть —
    /// фразой ph с классом, без выделения — заготовкой у курсора. false — оболочка так не умеет.
    /// </summary>
    public virtual bool ApplyTextFormat(string prefix, string? token) => false;

    /// <summary>
    /// Вставляет у курсора фразовый элемент с текстом: выделение оборачивается, иначе внутрь
    /// ставится выделенная заготовка <paramref name="placeholder"/>. false — оболочка так не умеет
    /// (тогда элемент вставляется блоком после текущего).
    /// </summary>
    public virtual bool InsertInlineElement(DitaNode element, string placeholder) => false;

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

    private void Changed(DitaNode? focus, DitaNode? parent, params DitaNode[] changed)
    {
        Document!.IsDirty = true;
        AfterStructuralEdit(focus, parent, changed);
    }

    public bool InsertElement(string name)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        // Фразовый элемент (сноска, термин, картинка) — в строку у курсора, а не блоком после абзаца.
        if (TryInsertInline(CurrentNode, name))
        {
            return true;
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

        Changed(FirstEditable(created), created.Parent);
        return true;
    }

    private bool TryInsertInline(DitaNode current, string name)
    {
        var catalog = DitaCatalog.Default;
        if (catalog.Get(name) is not { Display: DisplayKind.Inline or DisplayKind.Empty } def ||
            catalog.Get(current.Name) is not { IsMixed: true } ||
            !catalog.CanInsert(current, name, current.ElementChildren().Count()))
        {
            return false;
        }

        var element = catalog.CreateElement(name);
        return def.IsMixed && element.Children.Count == 0
            ? InsertInlineElement(element, PlaceholderFor(def))
            : InsertInlineNode(element);
    }

    /// <summary>Заготовка текста нового фразового элемента — выделена, набор её заменяет.</summary>
    public static string PlaceholderFor(ElementDef def) => def.Name switch
    {
        "fn" => "Текст сноски",
        _ when def.Description.Length is > 0 and <= 40 => def.Description,
        _ => "текст"
    };

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
        Changed(FirstEditable(focus), parent);
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

        Changed(FirstEditable(CurrentNode), CurrentNode.Parent);
        return true;
    }

    public bool MergeCurrentCellRight() => MergeCell(EditCommands.MergeTableCellRight, "Объединение ячеек по горизонтали");

    public bool MergeCurrentCellDown() => MergeCell(EditCommands.MergeTableCellDown, "Объединение ячеек по вертикали");

    public bool EditCurrentTable(TableOperation operation)
    {
        if (Document is null || CurrentNode is null || TableCommands.CellOf(CurrentNode) is not { } cell ||
            TableCommands.TableOf(cell) is not { Parent: { } holder } table)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit(TableCommands.Describe(operation));
        if (TableCommands.Apply(cell, operation) is not { } focus)
        {
            return false;
        }

        CurrentNode = focus;
        Changed(FirstEditable(focus), holder, table);
        return true;
    }

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
        Changed(FirstEditable(merged), merged.Parent, merged);
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
        Changed(FirstEditable(CurrentNode), CurrentNode.Parent, CurrentNode);
        return enabled;
    }

    /// <summary>
    /// Оформление текущего блока (абзац, заголовок, ячейка): ставит класс группы
    /// <paramref name="prefix"/> вместо прежнего или снимает (null). Выравнивание ячейки CALS —
    /// стандартным @align.
    /// </summary>
    public bool SetCurrentBlockFormat(string prefix, string? token)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Оформление блока");
        var node = CurrentNode;
        if (prefix == TextFormatting.AlignPrefix && node.Name == "entry")
        {
            var value = TextFormatting.Alignments.FirstOrDefault(a => a.Token == token).Css;
            if (value is null)
            {
                node.RemoveAttribute("align");
            }
            else
            {
                node.SetAttribute("align", value);
            }

            TextFormatting.SetToken(node, prefix, null);
        }
        else
        {
            TextFormatting.SetToken(node, prefix, token);
        }

        Changed(FirstEditable(node), node.Parent, node);
        return true;
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
        Changed(FirstEditable(CurrentNode), CurrentNode.Parent, CurrentNode);
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
        Changed(FirstEditable(CurrentNode), CurrentNode.Parent, CurrentNode);
    }

    public void AcceptCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is not { } node || !TrackChanges.IsTracked(node))
        {
            return;
        }

        BeforeStructuralEdit("Принятие правки (track changes)");
        var wasDeleted = TrackChanges.IsDeleted(node);
        var parent = node.Parent;
        TrackChanges.Accept(node);
        Changed(wasDeleted ? null : FirstEditable(node), parent, node);
    }

    public void RejectCurrentTrackedChange()
    {
        if (Document is null || CurrentNode is not { } node || !TrackChanges.IsTracked(node))
        {
            return;
        }

        BeforeStructuralEdit("Отклонение правки (track changes)");
        var wasInserted = TrackChanges.IsInserted(node);
        var parent = node.Parent;
        TrackChanges.Reject(node);
        Changed(wasInserted ? null : FirstEditable(node), parent, node);
    }
}
