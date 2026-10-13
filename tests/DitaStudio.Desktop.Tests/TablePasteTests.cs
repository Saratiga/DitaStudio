using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Ctrl+C/X/V с ячейками таблицы и блоками: между таблицами «Автора», из Excel/Word, блоки внутрь ячейки.</summary>
public sealed class TablePasteTests
{
    private const string Topic =
        "<concept id=\"c\"><title>Т</title><conbody><p>Абзац перед.</p>" +
        "<table><tgroup cols=\"3\"><colspec colname=\"c1\" colnum=\"1\"/><colspec colname=\"c2\" colnum=\"2\"/><colspec colname=\"c3\" colnum=\"3\"/><tbody>" +
        "<row><entry>a1</entry><entry>b1</entry><entry>c1</entry></row>" +
        "<row><entry>a2</entry><entry>b2</entry><entry>c2</entry></row></tbody></tgroup></table>" +
        "<p>Абзац после.</p></conbody></concept>";

    private static (Window Window, AuthorView Author, DitaDocument Document, List<string> Undo) Show()
    {
        TableClipboard.LastCopied = null;
        var document = DitaDocument.Parse(Topic);
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, description) => undo.Add(description);
        author.Load(document);
        var window = new Window { Width = 900, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document, undo);
    }

    private static string[][] Rows(DitaDocument document) =>
        document.Root.Descendants().Where(n => n.Name == "row").Select(r => r.ElementChildren().Select(e => e.InnerText).ToArray()).ToArray();

    [AvaloniaFact]
    public void CopyAndPasteSelectedCells_InsideTheTable_FillsFromTopLeft()
    {
        var (window, author, document, undo) = Show();
        var table = document.Root.Descendants().First(n => n.Name == "table");
        author.SelectCellRange(table, new CellRange(0, 1, 0, 1));

        var text = author.Surface.CopySelectedCells(cut: false);
        Assert.Equal("a1\tb1\r\na2\tb2", text);

        author.SelectCellRange(table, new CellRange(1, 1, 2, 2));
        var result = author.Surface.PasteCells(text);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.AddedRows); // вторая строка ниже таблицы не помещается — таблица удлинилась
        Assert.Equal(1, result.Value.SkippedColumns); // столбец за краем отброшен
        var rows = Rows(document);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "a2", "b2", "a1" }, rows[1]);
        Assert.Equal(new[] { "", "", "a2" }, rows[2]);
        Assert.Equal(new[] { "a1", "b1", "c1" }, rows[0]);
        Assert.Equal(new[] { "Вставка ячеек" }, undo); // одна отмена на всё
        window.Close();
    }

    [AvaloniaFact]
    public void CutCells_ClearsThem_AndTextFromExcelFillsFromCursorCell()
    {
        var (window, author, document, undo) = Show();
        var table = document.Root.Descendants().First(n => n.Name == "table");
        author.SelectCellRange(table, new CellRange(0, 0, 0, 2));
        Assert.Equal("a1\tb1\tc1", author.Surface.CopySelectedCells(cut: true));
        Assert.Equal(new[] { "", "", "" }, Rows(document)[0]);

        // Курсор в ячейке, в буфере текст из Excel: две строки по две ячейки.
        author.ClearCellSelection();
        var cell = document.Root.Descendants().Where(n => n.Name == "entry").ElementAt(1); // b1
        Assert.NotNull(author.EditorFor(cell));
        author.EditorFor(cell)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        var result = author.Surface.PasteCells("x\ty\r\nz\tw\r\n", cell);
        Assert.NotNull(result);
        var rows = Rows(document);
        Assert.Equal(new[] { "", "x", "y" }, rows[0]);
        Assert.Equal(new[] { "a2", "z", "w" }, rows[1]);
        Assert.Equal(2, undo.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void TabularText_OutsideTable_BecomesTable_PlainTextStaysText()
    {
        var (window, author, document, undo) = Show();
        var first = document.Root.Descendants().First(n => n.Name == "p");
        author.EditorFor(first)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        Assert.False(author.Surface.PasteAsTable("просто слова"));
        Assert.False(author.Surface.PasteAsTable("строка без табуляции"));
        Assert.True(author.Surface.PasteAsTable("№\tИмя\r\n1\tАльфа\r\n2\tБета"));
        Dispatcher.UIThread.RunJobs();

        var tables = document.Root.Descendants().Where(n => n.Name == "table").ToList();
        Assert.Equal(2, tables.Count);
        Assert.Same(first, document.Root.FirstElement("conbody")!.ElementChildren().First()); // новая таблица — после абзаца, до старой
        var created = document.Root.FirstElement("conbody")!.ElementChildren().ElementAt(1);
        Assert.Equal("table", created.Name);
        Assert.Equal(new[] { new[] { "№", "Имя" }, new[] { "1", "Альфа" }, new[] { "2", "Бета" } },
            created.Descendants().Where(n => n.Name == "row").Select(r => r.ElementChildren().Select(e => e.InnerText).ToArray()).ToArray());
        Assert.Single(undo);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlV_InCellEditor_PastesExcelText_IntoTheTable()
    {
        var (window, author, document, _) = Show();
        var cell = document.Root.Descendants().Where(n => n.Name == "entry").First();
        var clipboard = TopLevel.GetTopLevel(author)?.Clipboard;
        Assert.NotNull(clipboard);
        await clipboard!.SetTextAsync("1\t2\r\n3\t4");
        var editor = author.EditorFor(cell)!;
        editor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "1", "2", "c1" }, Rows(document)[0]);
        Assert.Equal(new[] { "3", "4", "c2" }, Rows(document)[1]);
        window.Close();
    }

    [AvaloniaFact]
    public void BlocksPastedAtCellEditor_GoInsideTheCell()
    {
        var (window, author, document, _) = Show();
        var table = document.Root.Descendants().First(n => n.Name == "table");
        var cell = document.Root.Descendants().First(n => n.Name == "entry");
        var blocks = BlockClipboard.Parse("<p>блок</p>")!;
        var place = BlockClipboard.FindInsertion(cell, blocks);
        Assert.NotNull(place);
        Assert.Same(cell, place!.Parent);
        Assert.True(author.Surface.InsertBlocks(place.Parent, place.Index, blocks));
        Assert.Equal("блок", cell.FirstElement("p")!.InnerText);
        Assert.Same(table, document.Root.Descendants().First(n => n.Name == "table")); // таблица не разобрана и не продублирована
        window.Close();
    }
}
