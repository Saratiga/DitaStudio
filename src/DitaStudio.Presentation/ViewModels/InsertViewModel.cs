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
    private readonly MainViewModel _main;

    [ObservableProperty]
    private bool showElementTags = true;

    public InsertViewModel(MainViewModel main)
    {
        _main = main;
    }

    // [RelayCommand] — не только для InsertParagraph/InsertUl/и т.п. ниже,
    // но и для палитры вставки (MainWindow.SidePanels.cs, не мигрирована),
    // которая вызывает произвольное имя элемента из каталога по double-click.
    [RelayCommand]
    private void InsertElement(string name)
    {
        var pane = _main.Current;
        if (pane is null)
        {
            return;
        }

        pane.Mode = EditorMode.Author;
        if (!pane.Author.InsertElement(name))
        {
            _main.StatusText = $"Элемент <{name}> здесь недопустим.";
            return;
        }

        _main.Documents.RefreshAllTabTitles();
        _main.RefreshOutline?.Invoke();
        _main.StatusText = $"Вставлен <{name}>";
    }

    /// <summary>
    /// Что можно вставить у курсора текущей вкладки: фразовые элементы — в строку текущего блока,
    /// блочные — после него (как это делает <see cref="InsertElementCommand"/>). По описанию.
    /// </summary>
    public (IReadOnlyList<ElementDef> Inline, IReadOnlyList<ElementDef> After) InsertCandidates()
    {
        var node = _main.Current?.Author.CurrentNode;
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
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node?.Parent is null)
        {
            return;
        }

        var options = await _main.Dialogs.InsertTableAsync();
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
            _main.StatusText = "Таблицу здесь вставить нельзя.";
            return;
        }

        parent.Insert(EditCommands.ChildIndexForElementIndex(parent, index), table);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshOutline?.Invoke();
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
        var pane = _main.Current;
        if (pane is null || pane.FilePath is null)
        {
            return;
        }

        var file = await _main.Files.OpenFileAsync("Выберите изображение",
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
            _main.StatusText = "Рисунок вставлен: замените название под ним. Подпись «Рисунок N» появится при публикации.";
            _main.Documents.RefreshAllTabTitles();
            return;
        }

        image.SetAttribute("placement", "break");
        if (!pane.Author.InsertInlineNode(image))
        {
            _main.StatusText = "Поставьте курсор в абзац, куда вставить изображение.";
            return;
        }

        _main.Documents.RefreshAllTabTitles();
    }

    /// <summary>Подпись рисунка или таблицы под курсором: добавить пустую («Рисунок N») или убрать совсем.</summary>
    [RelayCommand]
    private void ToggleCaption()
    {
        if (_main.Current is not { } pane)
        {
            return;
        }

        switch (pane.Author.ToggleCaption())
        {
            case true:
                _main.StatusText = "Подпись добавлена: при публикации — «Рисунок N» / «Таблица N»; впишите название в заголовок.";
                _main.Documents.RefreshAllTabTitles();
                break;
            case false:
                _main.StatusText = "Подпись убрана: у этого рисунка (таблицы) подписи и номера не будет.";
                _main.Documents.RefreshAllTabTitles();
                break;
            default:
                _main.StatusText = "Поставьте курсор в рисунок или таблицу, чтобы добавить или убрать подпись.";
                break;
        }
    }

    /// <summary>Изображение из абзаца под курсором — в рисунок с названием и номером.</summary>
    [RelayCommand]
    private void WrapImageAsFigure()
    {
        if (_main.Current is not { } pane)
        {
            return;
        }

        if (!pane.Author.WrapImageAsFigure())
        {
            _main.StatusText = "Поставьте курсор в абзац с изображением (вне рисунка), чтобы оформить его как рисунок.";
            return;
        }

        _main.StatusText = "Изображение оформлено как рисунок: замените название под ним.";
        _main.Documents.RefreshAllTabTitles();
    }

    [RelayCommand]
    private async Task InsertXref()
    {
        var pane = _main.Current;
        var project = _main.Project;
        if (project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = await _main.Dialogs.InsertXrefAsync(project, pane.FilePath);
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
            _main.StatusText = "Поставьте курсор в текст, куда вставить ссылку.";
            return;
        }

        _main.Documents.RefreshAllTabTitles();
    }

    private void Format(string element)
    {
        var pane = _main.Current;
        if (pane is null)
        {
            return;
        }

        if (!pane.Author.WrapCurrentInline(element))
        {
            _main.StatusText = "Выделите текст в режиме «Автор».";
            return;
        }

        _main.Documents.RefreshAllTabTitles();
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
        if (_main.Current?.Author.MoveCurrent(up) == true)
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshOutline?.Invoke();
        }
    }

    [RelayCommand]
    private async Task DeleteElement()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!await _main.Dialogs.ConfirmAsync("Удаление", $"Удалить элемент <{node.Name}> вместе с содержимым?"))
        {
            return;
        }

        if (_main.Current?.Author.DeleteCurrent() == true)
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshOutline?.Invoke();
        }
    }

    [RelayCommand]
    private async Task RenameId()
    {
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        var project = _main.Project;
        if (project is null || pane?.FilePath is null || node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var oldId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(oldId))
        {
            _main.StatusText = "У элемента нет id — задайте его в панели «Атрибуты», затем переименовывайте.";
            return;
        }

        var newId = await _main.Dialogs.RenameIdAsync(oldId!);
        if (string.IsNullOrWhiteSpace(newId) || newId == oldId)
        {
            return;
        }

        foreach (var p in _main.Panes.Values)
        {
            p.CommitPendingEdits();
        }

        var result = RefactorService.RenameId(project, pane.FilePath, oldId!, newId!);
        _main.ApplyRefactorResult?.Invoke(result);
        _main.StatusText = $"id «{oldId}» переименован в «{newId}». Обновлено ссылок: {result.UpdatedReferences}.";
    }

    [RelayCommand]
    private async Task ExtractToConref()
    {
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        var project = _main.Project;
        if (project is null || pane?.FilePath is null || node is null || node.Parent is null)
        {
            _main.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var suggestedId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(suggestedId))
        {
            suggestedId = DocumentTemplates.SuggestId(node.InnerText, node.Name);
        }

        var dialogResult = await _main.Dialogs.ExtractToConrefAsync(project, suggestedId!);
        if (dialogResult is null)
        {
            return;
        }

        foreach (var p in _main.Panes.Values)
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
            await _main.Dialogs.MessageAsync("Вынесение в conref", ex.Message);
            return;
        }

        if (result.UpdatedReferences == 0)
        {
            await _main.Dialogs.MessageAsync("Вынесение в conref", "Не удалось перенести элемент — проверьте цель.");
            return;
        }

        _main.ApplyRefactorResult?.Invoke(result);
        if (dialogResult.TargetFile is null)
        {
            project.Scan();
            _main.RefreshProjectTree?.Invoke();
        }

        _main.StatusText = $"Элемент вынесен в conref (id «{dialogResult.ElementId}»).";
    }

    /// <summary>Строки и столбцы таблицы под курсором (меню ячейки, панель инструментов).</summary>
    [RelayCommand]
    private void EditTable(TableOperation operation)
    {
        if (_main.Current?.Author.EditCurrentTable(operation) == true)
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.RefreshOutline?.Invoke();
            _main.StatusText = TableCommands.Describe(operation) + " — выполнено.";
        }
        else
        {
            _main.StatusText = operation switch
            {
                TableOperation.DeleteRow => "Строку удалить нельзя: в таблице должна остаться хотя бы одна строка.",
                TableOperation.DeleteColumn => "Столбец удалить нельзя: он последний или число столбцов задано типом таблицы.",
                TableOperation.SplitCell => "Ячейка не объединена — делить нечего.",
                TableOperation.InsertColumnLeft or TableOperation.InsertColumnRight => "В этой таблице число столбцов задано её типом.",
                _ => "Поставьте курсор в ячейку таблицы."
            };
        }
    }

    [RelayCommand]
    private void MergeCellRight()
    {
        if (_main.Current?.Author.MergeCurrentCellRight() == true)
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = "Ячейки объединены по горизонтали.";
        }
        else
        {
            _main.StatusText = "Выделите ячейку таблицы, у которой есть соседняя справа.";
        }
    }

    [RelayCommand]
    private void MergeCellDown()
    {
        if (_main.Current?.Author.MergeCurrentCellDown() == true)
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = "Ячейки объединены по вертикали.";
        }
        else
        {
            _main.StatusText = "Выделите ячейку таблицы, у которой есть соседняя снизу.";
        }
    }

    [RelayCommand]
    private void TogglePageBreakBeforeTitle()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _main.StatusText = "Выделите заголовок (title) — например, заголовок раздела или топика.";
            return;
        }

        var enabled = _main.Current!.Author.ToggleCurrentOutputClass("page-break-before");
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = enabled == true
            ? "Разрыв страницы перед заголовком включён."
            : "Разрыв страницы перед заголовком выключен.";
    }

    /// <summary>Выравнивание текущего блока: align-left (по умолчанию — класс снимается), -center, -right, -justify.</summary>
    [RelayCommand]
    private void SetAlignment(string? token)
    {
        var author = _main.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _main.StatusText = "Поставьте курсор в абзац, заголовок или ячейку.";
            return;
        }

        var value = token is null or "align-left" ? null : token;
        if (author.SetCurrentBlockFormat(TextFormatting.AlignPrefix, value))
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = "Выравнивание: " + TextFormatting.Alignments.First(a => a.Token == (value ?? "align-left")).Label.ToLowerInvariant() + ".";
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
            var typed = await _main.Dialogs.PromptTextAsync("Свой размер шрифта", "Размер, пт",
                (_main.Current?.Author.CurrentNode is { } node && TextFormatting.SizeOf(node) is { } current ? current : 11).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                $"От {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт; дробные значения — через точку или запятую (например, 13,5).");
            if (typed is null)
            {
                return;
            }

            if (!double.TryParse(typed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var custom) ||
                custom < TextFormatting.MinCustomSize || custom > TextFormatting.MaxCustomSize)
            {
                _main.StatusText = $"Размер должен быть числом от {TextFormatting.MinCustomSize:0} до {TextFormatting.MaxCustomSize:0} пт.";
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

    private void ApplyTextFormat(string prefix, string? token, string done)
    {
        var author = _main.Current?.Author;
        if (author?.CurrentNode is null)
        {
            _main.StatusText = "Поставьте курсор в текст или выделите его.";
            return;
        }

        if (author.ApplyTextFormat(prefix, token))
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = done;
        }
        else
        {
            _main.StatusText = "Здесь оформление текста недоступно.";
        }
    }

    /// <summary>
    /// Положение блока на отдельном листе PDF/DOCX: "place-bottom-right" и т. п. (<see cref="PagePlacement"/>),
    /// null — обычное, в тексте.
    /// </summary>
    [RelayCommand]
    private void SetPagePlacement(string? token)
    {
        var author = _main.Current?.Author;
        if (author?.CurrentNode is null || PagePlacement.PlaceableFor(author.CurrentNode) is null)
        {
            _main.StatusText = "Поставьте курсор в абзац, рисунок, таблицу или заметку прямо в тексте топика (не в списке).";
            return;
        }

        if (author.SetCurrentBlockFormat(PagePlacement.Prefix, token))
        {
            _main.Documents.RefreshAllTabTitles();
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = PagePlacement.LabelOf(token) is { } label
                ? $"Блок на отдельном листе PDF и DOCX: {label.ToLowerInvariant()}."
                : "Блок снова идёт в тексте.";
        }
    }

    /// <summary>Нумерованный абзац (пункт): номер по заголовкам — 2.3.1 (outputclass numbered).</summary>
    [RelayCommand]
    private void ToggleNumberedParagraph()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || node.Name != "p")
        {
            _main.StatusText = "Поставьте курсор в абзац.";
            return;
        }

        var enabled = _main.Current!.Author.ToggleCurrentOutputClass(HeadingNumbering.NumberedClass);
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = enabled == true
            ? "Абзац нумерованный: при публикации получит номер по заголовкам (например, 2.3.1)."
            : "Абзац больше не нумеруется.";
    }

    /// <summary>Заголовок «без номера»: не нумеруется и не попадает в оглавление (outputclass nonumber).</summary>
    [RelayCommand]
    private void ToggleUnnumberedTitle()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            _main.StatusText = "Поставьте курсор в заголовок топика или раздела.";
            return;
        }

        var enabled = _main.Current!.Author.ToggleCurrentOutputClass(TocRules.NoNumberClass);
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = enabled == true
            ? "Заголовок без номера: при публикации не нумеруется и не попадает в оглавление."
            : "Заголовок снова нумеруется и попадает в оглавление.";
    }

    [RelayCommand]
    private void ToggleTablePageBreakAuto()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || node.Name != "table")
        {
            _main.StatusText = "Выделите таблицу целиком (не отдельную ячейку).";
            return;
        }

        var enabled = _main.Current!.Author.ToggleCurrentOutputClass("page-break-auto");
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = enabled == true
            ? "Таблица теперь может переноситься на страницы с повтором шапки."
            : "Таблица снова печатается как единый блок.";
    }

    [RelayCommand]
    private void ToggleRevChanged()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент, который нужно отметить как изменённый.";
            return;
        }

        var enabled = _main.Current!.Author.ToggleCurrentRev();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = enabled == true
            ? "Элемент отмечен как изменённый (rev) — при публикации появится полоса на полях."
            : "Отметка об изменении снята.";
    }

    [RelayCommand]
    private void MarkTrackedInserted()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент, который нужно пометить как вставленный.";
            return;
        }

        _main.Current!.Author.MarkCurrentInserted();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = "Элемент помечен как вставленный (track changes).";
    }

    [RelayCommand]
    private void MarkTrackedDeleted()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент, который нужно пометить как удалённый.";
            return;
        }

        _main.Current!.Author.MarkCurrentDeleted();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = "Элемент помечен как удалённый (track changes) — скрыт из публикации, виден зачёркнутым в предпросмотре.";
    }

    [RelayCommand]
    private void AcceptTrackedChange()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _main.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _main.Current!.Author.AcceptCurrentTrackedChange();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = "Правка принята.";
    }

    [RelayCommand]
    private void RejectTrackedChange()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null || !TrackChanges.IsTracked(node))
        {
            _main.StatusText = "Поставьте курсор в элемент с отслеживаемой правкой.";
            return;
        }

        _main.Current!.Author.RejectCurrentTrackedChange();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = "Правка отклонена.";
    }

    /// <summary>Выполняет выбранную команду плагина (см. IAuthorCommandPlugin) на открытом
    /// документе — пункт меню один и тот же для любого числа подключённых плагинов.</summary>
    [RelayCommand]
    private async Task RunAuthorCommandPlugin()
    {
        var pane = _main.Current;
        if (pane is null)
        {
            await _main.Dialogs.MessageAsync("Команда плагина", "Откройте документ.");
            return;
        }

        var command = await _main.Dialogs.PickOneAsync("Команда плагина", "Выберите команду:", PluginRegistry.AuthorCommands, c => c.Name);
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
            await _main.Dialogs.MessageAsync("Команда плагина", $"Плагин «{command.Name}» упал: {ex.Message}");
            return;
        }

        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshAttributePanel?.Invoke();
        _main.StatusText = $"Выполнена команда плагина «{command.Name}».";
    }

    partial void OnShowElementTagsChanged(bool value)
    {
        foreach (var pane in _main.Panes.Values)
        {
            pane.Author.ShowElementTags = value;
            pane.Author.Rebuild();
        }
    }

    [RelayCommand]
    private void Undo()
    {
        _main.Current?.PerformUndo();
        _main.StatusText = "Отменено.";
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _main.RefreshMapTree?.Invoke();
    }

    [RelayCommand]
    private void Redo()
    {
        _main.Current?.PerformRedo();
        _main.StatusText = "Повторено.";
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _main.RefreshMapTree?.Invoke();
    }
}
