using System.Windows;
using System.Windows.Controls;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using Microsoft.Win32;

namespace DitaStudio.App;

// Вставка элементов (абзац, список, таблица, изображение, ссылка, сноска), инлайн-форматирование,
// перестановка/удаление элемента, объединение ячеек таблицы, переключатели печати.
public partial class MainWindow
{
    private void InsertElement(string name)
    {
        var pane = Current;
        if (pane is null)
        {
            return;
        }

        pane.Mode = EditorMode.Author;
        if (!pane.Author.InsertElement(name))
        {
            UpdateStatus($"Элемент <{name}> здесь недопустим.");
            return;
        }

        UpdateTabHeaders();
        BuildOutline();
        UpdateStatus($"Вставлен <{name}>");
    }

    private void OnInsertParagraph(object sender, RoutedEventArgs e) => InsertElement("p");

    private void OnInsertUl(object sender, RoutedEventArgs e) => InsertElement("ul");

    private void OnInsertOl(object sender, RoutedEventArgs e) => InsertElement("ol");

    private void OnInsertNote(object sender, RoutedEventArgs e) => InsertElement("note");

    private void OnInsertCodeblock(object sender, RoutedEventArgs e) => InsertElement("codeblock");

    private void OnInsertFootnote(object sender, RoutedEventArgs e) => InsertElement("fn");

    private void OnInsertTable(object sender, RoutedEventArgs e)
    {
        var pane = Current;
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
            UpdateStatus("Таблицу здесь вставить нельзя.");
            return;
        }

        parent.Insert(EditCommands.ChildIndexForElementIndex(parent, index), table);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        UpdateTabHeaders();
        BuildOutline();
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

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var pane = Current;
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

        if (dialog.ShowDialog(this) != true)
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
            UpdateStatus("Поставьте курсор в абзац, куда вставить изображение.");
            return;
        }

        UpdateTabHeaders();
    }

    private void OnInsertXref(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        if (_project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = Dialogs.InsertXref(_project, pane.FilePath);
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
            UpdateStatus("Поставьте курсор в текст, куда вставить ссылку.");
            return;
        }

        UpdateTabHeaders();
    }

    private void Format(string element)
    {
        var pane = Current;
        if (pane is null)
        {
            return;
        }

        if (!pane.Author.WrapCurrentInline(element))
        {
            UpdateStatus("Выделите текст в режиме «Автор».");
            return;
        }

        UpdateTabHeaders();
    }

    private void OnFormatBold(object sender, RoutedEventArgs e) => Format("b");

    private void OnFormatItalic(object sender, RoutedEventArgs e) => Format("i");

    private void OnFormatCode(object sender, RoutedEventArgs e) => Format("codeph");

    private void OnFormatUicontrol(object sender, RoutedEventArgs e) => Format("uicontrol");

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveElement(true);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveElement(false);

    private void MoveElement(bool up)
    {
        if (Current?.Author.MoveCurrent(up) == true)
        {
            UpdateTabHeaders();
            BuildOutline();
        }
    }

    private void OnDeleteElement(object sender, RoutedEventArgs e)
    {
        var node = Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!Dialogs.Confirm("Удаление", $"Удалить элемент <{node.Name}> вместе с содержимым?"))
        {
            return;
        }

        if (Current?.Author.DeleteCurrent() == true)
        {
            UpdateTabHeaders();
            BuildOutline();
        }
    }

    private void OnMergeCellRight(object sender, RoutedEventArgs e)
    {
        if (Current?.Author.MergeCurrentCellRight() == true)
        {
            UpdateTabHeaders();
            BuildAttributePanel();
            UpdateStatus("Ячейки объединены по горизонтали.");
        }
        else
        {
            UpdateStatus("Выделите ячейку таблицы, у которой есть соседняя справа.");
        }
    }

    private void OnMergeCellDown(object sender, RoutedEventArgs e)
    {
        if (Current?.Author.MergeCurrentCellDown() == true)
        {
            UpdateTabHeaders();
            BuildAttributePanel();
            UpdateStatus("Ячейки объединены по вертикали.");
        }
        else
        {
            UpdateStatus("Выделите ячейку таблицы, у которой есть соседняя снизу.");
        }
    }

    private void OnTogglePageBreakBeforeTitle(object sender, RoutedEventArgs e)
    {
        var node = Current?.Author.CurrentNode;
        if (node is null || node.Name != "title")
        {
            UpdateStatus("Выделите заголовок (title) — например, заголовок раздела или топика.");
            return;
        }

        var enabled = Current!.Author.ToggleCurrentOutputClass("page-break-before");
        UpdateTabHeaders();
        BuildAttributePanel();
        UpdateStatus(enabled == true
            ? "Разрыв страницы перед заголовком включён."
            : "Разрыв страницы перед заголовком выключен.");
    }

    private void OnToggleTablePageBreakAuto(object sender, RoutedEventArgs e)
    {
        var node = Current?.Author.CurrentNode;
        if (node is null || node.Name != "table")
        {
            UpdateStatus("Выделите таблицу целиком (не отдельную ячейку).");
            return;
        }

        var enabled = Current!.Author.ToggleCurrentOutputClass("page-break-auto");
        UpdateTabHeaders();
        BuildAttributePanel();
        UpdateStatus(enabled == true
            ? "Таблица теперь может переноситься на страницы с повтором шапки."
            : "Таблица снова печатается как единый блок.");
    }

    private void OnToggleTags(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.Author.ShowElementTags = item.IsChecked;
            pane.Author.Rebuild();
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        Current?.PerformUndo();
        UpdateTabHeaders();
        BuildOutline();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        Current?.PerformRedo();
        UpdateTabHeaders();
        BuildOutline();
    }
}
