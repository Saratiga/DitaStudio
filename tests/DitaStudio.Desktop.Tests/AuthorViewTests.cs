using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Presentation.Authoring;
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

    /// <summary>Нажатие клавиши с физической клавишей QWERTY — как настоящая клавиатура.</summary>
    private static void Press(Window window, Key key, RawInputModifiers modifiers)
    {
        var physical = key switch
        {
            Key.Enter => PhysicalKey.Enter,
            Key.Back => PhysicalKey.Backspace,
            Key.Delete => PhysicalKey.Delete,
            Key.Tab => PhysicalKey.Tab,
            Key.Up => PhysicalKey.ArrowUp,
            Key.Down => PhysicalKey.ArrowDown,
            Key.Space => PhysicalKey.Space,
            Key.B => PhysicalKey.B,
            _ => PhysicalKey.None
        };
        window.KeyPress(key, modifiers, physical, null);
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

        Press(window, Key.Enter, RawInputModifiers.None);
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

        Press(window, Key.Back, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var merged = Assert.Single(Paragraphs(document));
        Assert.Equal("Первый жирный абзац.Второй абзац.", merged.InnerText);
        Assert.Equal("Первый жирный абзац.".Length, FocusedEditor(author)!.CaretOffset);

        window.Close();

        (window, author, document, _) = Show();
        var first = Paragraphs(document)[0];
        Focus(window, author, first, "Первый жирный абзац.".Length);
        Press(window, Key.Delete, RawInputModifiers.None);
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
        Press(window, Key.B, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("<p><b>Второй</b> абзац.</p>", XmlSerializer.ToXml(second));
        Assert.Contains("Оформление <b>", undo);

        editor.Select(0, editor.Document.TextLength);
        Press(window, Key.Space, RawInputModifiers.Control | RawInputModifiers.Shift);
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

        Press(window, Key.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(ul.ElementChildren());
        Assert.Equal("ul", second.Parent!.Name);
        Assert.Same(ul.ElementChildren().First(), second.Parent.Parent);
        Assert.Same(second, author.CurrentNode);
        Assert.NotNull(author.EditorFor(second));
        Assert.Same(author.EditorFor(second), FocusedEditor(author));

        Press(window, Key.Tab, RawInputModifiers.Shift);
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
        Press(window, Key.Enter, RawInputModifiers.None);
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

        Press(window, Key.Enter, RawInputModifiers.None);
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

        Press(window, Key.Down, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(author.EditorFor(paragraphs[1]), FocusedEditor(author));

        Press(window, Key.Up, RawInputModifiers.None);
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

    [AvaloniaFact]
    public void InsertElement_InlineGoesToCaret_BlockGoesAfter()
    {
        // Замечание: сноска и картинка из палитры вставлялись блоком после абзаца, а не у курсора.
        var (window, author, document, undo) = Show();
        var second = Paragraphs(document)[1];
        var editor = Focus(window, author, second, "Второй".Length);

        Assert.True(author.Surface.InsertElement("fn"));
        Assert.Equal("<p>Второй<fn>Текст сноски</fn> абзац.</p>", XmlSerializer.ToXml(second));
        Assert.Contains("Вставка <fn>", undo);

        // Курсор — в текст сноски в области «Сноски», заготовка выделена, набор её заменяет.
        Dispatcher.UIThread.RunJobs();
        var fn = second.FirstElement("fn")!;
        Assert.Equal("Текст сноски", author.EditorFor(fn)!.SelectedText);
        window.KeyTextInput("Пояснение");
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("<p>Второй<fn>Пояснение</fn> абзац.</p>", XmlSerializer.ToXml(second));

        // Выделенный текст оборачивается целиком.
        var first = Paragraphs(document)[0];
        editor = Focus(window, author, first, 0);
        editor.Select(0, "Первый".Length);
        Assert.True(author.Surface.InsertElement("term"));
        Assert.Equal("<p><term>Первый</term> <b>жирный</b> абзац.</p>", XmlSerializer.ToXml(first));

        // Картинка из палитры — плашкой у курсора, у конца полужирного — рядом с ним.
        Focus(window, author, first, "Первый жирный".Length);
        Assert.True(author.Surface.InsertElement("image"));
        Assert.Equal("<p><term>Первый</term> <b>жирный</b><image/> абзац.</p>", XmlSerializer.ToXml(first));

        // Блочный элемент — по-прежнему после текущего абзаца.
        Focus(window, author, first, 2);
        Assert.True(author.Surface.InsertElement("note"));
        Assert.Equal("note", EditCommands.NextElement(first)!.Name);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Wrappers_HaveFrameAndLabel_ParagraphsDoNot(string theme)
    {
        // Замечание: в «Авторе» не видно, где начинается и кончается div, section, fn.
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var (window, author, document, _) = Show(
                "<concept id=\"c\"><title>Обёртки</title><conbody>" +
                "<p>Абзац со сноской<fn>Текст сноски.</fn> и продолжением.</p>" +
                "<div outputclass=\"warning-box\"><p>Внутри div.</p><ul><li>пункт</li></ul></div>" +
                "<section><title>Раздел</title><sectiondiv><p>Внутри sectiondiv.</p></sectiondiv></section>" +
                "</conbody></concept>");

            Border Frame(string name) => (Border)author.ViewFor(document.Root.DescendantsAndSelf().First(n => n.Name == name))!;
            string? Label(Border border) => ((StackPanel)border.Child!).Children.OfType<TextBlock>().FirstOrDefault()?.Text;

            Assert.Equal(2, Frame("div").BorderThickness.Left);
            Assert.Equal("div · warning-box", Label(Frame("div")));
            Assert.Equal("section", Label(Frame("section")));
            Assert.Equal("sectiondiv", Label(Frame("sectiondiv")));
            Assert.Null(Label(Frame("ul")));
            Assert.Equal(0, Frame("ul").BorderThickness.Left);

            var frame = window.CaptureRenderedFrame();
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, $"author-wrappers-{theme}.png"));
            window.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    [AvaloniaFact]
    public void FontSize_WrapsSelection_ReusesWrapper_WholeBlock_Caret_Reset()
    {
        // Замечание: «печатали 14 пт, дальше надо 10 пт» — без правки XML.
        var (window, author, document, _) = Show();
        var second = Paragraphs(document)[1];
        var editor = Focus(window, author, second, 0);
        const string size = Core.Publishing.TextFormatting.SizePrefix;

        editor.Select(0, "Второй".Length);
        Assert.True(author.Surface.ApplyTextFormat(size, "size-10"));
        author.FlushPendingEdits();
        Assert.Equal("<p><ph outputclass=\"size-10\">Второй</ph> абзац.</p>", XmlSerializer.ToXml(second));

        // Тот же участок ещё раз — меняется класс, без вложенной обёртки.
        editor = Focus(window, author, second, 0);
        editor.Select(0, "Второй".Length);
        author.Surface.ApplyTextFormat(size, "size-14");
        author.FlushPendingEdits();
        Assert.Equal("<p><ph outputclass=\"size-14\">Второй</ph> абзац.</p>", XmlSerializer.ToXml(second));

        // Весь текст блока — класс на самом абзаце, лишняя фраза уходит.
        editor = Focus(window, author, second, 0);
        editor.Select(0, editor.Document.TextLength);
        author.Surface.ApplyTextFormat(size, "size-12");
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("<p outputclass=\"size-12\">Второй абзац.</p>", XmlSerializer.ToXml(second));
        Assert.Equal(16, author.EditorFor(second)!.FontSize);

        // Без выделения — заготовка у курсора, набор идёт уже этим размером.
        editor = Focus(window, author, second, "Второй абзац.".Length);
        author.Surface.ApplyTextFormat(size, "size-8");
        window.KeyTextInput(" Мелко");
        Dispatcher.UIThread.RunJobs();
        author.FlushPendingEdits();
        Assert.Equal("<p outputclass=\"size-12\">Второй абзац.<ph outputclass=\"size-8\"> Мелко</ph></p>", XmlSerializer.ToXml(second));

        // Сброс на выделении — фраза без других атрибутов разворачивается.
        editor = Focus(window, author, second, 0);
        editor.Select("Второй абзац.".Length, " Мелко".Length);
        author.Surface.ApplyTextFormat(size, null);
        author.FlushPendingEdits();
        Assert.Equal("<p outputclass=\"size-12\">Второй абзац. Мелко</p>", XmlSerializer.ToXml(second));
        window.Close();
    }

    [AvaloniaFact]
    public void EnterAtEnd_ShowsSuggestions_DefaultSplits_FilterInserts()
    {
        // Замечание: по Enter — панелька с подсказкой, какой блок будет (как в Oxygen).
        var (window, author, document, undo) = Show();
        var first = Paragraphs(document)[0];
        Focus(window, author, first, InlineContent.FromNode(first).Length);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var popup = Assert.IsType<ElementSuggestions>(author.Suggestions);
        Assert.True(popup.IsOpen);
        Assert.Null(popup.Visible[0].Element);
        Assert.Contains("как обычно", popup.Visible[0].Title);
        Assert.Contains(popup.Visible, s => s.Element == "note");
        Assert.DoesNotContain(popup.Visible, s => s.Element == "b");
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-enter-suggestions.png"));

        // Первая строка — прежний Enter: новый абзац после текущего.
        popup.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, Paragraphs(document).Count);
        Assert.Null(author.Suggestions);

        // Фильтр и выбор элемента.
        var second = Paragraphs(document)[1];
        Focus(window, author, second, 0);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        author.Suggestions!.Filter("note");
        Assert.Equal("note", author.Suggestions.Selected?.Element);
        author.Suggestions.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("note", EditCommands.NextElement(second)!.Name);
        Assert.Contains("Вставка <note>", undo);

        // В конце последнего пункта списка — элементы и после самого списка.
        var lastItem = document.Root.Descendants().Last(n => n.Name == "li");
        Focus(window, author, lastItem, InlineContent.FromNode(lastItem).Length);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(author.Suggestions!.Visible, s => s.Element == "p" && s.Title.EndsWith("после <ul>"));
        author.Suggestions.Filter("после <ul>");
        var p = author.Suggestions.Visible.First(s => s.Element == "p");
        author.Suggestions.Filter(string.Empty);
        author.Suggestions.IsOpen = false; // Esc / щелчок мимо — ничего не меняется
        Dispatcher.UIThread.RunJobs();
        Assert.Same(lastItem, author.CurrentNode);
        Assert.Equal(2, document.Root.Descendants().Count(n => n.Name == "li"));
        Assert.NotNull(p);

        // Флажок выключен — Enter снова сразу создаёт блок.
        AuthorView.EnterSuggestionsEnabled = false;
        try
        {
            Focus(window, author, lastItem, InlineContent.FromNode(lastItem).Length);
            Press(window, Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(author.Suggestions);
            Assert.Equal(3, document.Root.Descendants().Count(n => n.Name == "li"));
        }
        finally
        {
            AuthorView.EnterSuggestionsEnabled = true;
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Footnotes_AreChipsInText_EditedInFootnotesArea(string theme)
    {
        // Замечание: сноски в «Авторе» — не частью сплошного текста, а внизу.
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var (window, author, document, _) = Show(
                "<concept id=\"c\"><title>Сноски</title><conbody>" +
                "<p>Версия ВПО на ЖКИ<fn>Вывод на ЖКИ по умолчанию.</fn> см. рисунок 93.</p>" +
                "<p>Второй<fn>Вторая сноска.</fn> абзац.</p></conbody></concept>");
            var first = Paragraphs(document)[0];
            var fn1 = first.FirstElement("fn")!;
            var fn2 = Paragraphs(document)[1].FirstElement("fn")!;

            // В тексте — плашка с номером, текста сноски в строке нет.
            var text = author.EditorFor(first)!.Text;
            Assert.DoesNotContain("Вывод", text);
            Assert.Contains(InlineChar, text);
            Assert.Equal("сноска 2", InlineContent.DescribeChip(fn2));

            // Внизу — область «Сноски» с редакторами текста сносок.
            Assert.Contains(author.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Сноски");
            var note = author.EditorFor(fn1)!;
            Assert.Equal("Вывод на ЖКИ по умолчанию.", note.Text);
            note.FocusEditor(note.Text.Length);
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput(" Уточнение.");
            Press(window, Key.Enter, RawInputModifiers.None); // Enter в сноске ничего не делает
            Dispatcher.UIThread.RunJobs();
            Assert.Null(author.Suggestions);
            author.FlushPendingEdits();
            Assert.Equal("<p>Версия ВПО на ЖКИ<fn>Вывод на ЖКИ по умолчанию. Уточнение.</fn> см. рисунок 93.</p>", XmlSerializer.ToXml(first));

            // Правка абзаца со сноской не отвязывает её от области «Сноски».
            var editor = Focus(window, author, first, 0);
            window.KeyTextInput("Тут: ");
            author.FlushPendingEdits();
            Assert.Same(fn1, first.FirstElement("fn"));
            note = author.EditorFor(fn1)!;
            note.FocusEditor(0);
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("!");
            author.FlushPendingEdits();
            Assert.Equal("!Вывод на ЖКИ по умолчанию. Уточнение.", fn1.InnerText);

            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"author-footnotes-{theme}.png"));

            // Удалили плашку из абзаца — сноска уходит и из области.
            editor = Focus(window, author, first, 0);
            var chip = editor.Text.IndexOf(InlineChar);
            editor.Document.Remove(chip, 1);
            author.FlushPendingEdits();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(first.FirstElement("fn"));
            Assert.Null(author.EditorFor(fn1));
            Assert.NotNull(author.EditorFor(fn2));
            Assert.Equal("сноска 1", InlineContent.DescribeChip(fn2));
            window.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    [AvaloniaFact]
    public void Images_ShownAsPictures_ResizeWritesWidth()
    {
        // Замечание: изображение в «Авторе» — картинкой, а не надписью, и размер меняется мышью.
        const string png = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAJ0lEQVR4nGM8oaHBMBCAaUBsHbV41OJRi0ctHrV41OJRi0ctHhAAABx5AUDfR9jWAAAAAElFTkSuQmCC";
        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioImageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(png));
        var path = Path.Combine(dir, "topic.dita");
        File.WriteAllText(path, "<concept id=\"c\"><title>Картинки</title><conbody>" +
            "<p>Значок <image href=\"pic.png\" height=\"10px\"/> в строке.</p>" +
            "<fig><image href=\"pic.png\" placement=\"break\"/></fig></conbody></concept>");
        var document = DitaDocument.Load(path);
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, description) => undo.Add(description);
        author.Load(document);
        var window = new Window { Width = 900, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var images = author.GetVisualDescendants().OfType<ResizableImage>().ToList();
        Assert.Equal(2, images.Count);
        var inline = images.First(i => i.FindAncestorOfType<BlockEditor>() is not null);
        Assert.Equal(20, inline.ShownWidth, 1); // height="10px" при пропорциях 2:1
        Assert.DoesNotContain(author.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("🖼") == true);

        // Перетаскивание маркера в углу картинки-блока мышью.
        var block = images.Single(i => !ReferenceEquals(i, inline));
        var before = block.ShownWidth;
        var inside = block.TranslatePoint(new Point(5, 5), window)!.Value;
        window.CaptureRenderedFrame(); // проверка попадания идёт по отрисованному кадру
        window.MouseMove(new Point(1, 1), RawInputModifiers.None);
        window.MouseMove(inside, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var thumb = block.Children.OfType<Border>().Single();
        Assert.True(thumb.IsVisible);
        var corner = thumb.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(corner + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(corner + new Vector(60, 0), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 60, block.ShownWidth, 1);
        Assert.Equal($"{Math.Round(before + 60)}px", document.Root.Descendants().Last(n => n.Name == "image").GetAttribute("width"));

        inline.ResizeTo(64);
        var image = document.Root.Descendants().First(n => n.Name == "image");
        Assert.Equal("64px", image.GetAttribute("width"));
        Assert.Null(image.GetAttribute("height"));
        Assert.True(document.IsDirty);
        Assert.Contains("Размер изображения", undo);
        author.FlushPendingEdits();
        Assert.Contains("<image href=\"pic.png\" width=\"64px\"/>", XmlSerializer.ToXml(document.Root));

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
