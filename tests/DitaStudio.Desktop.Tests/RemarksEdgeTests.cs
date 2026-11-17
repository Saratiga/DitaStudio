using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Validation;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Замечания (3): пограничные случаи «Автора» — разметка остаётся допустимой, курсор и выделение ведут себя предсказуемо.</summary>
public sealed class RemarksEdgeTests
{
    private static (Window Window, AuthorView Author, DitaDocument Document) Show(string xml, double width = 900)
    {
        var document = DitaDocument.Parse(xml);
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = width, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document);
    }

    private static DitaNode First(DitaDocument document, string name) => document.Root.DescendantsAndSelf().First(n => n.Name == name);

    private static void AssertValid(DitaDocument document)
    {
        var errors = new DitaValidator { CheckStyleRules = false }.Validate(document).Where(i => i.Severity == IssueSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => e.Message)) + "\n" + XmlSerializer.ToXml(document.Root));
    }

    private static BlockEditor Select(AuthorView author, DitaNode block, int start, int length)
    {
        var editor = author.EditorFor(block)!;
        editor.FocusEditor(start);
        editor.Select(start, length);
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    private static BlockEditor? FocusedFor(AuthorView author) => author.Editors.FirstOrDefault(e => e.TextArea.IsKeyboardFocusWithin);

    private const string Bold = "<concept id=\"c\"><title>Т</title><conbody><p>Первый <b>жирный</b> абзац.</p></conbody></concept>";

    [AvaloniaFact]
    public void TrackSelection_AcrossBoldBoundary_StaysValid_TextIntact()
    {
        var (window, author, document) = Show(Bold);
        var p = First(document, "p");
        Select(author, p, 3, 7); // «вый жир» — через границу b
        author.Surface.MarkCurrentDeleted();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Первый жирный абзац.", p.InnerText);
        var ph = Assert.Single(p.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("deleted", ph.GetAttribute("status"));
        Assert.Equal("вый жир", ph.InnerText);
        Assert.Null(p.GetAttribute("status"));
        AssertValid(document);

        // Отклонить удаление — слова остаются, обёртки нет, жирный на месте.
        author.EditorFor(p)!.FocusEditor(5);
        Dispatcher.UIThread.RunJobs();
        author.Surface.RejectCurrentTrackedChange();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(p.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("Первый жирный абзац.", p.InnerText);
        Assert.Equal("жирный", First(document, "b").InnerText);
        AssertValid(document);
        window.Close();
    }

    [AvaloniaFact]
    public void TrackSelection_WholeBlockText_MarksBlock_NotWrapper()
    {
        var (window, author, document) = Show(Bold);
        var p = First(document, "p");
        Select(author, p, 0, author.EditorFor(p)!.PlainTextLength);
        author.Surface.MarkCurrentInserted();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("new", p.GetAttribute("status"));
        Assert.Empty(p.Descendants().Where(n => n.Name == "ph"));
        window.Close();
    }

    [AvaloniaFact]
    public void AcceptDeletedWords_RemovesThem_RemarkSameWords_ChangesStatusWithoutNesting()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Раз два три.</p></conbody></concept>");
        var p = First(document, "p");
        Select(author, p, 4, 3); // «два»
        author.Surface.MarkCurrentInserted();
        Dispatcher.UIThread.RunJobs();
        Select(author, p, 4, 3);
        author.Surface.MarkCurrentDeleted();
        Dispatcher.UIThread.RunJobs();
        var ph = Assert.Single(p.Descendants().Where(n => n.Name == "ph")); // та же обёртка, без вложенности
        Assert.Equal("deleted", ph.GetAttribute("status"));

        author.EditorFor(p)!.FocusEditor(5);
        Dispatcher.UIThread.RunJobs();
        author.Surface.AcceptCurrentTrackedChange();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Раз  три.", p.InnerText);
        AssertValid(document);
        window.Close();
    }

    [AvaloniaFact]
    public void ProductThenTrack_SameWords_OneWrapper_AcceptKeepsProduct()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Раз два три.</p></conbody></concept>");
        var p = First(document, "p");
        Select(author, p, 4, 3);
        author.Surface.SetSelectionAttributes(new[] { new KeyValuePair<string, string?>("product", "A") });
        Dispatcher.UIThread.RunJobs();
        Select(author, p, 4, 3);
        author.Surface.MarkCurrentChanged();
        Dispatcher.UIThread.RunJobs();
        var ph = Assert.Single(p.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("A", ph.GetAttribute("product"));
        Assert.Equal("changed", ph.GetAttribute("status"));

        author.EditorFor(p)!.FocusEditor(5);
        Dispatcher.UIThread.RunJobs();
        author.Surface.AcceptCurrentTrackedChange();
        Dispatcher.UIThread.RunJobs();
        ph = Assert.Single(p.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("A", ph.GetAttribute("product"));
        Assert.Null(ph.GetAttribute("status"));

        // Снять последний атрибут — обёртка уходит.
        Select(author, p, 4, 3);
        author.Surface.SetSelectionAttributes(new[] { new KeyValuePair<string, string?>("product", null) });
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(p.Descendants().Where(n => n.Name == "ph"));
        Assert.Equal("Раз два три.", p.InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public void Selection_AtWrappedLineEnd_NoTailBeyondText()
    {
        var text = string.Concat(Enumerable.Repeat("слово на вкладке ", 30));
        var (window, author, document) = Show($"<concept id=\"c\"><title>Т</title><conbody><p>{text.Trim()}</p></conbody></concept>", width: 500);
        var p = First(document, "p");
        var editor = Select(author, p, 0, 0);
        window.CaptureRenderedFrame();
        editor.Select(20, 120); // через несколько строк переноса
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        var rects = editor.SelectionRectangles();
        Assert.True(rects.Count >= 2, "выделение через перенос — несколько строк");
        var view = editor.TextArea.TextView;
        var line = view.VisualLines.Single();
        var space = view.WideSpaceWidth;
        for (var i = 0; i < rects.Count - 1; i++)
        {
            // Строка с переносом: подсветка кончается у последнего знака (плюс пробел), не уходит к краю.
            var row = line.TextLines[i];
            Assert.True(rects[i].Right <= row.Width + space + 1, $"строка {i}: {rects[i].Right} > {row.Width} + {space}");
            Assert.True(rects[i].Width > 0);
        }

        Assert.Equal(Avalonia.Media.Brushes.Transparent, editor.TextArea.SelectionBrush); // штатный слой не рисует поверх
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlEquals_Subscript_CtrlShiftEquals_Superscript()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>H2O и x2</p></conbody></concept>");
        var p = First(document, "p");
        Select(author, p, 1, 1);
        window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, null);
        Dispatcher.UIThread.RunJobs();
        Select(author, p, 7, 1);
        window.KeyPress(Key.OemPlus, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Equal, null);
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("2", First(document, "sub").InnerText);
        Assert.Equal("2", First(document, "sup").InnerText);
        window.Close();
    }

    [AvaloniaFact]
    public void EquationOverSelection_ReplacesIt_NewEquationHasSelectedPlaceholder()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Было ЗАМЕНИ тут.</p></conbody></concept>");
        var p = First(document, "p");
        Select(author, p, 5, 6);
        Assert.True(author.Surface.InsertInlineFragment(Equations.Build("circle")!));
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("Было A = πr2 тут.", p.InnerText);
        AssertValid(document);

        var editor = author.EditorFor(p)!;
        editor.FocusEditor(editor.PlainTextLength);
        Assert.True(author.Surface.InsertInlineElement(DitaNode.Element("equation-inline"), "Место для уравнения"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Место для уравнения", author.EditorFor(p)!.SelectedText);
        author.FlushPendingEdits();
        Assert.Equal(2, p.Descendants().Count(n => n.Name == "equation-inline"));
        AssertValid(document);
        window.Close();
    }

    [AvaloniaFact]
    public void BeforeInsertPoint_PaletteInsertGoesBefore_EnterMenuOffersBefore_ArrowsLeave()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Первый.</p><p>Второй.</p></conbody></concept>");
        var conbody = First(document, "conbody");
        var first = conbody.FirstElement("p")!;
        Assert.True(author.PlaceInsertPointBefore(first));
        Assert.Same(first, author.CurrentNode);

        Assert.True(author.Surface.InsertElement("note"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "note", "p", "p" }, conbody.ElementChildren().Select(n => n.Name).ToArray());

        Assert.True(author.PlaceInsertPointBefore(conbody.ElementChildren().ElementAt(1)));
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(author.InsertMenu);
        author.InsertMenu!.IsOpen = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(author.PlaceInsertPointBefore(first));
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(author.CurrentInsertPoint);
        Assert.Same(first, author.CurrentNode);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickOnParagraphPadding_PutsCaret_LeftStripSelectsBlock()
    {
        var (window, author, document) = Show("<concept id=\"c\"><title>Т</title><conbody><p>Абзац с текстом.</p><p>Второй.</p></conbody></concept>");
        var p = First(document, "p");
        window.CaptureRenderedFrame();
        var row = author.ViewFor(p)!;
        var border = row as Border ?? row.GetVisualDescendants().OfType<Border>().First(b => ReferenceEquals(b.Tag, p));

        // Верхнее поле строки над текстом — курсор в текст, не выделение блока.
        var pad = border.TranslatePoint(new Point(60, 1), window)!.Value;
        window.MouseDown(pad, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(pad, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(author.SelectedBlocks);
        Assert.Same(p, author.CurrentNode);
        Assert.True(author.EditorFor(p)!.TextArea.IsFocused, "фокус: " + window.FocusManager?.GetFocusedElement() + " caret=" + author.EditorFor(p)!.CaretOffset + " same=" + ReferenceEquals(author.EditorFor(p), FocusedFor(author)));

        // Левая полоса рамки — выделение блока.
        Thread.Sleep(window.PlatformSettings!.GetDoubleTapTime(PointerType.Mouse) + TimeSpan.FromMilliseconds(50));
        var strip = border.TranslatePoint(new Point(1, border.Bounds.Height / 2), window)!.Value;
        window.MouseDown(strip, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(strip, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(p, Assert.Single(author.SelectedBlocks));

        // Shift+щелчок по полю у второго абзаца — оба блока.
        Thread.Sleep(window.PlatformSettings!.GetDoubleTapTime(PointerType.Mouse) + TimeSpan.FromMilliseconds(50));
        var second = author.EditorFor(document.Root.Descendants().Where(n => n.Name == "p").Last())!;
        var at = second.TranslatePoint(new Point(0, second.Bounds.Height / 2), window)!.Value;
        var margin = new Point(8, at.Y);
        window.MouseDown(margin, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(margin, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(author.SelectedBlocks.Count == 2, string.Join(",", author.SelectedBlocks.Select(b => b.Name + ":" + b.InnerText)));
        window.Close();
    }

    [AvaloniaFact]
    public void LinkSettings_ChangeTextAndHref_TextLinkRebuilt()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>См. <xref href=\"a.dita\">старый</xref> тут.</p></conbody></concept>");
        var p = First(document, "p");
        var editor = author.EditorFor(p)!;
        editor.FocusEditor(5);
        Dispatcher.UIThread.RunJobs();
        var item = editor.BuildContextMenu(6).ItemsSource!.OfType<MenuItem>().First(i => Equals(i.Header, "Настройка ссылки…"));
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("старый", ((TextBox)author.ChipEditor!.Editors["text"]).Text);
        author.ChipEditor.SetValue("text", "новый текст");
        author.ChipEditor.SetValue("href", "b.dita");
        author.ChipEditor.Commit();
        Dispatcher.UIThread.RunJobs();

        var xref = First(document, "xref");
        Assert.Equal("новый текст", xref.InnerText);
        Assert.Equal("b.dita", xref.GetAttribute("href"));
        Assert.Equal("См. новый текст тут.", p.InnerText);
        Assert.Contains("новый текст", author.EditorFor(p)!.Text);

        // Не по ссылке — пунктов ссылки нет.
        var plain = author.EditorFor(p)!.BuildContextMenu(1).ItemsSource!.OfType<MenuItem>();
        Assert.DoesNotContain(plain, i => Equals(i.Header, "Настройка ссылки…"));
        window.Close();
    }

    [AvaloniaFact]
    public void ProductNote_LongWordsTruncated_FootnoteTextNotListed()
    {
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Текст <ph product=\"A B\">очень длинная фраза для проверки сокращения</ph>" +
            "<fn>Сноска <ph product=\"Z\">внутри</ph></fn>.</p></conbody></concept>");
        var note = AuthorView.AttributeNote(First(document, "p"))!;
        Assert.True(note.StartsWith("‹очень длинная фраза для…› product: A, B"), note);
        Assert.DoesNotContain("Z", note); // слова сноски — в области «Сноски», не в пометке абзаца
        window.Close();
    }

    [AvaloniaFact]
    public void Screenshot_TrackedWords_AndWrappedSelection()
    {
        var text = string.Concat(Enumerable.Repeat("Длинное предложение продолжается на вкладке ", 6)).Trim();
        var (window, author, document) = Show(
            "<concept id=\"c\"><title>Т</title><conbody>" +
            "<p>Слова <ph status=\"new\" tcauthor=\"Иванов\" tcdate=\"2026-11-17 14:05\">вставлены</ph>, " +
            "<ph status=\"deleted\" tcauthor=\"Петров\" tcdate=\"2026-11-17 14:06\">удалены</ph> и " +
            "<ph status=\"changed\" tcauthor=\"Сидоров\" tcdate=\"2026-11-17 14:07\">изменены</ph>; " +
            "<ph product=\"Альфа\">только для Альфы</ph>.</p>" +
            "<table><tgroup cols=\"2\"><tbody><row><entry>Ячейка <ph status=\"new\" tcauthor=\"Иванов\" tcdate=\"2026-11-17 14:05\">новое</ph></entry><entry>Вторая</entry></row></tbody></tgroup></table>" +
            "<p>" + text + "</p></conbody></concept>", width: 620);
        var last = document.Root.Descendants().Where(n => n.Name == "p").Last();
        var editor = author.EditorFor(last)!;
        editor.FocusEditor(0);
        editor.Select(30, 90);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame()!;
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, "remarks-tracked-words.png"));
        window.Close();
    }
}
