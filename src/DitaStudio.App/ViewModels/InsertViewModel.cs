using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using Microsoft.Win32;

namespace DitaStudio.App.ViewModels;

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

    [RelayCommand]
    private void InsertParagraph() => InsertElement("p");

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
    private void InsertTable()
    {
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node?.Parent is null)
        {
            return;
        }

        var options = Dialogs.InsertTable();
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

    private static DitaNode BuildTableNode(Dialogs.TableResult options)
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
    private void InsertImage()
    {
        var pane = _main.Current;
        if (pane is null || pane.FilePath is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp|Все файлы|*.*",
            InitialDirectory = Path.GetDirectoryName(pane.FilePath)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, dialog.FileName);
        var image = DitaNode.Element("image");
        image.SetAttribute("href", href);
        image.SetAttribute("placement", "break");

        var alt = DitaNode.Element("alt");
        alt.SetText(Path.GetFileNameWithoutExtension(dialog.FileName));
        image.Add(alt);

        if (!pane.Author.InsertInlineNode(image))
        {
            _main.StatusText = "Поставьте курсор в абзац, куда вставить изображение.";
            return;
        }

        _main.Documents.RefreshAllTabTitles();
    }

    [RelayCommand]
    private void InsertXref()
    {
        var pane = _main.Current;
        var project = _main.Project;
        if (project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = Dialogs.InsertXref(project, pane.FilePath);
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
    private void DeleteElement()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!Dialogs.Confirm("Удаление", $"Удалить элемент <{node.Name}> вместе с содержимым?"))
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
    private void RenameId()
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

        var newId = Dialogs.RenameId(oldId!);
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
    private void ExtractToConref()
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

        var dialogResult = Dialogs.ExtractToConref(project, suggestedId!);
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
            Dialogs.Message("Вынесение в conref", ex.Message);
            return;
        }

        if (result.UpdatedReferences == 0)
        {
            Dialogs.Message("Вынесение в conref", "Не удалось перенести элемент — проверьте цель.");
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
    }

    [RelayCommand]
    private void Redo()
    {
        _main.Current?.PerformRedo();
        _main.StatusText = "Повторено.";
        _main.Documents.RefreshAllTabTitles();
        _main.RefreshOutline?.Invoke();
    }
}
