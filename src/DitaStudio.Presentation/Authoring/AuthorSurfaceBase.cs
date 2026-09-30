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

        var created = PlaceBlock(CurrentNode, name);
        if (created is null)
        {
            return false;
        }

        Changed(FirstEditable(created), created.Parent);
        return true;
    }

    /// <summary>Ставит новый блочный элемент после <paramref name="current"/>, а если там нельзя — в конец
    /// ближайшего родителя, куда он допустим. null — нигде нельзя.</summary>
    private static DitaNode? PlaceBlock(DitaNode current, string name)
    {
        var created = EditCommands.InsertAfter(current, name);
        if (created is null && current.Parent is not null)
        {
            created = EditCommands.Append(current, name);
        }

        var ancestor = current.Parent;
        while (created is null && ancestor is not null)
        {
            created = EditCommands.InsertAfter(ancestor, name) ?? EditCommands.Append(ancestor, name);
            ancestor = ancestor.Parent;
        }

        return created;
    }

    /// <summary>Заготовка названия нового рисунка — сразу видна и выделена для замены.</summary>
    public const string FigureTitlePlaceholder = "Название рисунка";

    /// <summary>
    /// Вставляет рисунок: блок <c>fig</c> с названием и изображением после текущего блока (подпись
    /// «Рисунок N. Название» получается при публикации). false — сюда рисунок не поставить, тогда
    /// вызывающий может вставить изображение в строку.
    /// </summary>
    public bool InsertFigure(DitaNode image)
    {
        if (Document is null || CurrentNode is null || DitaCatalog.Default.Get("fig") is null)
        {
            return false;
        }

        // Заранее: если fig нигде не допустим, отмена и перерисовка не нужны.
        if (!CanPlaceBlock(CurrentNode, "fig"))
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Вставка рисунка");
        var figure = PlaceBlock(CurrentNode, "fig");
        if (figure is null)
        {
            return false;
        }

        FillFigure(figure, image);
        Changed(figure.FirstElement("title") ?? figure, figure.Parent);
        return true;
    }

    private static bool CanPlaceBlock(DitaNode current, string name)
    {
        var catalog = DitaCatalog.Default;
        for (var node = current; node.Parent is { } parent; node = parent)
        {
            var index = parent.ElementChildren().TakeWhile(child => !ReferenceEquals(child, node)).Count() + 1;
            if (catalog.CanInsert(parent, name, index) || catalog.CanInsert(parent, name, parent.ElementChildren().Count()))
            {
                return true;
            }
        }

        return false;
    }

    private static void FillFigure(DitaNode figure, DitaNode image)
    {
        var title = figure.FirstElement("title");
        if (title is null)
        {
            title = DitaNode.Element("title");
            figure.Insert(0, title);
        }

        if (string.IsNullOrWhiteSpace(title.InnerText))
        {
            title.SetText(FigureTitlePlaceholder);
        }

        figure.Add(image);
    }

    /// <summary>Таблица (table, simpletable…), в которой стоит курсор, или null.</summary>
    protected DitaNode? CurrentTable()
    {
        for (var node = CurrentNode; node is not null; node = node.Parent)
        {
            if (node.Name is "table" or "simpletable" or "properties" or "choicetable")
            {
                return node;
            }
        }

        return null;
    }

    public virtual bool SelectCurrentTable() => false;

    private DitaNode? CurrentCalsTable() => CurrentTable() is { Name: "table" } table ? table : null;

    /// <summary>Границы всей таблицы CALS: frame, rowsep и colsep (см. <see cref="CalsBorders"/>).</summary>
    public bool SetTableBorders(TableBorderMode mode)
    {
        if (Document is null || CurrentCalsTable() is not { } table)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Границы таблицы");
        CalsBorders.SetMode(table, mode);
        Changed(FirstEditable(table), table.Parent, table);
        return true;
    }

    /// <summary>Линия под строкой таблицы под курсором.</summary>
    public bool SetRowBorder(bool visible)
    {
        var row = CurrentNode;
        while (row is not null && row.Name != "row")
        {
            row = row.Parent;
        }

        if (Document is null || row is null || CurrentCalsTable() is not { } table)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Линия под строкой");
        CalsBorders.SetRowSeparator(row, visible);
        Changed(FirstEditable(row), table.Parent, table);
        return true;
    }

    /// <summary>Линия справа от столбца таблицы под курсором (колонка — по ячейке с курсором).</summary>
    public bool SetColumnBorder(bool visible)
    {
        var entry = CurrentNode;
        while (entry is not null && entry.Name != "entry")
        {
            entry = entry.Parent;
        }

        if (Document is null || entry is null || CurrentCalsTable() is not { } table || entry.Closest("tgroup") is not { } tgroup)
        {
            return false;
        }

        var column = CalsBorders.LastColumnOf(entry);
        if (column < 0)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Линия справа от столбца");
        CalsBorders.SetColumnSeparator(tgroup, column, visible);
        Changed(FirstEditable(entry), table.Parent, table);
        return true;
    }

    /// <summary>Удаляет таблицу под курсором целиком (один шаг отмены).</summary>
    public bool DeleteCurrentTable()
    {
        if (CurrentTable() is not { } table)
        {
            return false;
        }

        CurrentNode = table;
        return DeleteCurrent();
    }

    /// <summary>Подпись рисунка или таблицы: есть <c>title</c> — убрать, нет — добавить пустой (см. <see cref="IAuthorSurface.ToggleCaption"/>).</summary>
    public bool? ToggleCaption()
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        var captioned = CurrentNode;
        while (captioned is not null && captioned.Name is not ("fig" or "table"))
        {
            captioned = captioned.Parent;
        }

        if (captioned is null)
        {
            return null;
        }

        FlushPendingEdits();
        BeforeStructuralEdit(captioned.FirstElement("title") is null ? "Подпись: добавить" : "Подпись: убрать");
        if (captioned.FirstElement("title") is { } existing)
        {
            var focus = FirstEditable(captioned);
            existing.RemoveSelf();
            Changed(focus, captioned.Parent, captioned);
            return false;
        }

        var title = DitaNode.Element("title");
        captioned.Insert(0, title);
        Changed(title, captioned.Parent, captioned);
        return true;
    }

    /// <summary>
    /// «Оформить как рисунок»: изображение из абзаца под курсором выносится в блок <c>fig</c> с названием,
    /// который встаёт сразу после абзаца; пустой после этого абзац убирается. false — нечего оформлять
    /// (в блоке нет изображения, оно уже в <c>fig</c>) или <c>fig</c> здесь недопустим.
    /// </summary>
    public bool WrapImageAsFigure()
    {
        if (Document is null || CurrentNode is not { Parent: { } parent } paragraph ||
            paragraph.ElementChildren().FirstOrDefault(child => child.Name == "image") is not { } image ||
            parent.Name == "fig")
        {
            return false;
        }

        var index = parent.ElementChildren().TakeWhile(child => !ReferenceEquals(child, paragraph)).Count() + 1;
        if (!DitaCatalog.Default.CanInsert(parent, "fig", index))
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Оформление изображения как рисунка");
        var figure = DitaCatalog.Default.CreateElement("fig");
        image.RemoveSelf();
        image.RemoveAttribute("placement");
        FillFigure(figure, image);
        parent.Insert(parent.IndexOf(paragraph) + 1, figure);

        var empty = string.IsNullOrWhiteSpace(paragraph.InnerText) && !paragraph.ElementChildren().Any();
        if (empty)
        {
            paragraph.RemoveSelf();
        }

        Changed(figure.FirstElement("title") ?? figure, parent);
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

        var node = CurrentNode;
        if (prefix == PagePlacement.Prefix)
        {
            // Положение на листе — у всего блока: курсор в подписи рисунка или ячейке таблицы
            // ставит на лист рисунок или таблицу целиком.
            if (PagePlacement.PlaceableFor(node) is not { } block)
            {
                return false;
            }

            node = block;
        }

        FlushPendingEdits();
        BeforeStructuralEdit("Оформление блока");
        if (prefix == PagePlacement.Prefix)
        {
            PagePlacement.Set(node, token);
        }
        else if (prefix == TextFormatting.AlignPrefix && node.Name == "entry")
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
