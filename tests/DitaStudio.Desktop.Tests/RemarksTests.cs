using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Замечания (3) в «Авторе»: правки и продукт на словах внутри блока, «изменено» с автором и временем, настройка ссылки по правой кнопке,
/// формулы и символы, курсор перед блоками и между блоками ячейки, выделение блока полем слева, переход к элементу из «Проверки».
/// </summary>
public sealed class RemarksTests
{
    private static (Window Window, AuthorView Author, DitaDocument Document) Show(string xml)
    {
        var document = DitaDocument.Parse(xml);
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = 900, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document);
    }

    private static DitaNode First(DitaDocument document, string name, Func<DitaNode, bool>? where = null) =>
        document.Root.DescendantsAndSelf().First(n => n.Name == name && (where?.Invoke(n) ?? true));

    private static List<string> Notes(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => Equals(t.Tag, "attribute-note")).Select(t => t.Text ?? string.Empty).ToList();

    [AvaloniaFact]
    public void TrackChanges_SelectedWordInTableCell_MarkedWithAuthorAndTime_NoteInCell()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><table><tgroup cols=\"1\"><tbody><row><entry>Одно слово тут</entry></row></tbody></tgroup></table></conbody></concept>");
        var entry = First(document, "entry");
        var editor = author.EditorFor(entry)!;
        editor.FocusEditor(0);
        editor.Select(5, 5); // «слово»
        Dispatcher.UIThread.RunJobs();

        author.Surface.MarkCurrentInserted();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(entry.GetAttribute("status")); // ячейка целиком не помечена
        var ph = Assert.Single(entry.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("слово", ph.InnerText);
        Assert.Equal("new", ph.GetAttribute("status"));
        Assert.False(string.IsNullOrEmpty(ph.GetAttribute("tcauthor")));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", ph.GetAttribute("tcdate")!);

        // Пометка — у ячейки, зелёная, со словом.
        var note = window.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "attribute-note"));
        Assert.Contains("‹слово›", note.Text);
        Assert.Contains("вставлено", note.Text);
        Assert.True(window.TryFindResource("Success", window.ActualThemeVariant, out var success));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)success!).Color, ((Avalonia.Media.ISolidColorBrush)note.Foreground!).Color);

        // Подсказка у слов под указателем — кто и когда.
        var cellEditor = author.EditorFor(entry)!;
        Assert.Contains(ph.GetAttribute("tcauthor")!, cellEditor.MarksTipAt(6));

        // Курсор в помеченных словах — «Принять» снимает пометку и обёртку, текст на месте.
        cellEditor.FocusEditor(7);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(author.Surface.TrackedAtCursor());
        author.Surface.AcceptCurrentTrackedChange();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(entry.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("Одно слово тут", entry.InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public void TrackChanges_MarkBlockChanged_OrangeNoteWithAuthorAndDate()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Абзац.</p></conbody></concept>");
        var p = First(document, "p");
        author.EditorFor(p)!.FocusEditor(2);
        Dispatcher.UIThread.RunJobs();

        author.Surface.MarkCurrentChanged();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("changed", p.GetAttribute("status"));
        var note = window.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "attribute-note"));
        Assert.StartsWith("Изменено · " + p.GetAttribute("tcauthor") + " · ", note.Text);
        Assert.Matches(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}", note.Text!);
        Assert.True(window.TryFindResource("Warning", window.ActualThemeVariant, out var warning));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)warning!).Color, ((Avalonia.Media.ISolidColorBrush)note.Foreground!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void Product_OnSelectedWords_OnlyThoseWords_GrayNoteInBlock()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Общий текст и особый текст.</p></conbody></concept>");
        var p = First(document, "p");
        var editor = author.EditorFor(p)!;
        editor.FocusEditor(0);
        editor.Select(14, 6); // «особый»
        Dispatcher.UIThread.RunJobs();
        Assert.True(author.Surface.HasPartialTextSelection);
        Assert.Null(author.Surface.InlineAttributeTarget()); // своей обёртки ещё нет — панель покажет «черновик»

        var ph = author.Surface.SetSelectionAttributes(new[] { new KeyValuePair<string, string?>("product", "Альфа") });
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(ph);
        Assert.Null(p.GetAttribute("product"));
        Assert.Equal("Альфа", First(document, "ph").GetAttribute("product"));
        Assert.Equal("особый", First(document, "ph").InnerText);
        var note = Assert.Single(Notes(window));
        Assert.Equal("‹особый› product: Альфа", note);

        // Курсор в этих словах — панель «Атрибуты» показывает их ph, а не абзац.
        var fresh = author.EditorFor(p)!;
        fresh.FocusEditor(16);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ph", author.Surface.InlineAttributeTarget()?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickOnLink_OffersLinkSettings_AndRemoveLink()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>См. <xref href=\"https://example.com\" scope=\"external\">сайт</xref> тут.</p></conbody></concept>");
        var p = First(document, "p");
        var editor = author.EditorFor(p)!;
        editor.FocusEditor(5);
        Dispatcher.UIThread.RunJobs();

        var menu = editor.BuildContextMenu(6);
        var items = menu.ItemsSource!.OfType<MenuItem>().ToList();
        var settings = items.First(i => Equals(i.Header, "Настройка ссылки…"));
        settings.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(author.ChipEditor);
        author.ChipEditor!.IsOpen = false;
        Dispatcher.UIThread.RunJobs();

        var unlink = editor.BuildContextMenu(6).ItemsSource!.OfType<MenuItem>().First(i => Equals(i.Header, "Убрать ссылку (оставить текст)"));
        unlink.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(p.Descendants().Where(n => n.Name == "xref"));
        Assert.Equal("См. сайт тут.", p.InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public void EquationAndSymbol_InsertedAtCursor()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Формула: </p></conbody></concept>");
        var p = First(document, "p");
        author.EditorFor(p)!.FocusEditor(9);
        Dispatcher.UIThread.RunJobs();

        Assert.True(author.Surface.InsertInlineFragment(Equations.Build("pythagoras")!));
        Dispatcher.UIThread.RunJobs();
        var equation = First(document, "equation-inline");
        Assert.Equal(3, equation.Descendants().Count(n => n.Name == "sup"));
        Assert.Equal("a2 + b2 = c2", equation.InnerText);

        Assert.True(author.Surface.InsertText(" ©"));
        author.FlushPendingEdits();
        Assert.EndsWith("= c2 ©", p.InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public void InsertPoint_BeforeFirstParagraph_UnderTitle_AndBetweenBlocksInCell()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Заголовок</title><conbody><p>Первый абзац.</p>" +
            "<table><tgroup cols=\"1\"><tbody><row><entry><p>Раз.</p><p>Два.</p></entry></row></tbody></tgroup></table></conbody></concept>");
        var conbody = document.Root.FirstElement("conbody")!;
        var first = conbody.FirstElement("p")!;

        // Щелчок в промежутке между заголовком и первым абзацем — черта перед абзацем.
        window.CaptureRenderedFrame();
        var top = author.ViewFor(first)!.TranslatePoint(new Point(30, 0), window)!.Value;
        var click = new Point(top.X, top.Y - 3);
        window.MouseDown(click, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var point = Assert.IsType<AuthorView.InsertPoint>(author.CurrentInsertPoint);
        Assert.True(point.Before);
        Assert.Same(conbody, point.Parent);
        Assert.Equal(0, point.Index);

        Assert.True(author.TypeAtInsertPoint("Новый"));
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("Новый", conbody.FirstElement("p")!.InnerText);

        // Ячейка из абзацев: черта перед первым, между ними и после последнего.
        var cellParagraphs = First(document, "entry").ElementChildren().ToList();
        Assert.True(author.PlaceInsertPointBefore(cellParagraphs[0]));
        Assert.True(author.PlaceInsertPointAfter(cellParagraphs[0]));
        Assert.True(author.PlaceInsertPointAfter(cellParagraphs[1]));
        Assert.True(author.TypeAtInsertPoint("Три"));
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal(new[] { "Раз.", "Два.", "Три" }, First(document, "entry").ElementChildren().Select(n => n.InnerText).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void MarginClick_SelectsWholeBlock_SecondClickSelectsParent()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>До.</p><ul><li>Пункт один</li><li>Пункт два</li></ul></conbody></concept>");
        var li = First(document, "li");
        window.CaptureRenderedFrame();
        var view = author.EditorFor(li)!;
        var at = view.TranslatePoint(new Point(0, view.Bounds.Height / 2), window)!.Value;
        var margin = new Point(8, at.Y); // поле слева от текста

        window.MouseDown(margin, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(margin, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(li, Assert.Single(author.SelectedBlocks));

        Thread.Sleep(window.PlatformSettings!.GetDoubleTapTime(PointerType.Mouse) + TimeSpan.FromMilliseconds(50));
        window.MouseDown(margin, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(margin, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ul", Assert.Single(author.SelectedBlocks).Name);
        window.Close();
    }

    [AvaloniaFact]
    public void RevealNode_ContainerAndInline_PutCaretForFixing()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><section><title>Раздел</title><p>Текст <xref href=\"x.dita\">ссылки</xref>.</p></section></conbody></concept>");
        var section = First(document, "section");
        Assert.True(author.RevealNode(section));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("title", author.CurrentNode?.Name);

        var xref = First(document, "xref");
        Assert.True(author.RevealNode(xref));
        Dispatcher.UIThread.RunJobs();
        var editor = author.EditorFor(First(document, "p"))!;
        Assert.Equal("ссылки", editor.SelectedText);
        window.Close();
    }
}
