using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Presentation.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Кнопки оформления — переключатели: оформление у курсора видно, повторное нажатие снимает его с выделения или со всего элемента.</summary>
public sealed class FormatToggleTests
{
    private static DitaNode Block(string xml) =>
        DitaDocument.Parse($"<concept id=\"c\"><title>t</title><conbody>{xml}</conbody></concept>").Root.FirstElement("conbody")!.ElementChildren().First();

    [Fact]
    public void Model_FormattingIn_Unwrap_SplitsElementAroundSelection()
    {
        var p = Block("<p>a <b>bold <i>it</i></b> z</p>");
        var content = InlineContent.FromNode(p);
        Assert.Equal(new[] { "b" }, content.FormattingIn("a bo".Length, 0).Select(n => n.Name));
        Assert.Equal(new[] { "b", "i" }, content.FormattingIn("a bold i".Length, 0).Select(n => n.Name));
        Assert.Empty(content.FormattingIn(0, "a bold".Length)); // не весь участок жирный
        Assert.Equal(new[] { "b" }, content.FormattingIn(2, 3).Select(n => n.Name));

        // Середина жирного «bold» — без оформления, края остаются жирными (элемент разрезан на два).
        Assert.True(content.Unwrap(3, 2, "b"));
        content.WriteBack();
        Assert.Equal("<p>a <b>b</b>ol<b>d <i>it</i></b> z</p>", XmlSerializer.ToXml(p));

        var whole = Block("<p>x <b>все</b> y</p>");
        var c2 = InlineContent.FromNode(whole);
        var b = c2.FormattingIn(3, 0).Single();
        Assert.Equal((2, 3), c2.ExtentOf(b));
        Assert.False(c2.Unwrap(0, 1, "b"));
    }

    [Fact]
    public void CaretFormatting_ReadsSizeColorMarkAndAlignment()
    {
        var p = Block("<p outputclass=\"align-center size-14\">a <ph outputclass=\"color-red mark-yellow\"><b>b</b></ph></p>");
        var content = InlineContent.FromNode(p);
        var formatting = CaretFormatting.Of(content.FormattingIn(3, 0), p);
        Assert.True(formatting.Has("b") && formatting.Has("ph"));
        Assert.Equal(14, formatting.SizePt);
        Assert.Equal("#C00000", formatting.ColorHex);
        Assert.Equal("#FFFF00", formatting.MarkHex);
        Assert.Equal("center", formatting.Align);
    }

    [AvaloniaFact]
    public void Toggle_InAuthor_UnwrapsWholeElementAtCaret_OrSelectedPart_CtrlBToo()
    {
        var document = DitaDocument.Parse("<concept id=\"c\"><title>Т</title><conbody><p>Первый <b>жирный</b> абзац.</p></conbody></concept>");
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, d) => undo.Add(d);
        author.Load(document);
        var window = new Window { Width = 800, Height = 400, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var p = document.Root.Descendants().First(n => n.Name == "p");
        var editor = author.EditorFor(p)!;

        var changed = 0;
        author.CaretFormattingChanged += (_, _) => changed++;
        editor.FocusEditor("Первый жир".Length);
        Dispatcher.UIThread.RunJobs();
        Assert.True(author.CurrentFormatting()!.Has("b"));
        Assert.True(changed > 0);

        // Курсор в жирном слове, без выделения — снимается весь элемент.
        Assert.True(author.Surface.ToggleCurrentInline("b"));
        author.FlushPendingEdits();
        Assert.Equal("<p>Первый жирный абзац.</p>", XmlSerializer.ToXml(p));
        Assert.Equal(new[] { "Снятие оформления <b>" }, undo);
        Assert.False(author.CurrentFormatting()!.Has("b"));

        // Курсор в обычном тексте без выделения — нечего ни снимать, ни оборачивать.
        Assert.False(author.Surface.ToggleCurrentInline("b"));
        Assert.Single(undo);

        // Выделение — оборачивается; повторное нажатие по части выделенного — снимает только с неё.
        editor = author.EditorFor(p)!;
        editor.FocusEditor(0);
        editor.Select("Первый ".Length, "жирный".Length);
        Assert.True(author.Surface.ToggleCurrentInline("b"));
        author.FlushPendingEdits();
        Assert.Equal("<p>Первый <b>жирный</b> абзац.</p>", XmlSerializer.ToXml(p));
        editor = author.EditorFor(p)!;
        editor.FocusEditor(0);
        editor.Select("Первый ".Length, "жир".Length);
        Assert.True(author.Surface.ToggleCurrentInline("b"));
        author.FlushPendingEdits();
        Assert.Equal("<p>Первый жир<b>ный</b> абзац.</p>", XmlSerializer.ToXml(p));

        // Ctrl+B на жирном тексте — тоже снимает.
        editor = author.EditorFor(p)!;
        editor.FocusEditor("Первый жирн".Length);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("<p>Первый жирный абзац.</p>", XmlSerializer.ToXml(p));
        window.Close();
    }
}
