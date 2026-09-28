using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Режим «Автор» Avalonia-версии: набор текста, клавиши структурных правок (Enter, Backspace,
/// Delete, Tab, стрелки), оформление выделения — через настоящие события клавиатуры headless-окна.
/// </summary>
public sealed class AuthorViewTests
{
    private const string Topic =
        "<concept id=\"c\"><title>Заголовок</title><conbody>" +
        "<p>Первый <b>жирный</b> абзац.</p><p>Второй абзац.</p>" +
        "<ul><li>один</li><li>два</li></ul>" +
        "</conbody></concept>";

    private static (Window Window, AuthorView Author, DitaDocument Document, List<string> Undo) Show(string xml = Topic)
    {
        var document = DitaDocument.Parse(xml);
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, description) => undo.Add(description);
        author.Load(document);

        var window = new Window { Width = 900, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document, undo);
    }

    private static List<DitaNode> Paragraphs(DitaDocument document) =>
        document.Root.FirstElement("conbody")!.ElementChildren().Where(n => n.Name == "p").ToList();

    private static BlockEditor Focus(Window window, AuthorView author, DitaNode node, int caret)
    {
        var editor = author.EditorFor(node)!;
        editor.FocusEditor(caret);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(node, author.CurrentNode);
        return editor;
    }

    private static BlockEditor? FocusedEditor(AuthorView author) =>
        author.Editors.FirstOrDefault(e => e.TextArea.IsKeyboardFocusWithin);

    [AvaloniaFact]
    public void Typing_GoesIntoModelWithSurroundingFormatting()
    {
        var (window, author, document, _) = Show();
        var first = Paragraphs(document)[0];
        Focus(window, author, first, "Первый жир".Length);

        window.KeyTextInput("ный и ещё жир");
        Dispatcher.UIThread.RunJobs();
        Assert.True(document.IsDirty);

        author.FlushPendingEdits();
        Assert.Equal("<p>Первый <b>жирный и ещё жирный</b> абзац.</p>", XmlSerializer.ToXml(first));
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_SplitsParagraph_KeepingBold_AndMovesCaret()
    {
        var (window, author, document, undo) = Show();
        Focus(window, author, Paragraphs(document)[0], "Первый жир".Length);

        window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var paragraphs = Paragraphs(document);
        Assert.Equal(3, paragraphs.Count);
        Assert.Equal("<p>Первый <b>жир</b></p>", XmlSerializer.ToXml(paragraphs[0]));
        Assert.Equal("<p><b>ный</b> абзац.</p>", XmlSerializer.ToXml(paragraphs[1]));
        Assert.Equal("Разделение блока", Assert.Single(undo));
        Assert.Same(paragraphs[1], author.CurrentNode);
        Assert.Same(author.EditorFor(paragraphs[1]), FocusedEditor(author));
        window.Close();
    }

    [AvaloniaFact]
    public void Backspace_AtStart_MergesWithPrevious_DeleteAtEnd_PullsNext()
    {
        var (window, author, document, _) = Show();
        Focus(window, author, Paragraphs(document)[1], 0);

        window.KeyPress(Key.Back, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var merged = Assert.Single(Paragraphs(document));
        Assert.Equal("Первый жирный абзац.Второй абзац.", merged.InnerText);
        Assert.Equal("Первый жирный абзац.".Length, FocusedEditor(author)!.CaretOffset);

        window.Close();

        (window, author, document, _) = Show();
        var first = Paragraphs(document)[0];
        Focus(window, author, first, "Первый жирный абзац.".Length);
        window.KeyPress(Key.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, Assert.Single(Paragraphs(document)));
        Assert.Equal("<p>Первый <b>жирный</b> абзац.Второй абзац.</p>", XmlSerializer.ToXml(first));
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlB_WrapsSelection_ShiftCtrlSpace_ClearsIt()
    {
        var (window, author, document, undo) = Show();
        var second = Paragraphs(document)[1];
        var editor = Focus(window, author, second, 0);

        editor.Select(0, "Второй".Length);
        window.KeyPress(Key.B, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("<p><b>Второй</b> абзац.</p>", XmlSerializer.ToXml(second));
        Assert.Contains("Оформление <b>", undo);

        editor.Select(0, editor.Document.TextLength);
        window.KeyPress(Key.Space, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("<p>Второй абзац.</p>", XmlSerializer.ToXml(second));
        window.Close();
    }

    [AvaloniaFact]
    public void Tab_IndentsListItem_ShiftTab_Outdents()
    {
        var (window, author, document, undo) = Show();
        var ul = document.Root.FirstElement("conbody")!.FirstElement("ul")!;
        var second = ul.ElementChildren().Last();
        Focus(window, author, second, 0);

        window.KeyPress(Key.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(ul.ElementChildren());
        Assert.Equal("ul", second.Parent!.Name);
        Assert.Same(ul.ElementChildren().First(), second.Parent.Parent);
        Assert.Same(second, author.CurrentNode);
        Assert.NotNull(author.EditorFor(second));
        Assert.Same(author.EditorFor(second), FocusedEditor(author));

        window.KeyPress(Key.Tab, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, ul.ElementChildren().Count());
        Assert.Equal(new[] { "Увеличение уровня", "Уменьшение уровня" }, undo);
        window.Close();
    }

    [AvaloniaFact]
    public void MixedContent_TextSegmentsAreEditors_NestedBlocksAreBlocks()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>T</title><conbody>" +
            "<ul><li>начало<ul><li>вложенный</li></ul>конец</li></ul>" +
            "<note><p>абзац в заметке</p></note>" +
            "</conbody></concept>");
        var conbody = document.Root.FirstElement("conbody")!;
        var item = conbody.FirstElement("ul")!.FirstElement("li")!;
        var nested = item.FirstElement("ul")!.FirstElement("li")!;

        var itemEditors = author.Editors.Where(e => ReferenceEquals(e.Node, item)).ToList();
        Assert.Equal(new[] { "начало", "конец" }, itemEditors.Select(e => e.Text));
        Assert.NotNull(author.EditorFor(nested));
        Assert.NotNull(author.EditorFor(conbody.FirstElement("note")!.FirstElement("p")!));
        Assert.DoesNotContain(author.Editors, e => e.Text.Contains(Presentation.Authoring.InlineContent.ChipChar));

        // Правка второго участка не трогает первый и вложенный список.
        itemEditors[1].FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("и ");
        author.FlushPendingEdits();
        Assert.Equal("начало", item.Children[0].Value);
        Assert.Equal("и конец", item.Children[^1].Value);
        Assert.Equal("вложенный", nested.InnerText);

        // Enter в первом участке уносит хвост и вложенный список в новый пункт.
        itemEditors[0].FocusEditor(3);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var items = conbody.FirstElement("ul")!.ElementChildren().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("нач", items[0].InnerText);
        Assert.Equal("ало", items[1].Children[0].Value);
        Assert.Same(items[1], nested.Parent!.Parent);
        window.Close();
    }

    [AvaloniaFact]
    public void StructuralEdits_RebuildOnlyAffectedBlocks()
    {
        // Enter, Backspace и вставка из палитры не пересоздают редакторы соседних блоков —
        // иначе правка в топике из сотен абзацев перерисовывала бы весь документ.
        var (window, author, document, _) = Show();
        var paragraphs = Paragraphs(document);
        var title = author.EditorFor(document.Root.FirstElement("title")!);
        var second = author.EditorFor(paragraphs[1]);
        Focus(window, author, paragraphs[0], 3);

        window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(title, author.EditorFor(document.Root.FirstElement("title")!));
        Assert.Same(second, author.EditorFor(paragraphs[1]));
        Assert.Equal(Paragraphs(document).Select(p => p.InnerText), author.Editors.Where(e => e.Node.Name == "p").Select(e => e.Text));

        Assert.True(author.Surface.InsertElement("note"));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, author.EditorFor(paragraphs[1]));
        var note = document.Root.FirstElement("conbody")!.FirstElement("note")!;
        Assert.Same(author.EditorFor(note), FocusedEditor(author));

        Assert.True(author.Surface.MoveCurrent(up: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, author.EditorFor(paragraphs[1]));
        Assert.Equal(
            document.Root.FirstElement("conbody")!.ElementChildren().Where(n => author.EditorFor(n) is not null).Select(n => n.Name),
            author.Editors.Select(e => e.Node).Where(n => n.Parent?.Name == "conbody").Distinct().Select(n => n.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void ArrowKeys_MoveBetweenBlocks()
    {
        var (window, author, document, _) = Show();
        var paragraphs = Paragraphs(document);
        var first = Focus(window, author, paragraphs[0], 3);

        window.KeyPress(Key.Down, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(author.EditorFor(paragraphs[1]), FocusedEditor(author));

        window.KeyPress(Key.Up, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, FocusedEditor(author));
        Assert.Equal(first.Document.TextLength, first.CaretOffset);
        window.Close();
    }

    [AvaloniaFact]
    public void InsertInlineNode_AddsChipAtCaret()
    {
        var (window, author, document, _) = Show();
        var second = Paragraphs(document)[1];
        Focus(window, author, second, "Второй".Length);

        var xref = DitaNode.Element("xref");
        xref.SetAttribute("href", "other.dita");
        Assert.True(author.Surface.InsertInlineNode(xref));
        Assert.Equal("<p>Второй<xref href=\"other.dita\"/> абзац.</p>", XmlSerializer.ToXml(second));
        Assert.Contains(InlineChar, author.EditorFor(second)!.Text);
        window.Close();
    }

    private const char InlineChar = Presentation.Authoring.InlineContent.ChipChar;

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Screenshots_TaskAndReference(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var root = Path.Combine(RepositoryRoot(), "samples", "GuideSample");
            foreach (var (file, name) in new[] { ("tasks/install.dita", "task"), ("reference/settings.dita", "reference") })
            {
                var document = DitaDocument.Load(Path.Combine(root, file));
                var author = new AuthorView();
                author.Load(document);
                var window = new Window { Width = 1000, Height = 900, Content = author };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotEmpty(author.Editors);

                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(dir);
                frame!.Save(Path.Combine(dir, $"author-{name}-{theme}.png"));
                window.Close();
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln).");
    }
}
