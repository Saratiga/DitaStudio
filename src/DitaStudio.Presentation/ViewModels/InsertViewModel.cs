using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Вставка элементов (абзац, список, таблица, изображение, ссылка, сноска),
// инлайн-форматирование, перестановка/удаление элемента, объединение ячеек
// таблицы, переключатели печати. Много однотипных команд, но без
// императивного построения WPF-дерева (BuildTableNode строит DitaNode —
// модель, не визуальное дерево) — риск ниже, чем размер файла намекает.
public partial class InsertViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly IDocumentHost _docs;

    [ObservableProperty]
    private bool showElementTags = true;

    public InsertViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _docs = docs;
    }

    // [RelayCommand] — не только для InsertParagraph/InsertUl/и т.п. ниже,
    // но и для палитры вставки (MainWindow.SidePanels.cs, не мигрирована),
    // которая вызывает произвольное имя элемента из каталога по double-click.
    [RelayCommand]
    private void InsertElement(string name)
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            return;
        }

        pane.Mode = EditorMode.Author;
        if (!pane.Author.InsertElement(name))
        {
            _shell.StatusText = $"Элемент <{name}> здесь недопустим.";
            return;
        }

        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        _shell.StatusText = $"Вставлен <{name}>";
    }

    /// <summary>
    /// Что можно вставить у курсора текущей вкладки: фразовые элементы — в строку текущего блока,
    /// блочные — после него (как это делает <see cref="InsertElementCommand"/>). По описанию.
    /// </summary>
    public (IReadOnlyList<ElementDef> Inline, IReadOnlyList<ElementDef> After) InsertCandidates()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            return (Array.Empty<ElementDef>(), Array.Empty<ElementDef>());
        }

        var catalog = DitaCatalog.Default;
        var inline = catalog.Get(node.Name) is { IsMixed: true }
            ? catalog.InsertableAt(node, DitaCatalog.ChildNames(node).Count)
                .Where(d => d.Display is DisplayKind.Inline or DisplayKind.Empty)
                .ToList()
            : new List<ElementDef>();

        var after = node.Parent is { } parent
            ? catalog.InsertableAt(parent, EditCommands.ElementIndexOf(parent, node) + 1)
                .Where(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty))
                .ToList()
            : new List<ElementDef>();

        static List<ElementDef> Sorted(IEnumerable<ElementDef> defs) =>
            defs.DistinctBy(d => d.Name)
                .OrderBy(d => string.IsNullOrEmpty(d.Description) ? d.Name : d.Description, StringComparer.CurrentCulture)
                .ToList();

        return (Sorted(inline), Sorted(after));
    }

    [RelayCommand]
    private void InsertParagraph() => InsertElement("p");

    [RelayCommand]
    private void InsertSection() => InsertElement("section");

    [RelayCommand]
    private void InsertUl() => InsertElement("ul");

    [RelayCommand]
    private void InsertOl() => InsertElement("ol");

    [RelayCommand]
    private void InsertNote() => InsertElement("note");

    [RelayCommand]
    private void InsertCodeblock() => InsertElement("codeblock");

    [RelayCommand]
    private void InsertFootnote() => InsertElement("fn");

    [RelayCommand]
    private async Task InsertTable()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node?.Parent is null)
        {
            return;
        }

        var options = await _ui.Dialogs.InsertTableAsync();
        if (options is null)
        {
            return;
        }

        pane.PushUndo("Вставка таблицы");
        var table = BuildTableNode(options);

        var parent = node.Parent;
        var index = EditCommands.ElementIndexOf(parent, node) + 1;
        if (!DitaCatalog.Default.CanInsert(parent, "table", index))
        {
            _shell.StatusText = "Таблицу здесь вставить нельзя.";
            return;
        }

        parent.Insert(EditCommands.ChildIndexForElementIndex(parent, index), table);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
    }

    private static DitaNode BuildTableNode(TableResult options)
    {
        var table = DitaNode.Element("table");
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            var title = DitaNode.Element("title");
            title.SetText(options.Title);
            table.Add(title);
        }

        var tgroup = DitaNode.Element("tgroup");
        tgroup.SetAttribute("cols", options.Columns.ToString());
        table.Add(tgroup);

        for (var c = 1; c <= options.Columns; c++)
        {
            var colspec = DitaNode.Element("colspec");
            colspec.SetAttribute("colname", "c" + c);
            colspec.SetAttribute("colnum", c.ToString());
            colspec.SetAttribute("colwidth", "1*");
            tgroup.Add(colspec);
        }

        if (options.Header)
        {
            var thead = DitaNode.Element("thead");
            var row = DitaNode.Element("row");
            for (var c = 1; c <= options.Columns; c++)
            {
                var entry = DitaNode.Element("entry");
                entry.SetAttribute("colname", "c" + c);
                row.Add(entry);
            }

            thead.Add(row);
            tgroup.Add(thead);
        }

        var tbody = DitaNode.Element("tbody");
        for (var r = 0; r < options.Rows; r++)
        {
            var row = DitaNode.Element("row");
            for (var c = 1; c <= options.Columns; c++)
            {
                var entry = DitaNode.Element("entry");
                entry.SetAttribute("colname", "c" + c);
                row.Add(entry);
            }

            tbody.Add(row);
        }

        tgroup.Add(tbody);
        return table;
    }

    [RelayCommand]
    private async Task InsertImage()
    {
        var pane = _docs.Current;
        if (pane is null || pane.FilePath is null)
        {
            return;
        }

        var file = await _ui.Files.OpenFileAsync("Выберите изображение",
            new[] { new FileFilter("Изображения", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.bmp"), FileFilter.All },
            Path.GetDirectoryName(pane.FilePath));
        if (file is null)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, file);
        var image = DitaNode.Element("image");
        image.SetAttribute("href", href);

        var alt = DitaNode.Element("alt");
        alt.SetText(Path.GetFileNameWithoutExtension(file));
        image.Add(alt);

        // Рисунок — блок fig с названием: при публикации у него подпись «Рисунок N. Название».
        // Там, где fig недопустим (например, в середине заголовка), изображение идёт в строку.
        if (pane.Author.InsertFigure(image))
        {
            _shell.StatusText = "Рисунок вставлен: замените название под ним. Подпись «Рисунок N» появится при публикации.";
            _docs.RefreshAllTabTitles();
            return;
        }

        image.SetAttribute("placement", "break");
        if (!pane.Author.InsertInlineNode(image))
        {
            _shell.StatusText = "Поставьте курсор в абзац, куда вставить изображение.";
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    /// <summary>Границы таблицы под курсором: все, внешняя рамка, горизонтальные, без границ.</summary>
    [RelayCommand]
    private void SetTableBorders(TableBorderMode mode)
    {
        var ok = _docs.Current?.Author.SetTableBorders(mode) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok
            ? mode switch
            {
                TableBorderMode.All => "Границы таблицы: все линии.",
                TableBorderMode.OuterOnly => "Границы таблицы: только внешняя рамка.",
                TableBorderMode.HorizontalOnly => "Границы таблицы: только горизонтальные линии.",
                _ => "Границы таблицы убраны."
            }
            : "Поставьте курсор в ячейку таблицы (обычной, CALS).";
    }

    /// <summary>Линия под строкой таблицы, в которой стоит курсор: true — показать, false — убрать.</summary>
    [RelayCommand]
    private void SetRowBorder(bool visible)
    {
        var ok = _docs.Current?.Author.SetRowBorder(visible) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok ? (visible ? "Линия под строкой показана." : "Линия под строкой убрана.") : "Поставьте курсор в ячейку таблицы.";
    }

    /// <summary>Линия справа от столбца, в котором стоит курсор: true — показать, false — убрать.</summary>
    [RelayCommand]
    private void SetColumnBorder(bool visible)
    {
        var ok = _docs.Current?.Author.SetColumnBorder(visible) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok ? (visible ? "Линия справа от столбца показана." : "Линия справа от столбца убрана.") : "Поставьте курсор в ячейку таблицы.";
    }

    /// <summary>Выделить целиком таблицу под курсором (контур; затем Delete удаляет её).</summary>
    [RelayCommand]
    private void SelectTable()
    {
        _shell.StatusText = _docs.Current?.Author.SelectCurrentTable() == true
            ? "Таблица выделена: Delete удаляет её целиком, Esc снимает выделение."
            : "Поставьте курсор в ячейку таблицы.";
    }

    /// <summary>Удалить таблицу под курсором целиком.</summary>
    [RelayCommand]
    private void DeleteTable()
    {
        if (_docs.Current?.Author.DeleteCurrentTable() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
            _shell.StatusText = "Таблица удалена (отмена — Ctrl+Alt+Z).";
        }
        else
        {
            _shell.StatusText = "Поставьте курсор в ячейку таблицы.";
        }
    }

    /// <summary>Подпись рисунка или таблицы под курсором: добавить пустую («Рисунок N») или убрать совсем.</summary>
    [RelayCommand]
    private void ToggleCaption()
    {
        if (_docs.Current is not { } pane)
        {
            return;
        }

        switch (pane.Author.ToggleCaption())
        {
            case true:
                _shell.StatusText = "Подпись добавлена: при публикации — «Рисунок N» / «Таблица N»; впишите название в заголовок.";
                _docs.RefreshAllTabTitles();
                break;
            case false:
                _shell.StatusText = "Подпись убрана: у этого рисунка (таблицы) подписи и номера не будет.";
                _docs.RefreshAllTabTitles();
                break;
            default:
                _shell.StatusText = "Поставьте курсор в рисунок или таблицу, чтобы добавить или убрать подпись.";
                break;
        }
    }

    /// <summary>Изображение из абзаца под курсором — в рисунок с названием и номером.</summary>
    [RelayCommand]
    private void WrapImageAsFigure()
    {
        if (_docs.Current is not { } pane)
        {
            return;
        }

        if (!pane.Author.WrapImageAsFigure())
        {
            _shell.StatusText = "Поставьте курсор в абзац с изображением (вне рисунка), чтобы оформить его как рисунок.";
            return;
        }

        _shell.StatusText = "Изображение оформлено как рисунок: замените название под ним.";
        _docs.RefreshAllTabTitles();
    }

    [RelayCommand]
    private async Task InsertXref()
    {
        var pane = _docs.Current;
        var project = _workspace.Project;
        if (project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = await _ui.Dialogs.InsertXrefAsync(project, pane.FilePath);
        if (result is null)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, result.File.FullPath);
        if (!string.IsNullOrEmpty(result.TopicId))
        {
            href += "#" + result.TopicId;
        }

        var xref = DitaNode.Element("xref");
        xref.SetAttribute("href", href);
        xref.SetAttribute("format", "dita");
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            xref.SetText(result.Text);
        }

        if (!pane.Author.InsertInlineNode(xref))
        {
            _shell.StatusText = "Поставьте курсор в текст, куда вставить ссылку.";
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    private void Format(string element)
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            return;
        }

        if (!pane.Author.WrapCurrentInline(element))
        {
            _shell.StatusText = "Выделите текст в режиме «Автор».";
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    [RelayCommand]
    private void FormatBold() => Format("b");

    [RelayCommand]
    private void FormatItalic() => Format("i");

    [RelayCommand]
    private void FormatUnderline() => Format("u");

    [RelayCommand]
    private void FormatCode() => Format("codeph");

    [RelayCommand]
    private void FormatUicontrol() => Format("uicontrol");

    [RelayCommand]
    private void MoveUp() => MoveElement(true);

    [RelayCommand]
    private void MoveDown() => MoveElement(false);

    private void MoveElement(bool up)
    {
        if (_docs.Current?.Author.MoveCurrent(up) == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
        }
    }

    [RelayCommand]
    private async Task DeleteElement()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!await _ui.Dialogs.ConfirmAsync("Удаление", $"Удалить элемент <{node.Name}> вместе с содержимым?"))
        {
            return;
        }

        if (_docs.Current?.Author.DeleteCurrent() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
        }
    }

    [RelayCommand]
    private async Task RenameId()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        var project = _workspace.Project;
        if (project is null || pane?.FilePath is null || node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var oldId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(oldId))
        {
            _shell.StatusText = "У элемента нет id — задайте его в панели «Атрибуты», затем переименовывайте.";
            return;
        }

        var newId = await _ui.Dialogs.RenameIdAsync(oldId!);
        if (string.IsNullOrWhiteSpace(newId) || newId == oldId)
        {
            return;
        }

        foreach (var p in _docs.Panes.Values)
        {
            p.CommitPendingEdits();
        }

        var result = RefactorService.RenameId(project, pane.FilePath, oldId!, newId!);
        _docs.ApplyRefactorResult(result);
        _shell.StatusText = $"id «{oldId}» переименован в «{newId}». Обновлено ссылок: {result.UpdatedReferences}.";
    }

    [RelayCommand]
    private async Task ExtractToConref()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        var project = _workspace.Project;
        if (project is null || pane?.FilePath is null || node is null || node.Parent is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var suggestedId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(suggestedId))
        {
            suggestedId = DocumentTemplates.SuggestId(node.InnerText, node.Name);
        }

        var dialogResult = await _ui.Dialogs.ExtractToConrefAsync(project, suggestedId!);
        if (dialogResult is null)
        {
            return;
        }

        foreach (var p in _docs.Panes.Values)
        {
            p.CommitPendingEdits();
        }

        RefactorResult result;
        try
        {
            result = RefactorService.ExtractToConref(
                project, pane.FilePath, node, dialogResult.ElementId,
                dialogResult.TargetFile?.FullPath, dialogResult.NewFileName);
        }
        catch (IOException ex)
        {
            await _ui.Dialogs.MessageAsync("Вынесение в conref", ex.Message);
            return;
        }

        if (result.UpdatedReferences == 0)
        {
            await _ui.Dialogs.MessageAsync("Вынесение в conref", "Не удалось перенести элемент — проверьте цель.");
            return;
        }

        _docs.ApplyRefactorResult(result);
        if (dialogResult.TargetFile is null)
        {
            project.Scan();
            _hooks.RefreshProjectTree?.Invoke();
        }

        _shell.StatusText = $"Элемент вынесен в conref (id «{dialogResult.ElementId}»).";
    }

    /// <summary>Строки и столбцы таблицы под курсором (меню ячейки, панель инструментов).</summary>
    [RelayCommand]
    private void EditTable(TableOperation operation)
    {
        if (_docs.Current?.Author.EditCurrentTable(operation) == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _hooks.RefreshOutline?.Invoke();
            _shell.StatusText = TableCommands.Describe(operation) + " — выполнено.";
        }
        else
        {
            _shell.StatusText = operation switch
            {
                TableOperation.DeleteRow => "Строку удалить нельзя: в таблице должна остаться хотя бы одна строка.",
                TableOperation.DeleteColumn => "Столбец удалить нельзя: он последний или число столбцов задано типом таблицы.",
                TableOperation.SplitCell => "Ячейка не объединена — делить нечего.",
                TableOperation.InsertColumnLeft or TableOperation.InsertColumnRight => "В этой таблице число столбцов задано её типом.",
                _ => "Поставьте курсор в ячейку таблицы."
            };
        }
    }

    /// <summary>
    /// Границы выделенных ячеек (или ячейки под курсором) как в меню «Границы» Word: выбранные стороны показываются, а если все уже есть —
    /// убираются; «Нет границы» убирает все.
    /// </summary>
    [RelayCommand]
    private void SetCellBorders(DitaStudio.Core.Publishing.BorderEdges edges)
    {
        switch (_docs.Current?.Author.SetCellBorders(edges))
        {
            case null:
                _shell.StatusText = "Границы: поставьте курсор в ячейку или выделите ячейки обычной таблицы (CALS).";
                return;
            case false:
                _shell.StatusText = "Края таблицы заданы для всей стороны сразу: выделите всю строку или весь столбец. Линии между ячейками применены.";
                break;
            default:
                _shell.StatusText = "Границы ячеек изменены.";
                break;
        }

        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
    }

    /// <summary>Выделяет строку таблицы, в которой курсор (прямоугольник ячеек).</summary>
    [RelayCommand]
    private void SelectTableRow()
    {
        if (_docs.Current?.Author.SelectCurrentRow() != true)
        {
            _shell.StatusText = "Поставьте курсор в ячейку обычной таблицы.";
        }
    }

    /// <summary>Выделяет столбец таблицы, в котором курсор.</summary>
    [RelayCommand]
    private void SelectTableColumn()
    {
        if (_docs.Current?.Author.SelectCurrentColumn() != true)
        {
            _shell.StatusText = "Поставьте курсор в ячейку обычной таблицы.";
        }
    }

    [RelayCommand]
    private void MergeCellRight()
    {
        if (_docs.Current?.Author.MergeCurrentCellRight() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Ячейки объединены по горизонтали.";
        }
        else
        {
            _shell.StatusText = "Выделите ячейку таблицы, у которой есть соседняя справа.";
        }
    }

    [RelayCommand]
    private void MergeCellDown()
    {
        if (_docs.Current?.Author.MergeCurrentCellDown() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Ячейки объединены по вертикали.";
        }
        else
        {
            _shell.StatusText = "Выделите ячейку таблицы, у которой есть соседняя снизу.";
        }
    }

    [RelayCommand]
    private void TogglePageBreakBeforeTitle()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _shell.StatusText = "Выделите заголовок (title) — например, заголовок раздела или топика.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-before");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Разрыв страницы перед заголовком включён."
            : "Разрыв страницы перед заголовком выключен.";
    }

    /// <summary>Выравнивание текущего блока: align-left (по умолчанию — класс снимается), -center, -right, -justify.</summary>
    [RelayCommand]
    private void SetAlignment(string? token)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _shell.StatusText = "Поставьте курсор в абзац, заголовок или ячейку.";
            return;
        }

        var value = token is null or "align-left" ? null : token;
        if (author.SetCurrentBlockFormat(TextFormatting.AlignPrefix, value))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Выравнивание: " + TextFormatting.Alignments.First(a => a.Token == (value ?? "align-left")).Label.ToLowerInvariant() + ".";
        }
    }

    /// <summary>Размеры шрифта для списка на панели: «Обычный» и размеры в пт.</summary>
    public IReadOnlyList<string> FontSizes { get; } = new[] { NormalSize }.Concat(TextFormatting.Sizes.Select(s => s.ToString())).Append(CustomSize).ToList();

    /// <summary>Пункт списка размеров: спросить число пунктов (дробные — через точку или запятую).</summary>
    public const string CustomSize = "Другой…";

    public const string NormalSize = "Обычный";

    /// <summary>Размер шрифта выделения или дальнейшего набора: "10" (пт) или «Обычный»/null — снять.</summary>
    [RelayCommand]
    private async Task SetFontSize(string? size)
    {
        if (size == CustomSize)
        {
            var typed = await _ui.Dialogs.PromptTextAsync("Свой размер шрифта", "Размер, пт",
                (_docs.Current?.Author.CurrentNode is { } node && TextFormatting.SizeOf(node) is { } current ? current : 11).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                $"От {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт; дробные значения — через точку или запятую (например, 13,5).");
            if (typed is null)
            {
                return;
            }

            if (!double.TryParse(typed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var custom) ||
                custom < TextFormatting.MinCustomSize || custom > TextFormatting.MaxCustomSize)
            {
                _shell.StatusText = $"Размер должен быть числом от {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт.";
                return;
            }

            size = custom.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        var parsed = double.TryParse(size?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points);
        var token = parsed ? TextFormatting.SizeToken(points) : null;
        ApplyTextFormat(TextFormatting.SizePrefix, token, token is null ? "Размер шрифта снят." : $"Размер шрифта {TextFormatting.ParseSizeToken(token):0.#} пт.");
    }

    /// <summary>Цвет выделения или дальнейшего набора: color-red… или null — снять.</summary>
    [RelayCommand]
    private void SetTextColor(string? token)
    {
        var color = TextFormatting.Colors.FirstOrDefault(c => c.Token == token);
        ApplyTextFormat(TextFormatting.ColorPrefix, color.Token,
            color.Token is null ? "Цвет текста снят." : $"Цвет текста: {color.Label.ToLowerInvariant()}.");
    }

    /// <summary>
    /// Маркер, как «Цвет выделения текста» в Word: есть выделение — закрашивается сразу; нет — включается кисть этого цвета (дальше
    /// выделение мышью красит текст, пока не нажат <c>Esc</c> или тот же значок). Параметр: <c>mark-red</c>…, <c>null</c> — «Нет цвета»
    /// (с выделением снимает маркер, без — «ластик»), <c>custom</c> — выбрать свой цвет в окне.
    /// </summary>
    [RelayCommand]
    private async Task SetMarker(string? token)
    {
        var author = _docs.Current?.Author;
        if (author is null)
        {
            _shell.StatusText = "Откройте документ и выделите текст.";
            return;
        }

        if (token == CustomMarker)
        {
            var current = author.MarkerPenToken is { } pen ? TextFormatting.ParseMarkToken(pen) : null;
            var picked = await _ui.Dialogs.PickColorAsync("Цвет маркера", current);
            if (picked is null || TextFormatting.MarkToken(picked) is not { } custom)
            {
                return;
            }

            token = custom;
        }

        if (author.HasTextSelection)
        {
            ApplyTextFormat(TextFormatting.MarkPrefix, token, token is null ? "Маркер снят." : $"Маркер: {MarkerLabel(token)}.");
            return;
        }

        // Выделения нет — кисть: тот же цвет второй раз выключает её.
        if (author.MarkerPenActive && author.MarkerPenToken == token)
        {
            author.StopMarkerPen();
            _shell.StatusText = "Маркер выключен.";
            return;
        }

        if (author.StartMarkerPen(token))
        {
            _shell.StatusText = token is null
                ? "Режим маркера: ластик — выделите текст мышью, чтобы снять маркер. Esc — выключить."
                : $"Режим маркера: {MarkerLabel(token)} — выделяйте текст мышью, он закрашивается. Esc — выключить.";
        }
        else
        {
            _shell.StatusText = "Здесь маркер недоступен.";
        }
    }

    /// <summary>Параметр команды маркера «Другой цвет…».</summary>
    public const string CustomMarker = "custom";

    private static string MarkerLabel(string token) =>
        TextFormatting.Marks.FirstOrDefault(m => m.Token == token) is { Label: not null } named
            ? named.Label.ToLowerInvariant()
            : TextFormatting.ParseMarkToken(token) ?? token;

    private void ApplyTextFormat(string prefix, string? token, string done)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _shell.StatusText = "Поставьте курсор в текст или выделите его.";
            return;
        }

        if (author.ApplyTextFormat(prefix, token))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = done;
        }
        else
        {
            _shell.StatusText = "Здесь оформление текста недоступно.";
        }
    }

    /// <summary>
    /// Положение блока на отдельном листе PDF/DOCX: "place-bottom-right" и т. п. (<see cref="PagePlacement"/>),
    /// null — обычное, в тексте.
    /// </summary>
    [RelayCommand]
    private void SetPagePlacement(string? token)
    {
        var author = _docs.Current?.Author;
        if (author?.CurrentNode is null || PagePlacement.PlaceableFor(author.CurrentNode) is null)
        {
            _shell.StatusText = "Поставьте курсор в абзац, рисунок, таблицу или заметку прямо в тексте топика (не в списке).";
            return;
        }

        if (author.SetCurrentBlockFormat(PagePlacement.Prefix, token))
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = PagePlacement.LabelOf(token) is { } label
                ? $"Блок на отдельном листе PDF и DOCX: {label.ToLowerInvariant()}."
                : "Блок снова идёт в тексте.";
        }
    }

    /// <summary>Нумерованный абзац (пункт): номер по заголовкам — 2.3.1 (outputclass numbered).</summary>
    [RelayCommand]
    private void ToggleNumberedParagraph()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "p")
        {
            _shell.StatusText = "Поставьте курсор в абзац.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(HeadingNumbering.NumberedClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Абзац нумерованный: при публикации получит номер по заголовкам (например, 2.3.1)."
            : "Абзац больше не нумеруется.";
    }

    /// <summary>Заголовок «без номера»: не нумеруется и не попадает в оглавление (outputclass nonumber).</summary>
    [RelayCommand]
    private void ToggleUnnumberedTitle()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _shell.StatusText = "Поставьте курсор в заголовок топика или раздела.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass(TocRules.NoNumberClass);
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Заголовок без номера: при публикации не нумеруется и не попадает в оглавление."
            : "Заголовок снова нумеруется и попадает в оглавление.";
    }

    [RelayCommand]
    private void ToggleTablePageBreakAuto()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "table")
        {
            _shell.StatusText = "Выделите таблицу целиком (не отдельную ячейку).";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-auto");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Таблица теперь может переноситься на страницы с повтором шапки."
            : "Таблица снова печатается как единый блок.";
    }

    [RelayCommand]
    private void ToggleRevChanged()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно отметить как изменённый.";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentRev();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Элемент отмечен как изменённый (rev) — при публикации появится полоса на полях."
            : "Отметка об изменении снята.";
    }

    [RelayCommand]
    private void MarkTrackedInserted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно пометить как вставленный.";
            return;
        }

        _docs.Current!.Author.MarkCurrentInserted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Элемент помечен как вставленный (track changes).";
    }

    [RelayCommand]
    private void MarkTrackedDeleted()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент, который нужно пометить как удалённый.";
            return;
        }

        _docs.Current!.Author.MarkCurrentDeleted();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Элемент помечен как удалённый (track changes) — скрыт из публикации, виден зачёркнутым в предпросмотре.";
    }

    [RelayCommand]
    private void AcceptTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _docs.Current!.Author.AcceptCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Правка принята.";
    }

    [RelayCommand]
    private void RejectTrackedChange()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _shell.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _docs.Current!.Author.RejectCurrentTrackedChange();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = "Правка отклонена.";
    }

    /// <summary>Выполняет выбранную команду плагина (см. IAuthorCommandPlugin) на открытом
    /// документе — пункт меню один и тот же для любого числа подключённых плагинов.</summary>
    [RelayCommand]
    private async Task RunAuthorCommandPlugin()
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            await _ui.Dialogs.MessageAsync("Команда плагина", "Откройте документ.");
            return;
        }

        var command = await _ui.Dialogs.PickOneAsync("Команда плагина", "Выберите команду:", PluginRegistry.AuthorCommands, c => c.Name);
        if (command is null)
        {
            return;
        }

        pane.CommitPendingEdits();

        try
        {
            command.Execute(pane);
        }
        catch (Exception ex)
        {
            await _ui.Dialogs.MessageAsync("Команда плагина", $"Плагин «{command.Name}» упал: {ex.Message}");
            return;
        }

        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = $"Выполнена команда плагина «{command.Name}».";
    }

    partial void OnShowElementTagsChanged(bool value)
    {
        foreach (var pane in _docs.Panes.Values)
        {
            pane.Author.ShowElementTags = value;
            pane.Author.Rebuild();
        }
    }

    [RelayCommand]
    private void Undo()
    {
        _docs.Current?.PerformUndo();
        _shell.StatusText = "Отменено.";
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _hooks.RefreshMapTree?.Invoke();
    }

    [RelayCommand]
    private void Redo()
    {
        _docs.Current?.PerformRedo();
        _shell.StatusText = "Повторено.";
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _hooks.RefreshMapTree?.Invoke();
    }
}
