using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>«Наименование таблицы…» / «Наименование рисунка…»: название пишется в title одной правкой, пустое убирает название, номер остаётся.</summary>
public sealed class CaptionNameTests
{
    private static (Window Window, AuthorView Author, DitaDocument Document, List<string> Undo) Show(string body)
    {
        var document = DitaDocument.Parse("<concept id=\"c\"><title>Т</title><conbody>" + body + "</conbody></concept>");
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, d) => undo.Add(d);
        author.Load(document);
        var window = new Window { Width = 800, Height = 500, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document, undo);
    }

    [AvaloniaFact]
    public void TableWithoutTitle_GetsName_EmptyNameKeepsNumberedCaption()
    {
        var (window, author, document, undo) = Show(
            "<table><tgroup cols=\"1\"><colspec colname=\"c1\"/><tbody><row><entry>a</entry></row></tbody></tgroup></table>");
        var table = document.Root.Descendants().First(n => n.Name == "table");
        author.EditorFor(table.Descendants().First(n => n.Name == "entry"))!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(table, author.Surface.CaptionedAtCursor());
        Assert.True(author.Surface.SetCaptionName("  Итоги года  "));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Итоги года", table.FirstElement("title")!.InnerText);
        Assert.Equal(new[] { "Наименование подписи" }, undo);
        Assert.Contains(author.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Таблица 1.");

        Assert.True(author.Surface.SetCaptionName("Итоги года")); // без изменений — правки нет
        Assert.Single(undo);

        Assert.True(author.Surface.SetCaptionName(""));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(table.FirstElement("title")); // подпись «Таблица 1» без названия
        Assert.Equal(string.Empty, table.FirstElement("title")!.InnerText);
        Assert.Equal(2, undo.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Figure_NameReplaced_CellMenuHasTableNameItem()
    {
        var (window, author, document, _) = Show("<fig><title>Старое</title><image href=\"a.png\"/></fig>" +
            "<table><tgroup cols=\"2\"><colspec colname=\"c1\"/><colspec colname=\"c2\"/><tbody><row><entry>a</entry><entry>b</entry></row></tbody></tgroup></table>");
        var fig = document.Root.Descendants().First(n => n.Name == "fig");
        author.EditorFor(fig.FirstElement("title")!)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(fig, author.Surface.CaptionedAtCursor());
        Assert.True(author.Surface.SetCaptionName("Новое"));
        Assert.Equal("Новое", fig.FirstElement("title")!.InnerText);

        var table = document.Root.Descendants().First(n => n.Name == "table");
        author.SelectCellRange(table, new CellRange(0, 0, 0, 1));
        var requested = false;
        author.CaptionNameRequested += (_, _) => requested = true;
        var item = author.BuildCellMenu().ItemsSource!.OfType<MenuItem>().First();
        Assert.Equal("Наименование таблицы…", item.Header);
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(requested);
        window.Close();
    }
}
