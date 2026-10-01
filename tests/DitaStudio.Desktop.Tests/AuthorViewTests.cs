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
using DitaStudio.Core.Schema;
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
            Key.Escape => PhysicalKey.Escape,
            Key.C => PhysicalKey.C,
            Key.X => PhysicalKey.X,
            Key.V => PhysicalKey.V,
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
    public void Div_TextIsEditable_EmptyDivAcceptsTyping_InsertedDivGetsCaret()
    {
        // div, bodydiv, sectiondiv по DITA 1.3 — смешанное содержимое. Раньше текст прямо в div
        // в «Авторе» не показывался вовсе, а в пустой div (в том числе только что вставленный)
        // напечатать было некуда: у блока не было ни одного поля ввода.
        var (window, author, document, _) = Show(
            "<topic id=\"t\"><title>T</title><body>" +
            "<div>Текст в div и <b>жирное</b>.<p>Абзац.</p></div>" +
            "<div/>" +
            "<section><sectiondiv/></section>" +
            "</body></topic>");
        var body = document.Root.FirstElement("body")!;
        var divs = body.ElementChildren().Where(n => n.Name == "div").ToList();
        var sectiondiv = body.FirstElement("section")!.FirstElement("sectiondiv")!;

        var textEditor = Assert.Single(author.Editors, e => ReferenceEquals(e.Node, divs[0]));
        Assert.Equal("Текст в div и жирное.", textEditor.Text);
        Assert.NotNull(author.EditorFor(divs[0].FirstElement("p")!));

        var emptyEditor = Assert.Single(author.Editors, e => ReferenceEquals(e.Node, divs[1]));
        emptyEditor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("Новый текст");
        author.FlushPendingEdits();
        Assert.Equal("<div>Новый текст</div>", XmlSerializer.ToXml(divs[1]));

        Assert.Single(author.Editors, e => ReferenceEquals(e.Node, sectiondiv));

        // Вставка div из палитры — курсор сразу в новом блоке, можно печатать.
        Focus(window, author, body.FirstElement("div")!.FirstElement("p")!, 0);
        Assert.True(author.Surface.InsertElement("div"));
        Dispatcher.UIThread.RunJobs();
        var inserted = FocusedEditor(author);
        Assert.NotNull(inserted);
        Assert.Equal("div", inserted!.Node.Name);
        window.KeyTextInput("вставлено");
        author.FlushPendingEdits();
        Assert.Equal("вставлено", inserted.Node.InnerText);
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

    /// <summary>В2: кнопки размера, цвета и выравнивания действуют на блок под курсором любого текстового вида,
    /// а не только на абзац: пункт списка, термин и определение, ячейка, команда шага, абзац в заметке.</summary>
    [AvaloniaFact]
    public void TextFormat_WorksOnAnyTextBlock()
    {
        var (window, author, document, _) = Show(
            "<task id=\"t\"><title>Т</title><taskbody>" +
            "<context><ul><li>Пункт</li></ul><dl><dlentry><dt>Термин</dt><dd>Определение</dd></dlentry></dl>" +
            "<note><p>В заметке</p></note>" +
            "<table><tgroup cols=\"1\"><tbody><row><entry>Ячейка</entry></row></tbody></tgroup></table></context>" +
            "<steps><step><cmd>Команда</cmd></step></steps></taskbody></task>");
        const string size = Core.Publishing.TextFormatting.SizePrefix;
        const string color = Core.Publishing.TextFormatting.ColorPrefix;

        foreach (var name in new[] { "li", "dt", "dd", "entry", "cmd" })
        {
            var node = document.Root.DescendantsAndSelf().First(n => n.Name == name);
            var editor = Focus(window, author, node, 0);
            editor.Select(0, editor.Document.TextLength);
            Assert.True(author.Surface.ApplyTextFormat(size, "size-18"), name + ": размер");
            editor = Focus(window, author, node, 0);
            editor.Select(0, editor.Document.TextLength);
            Assert.True(author.Surface.ApplyTextFormat(color, "color-red"), name + ": цвет");
            Assert.True(author.Surface.SetCurrentBlockFormat(Core.Publishing.TextFormatting.AlignPrefix, "align-center") || name == "entry", name + ": выравнивание");
            author.FlushPendingEdits();
            Dispatcher.UIThread.RunJobs();
            var classes = node.GetAttribute("outputclass") ?? string.Empty;
            Assert.Contains("size-18", classes);
            Assert.Contains("color-red", classes);
            Assert.Equal(24, author.EditorFor(node)!.FontSize);
        }

        var inNote = document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.Parent?.Name == "note");
        var noteEditor = Focus(window, author, inNote, 0);
        noteEditor.Select(0, noteEditor.Document.TextLength);
        Assert.True(author.Surface.ApplyTextFormat(size, "size-14"));
        author.FlushPendingEdits();
        Assert.Equal("size-14", inNote.GetAttribute("outputclass"));
        window.Close();
    }

    /// <summary>В7: изображение вставляется рисунком (fig с названием), из абзаца оформляется как рисунок,
    /// а подпись «Рисунок N.» видна в «Авторе».</summary>
    [AvaloniaFact]
    public void Figure_InsertAndWrap_MakeFigWithTitle_AndShowNumber()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Р</title><conbody><p>Текст.</p><p><image href=\"a.png\" placement=\"break\"/></p></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var texts = Paragraphs(document);

        // «Оформить как рисунок»: изображение уходит в fig после абзаца, пустой абзац убирается.
        Focus(window, author, texts[1], 0);
        Assert.True(author.Surface.WrapImageAsFigure());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "p", "fig" }, body.ElementChildren().Select(n => n.Name).ToArray());
        var figure = body.FirstElement("fig")!;
        Assert.Equal(new[] { "title", "image" }, figure.ElementChildren().Select(n => n.Name).ToArray());
        Assert.Equal(AuthorSurfaceBase.FigureTitlePlaceholder, figure.FirstElement("title")!.InnerText);
        Assert.Equal("a.png", figure.FirstElement("image")!.GetAttribute("href"));
        Assert.Null(figure.FirstElement("image")!.GetAttribute("placement"));
        Assert.Contains("Оформление изображения как рисунка", undo);

        // Изображения в абзаце нет — команда отказывает и ничего не меняет.
        Focus(window, author, texts[0], 0);
        var before = body.ToString();
        Assert.False(author.Surface.WrapImageAsFigure());
        Assert.Equal(before, body.ToString());

        // Вставка нового рисунка после текущего блока.
        var image = DitaNode.Element("image");
        image.SetAttribute("href", "b.png");
        Focus(window, author, texts[0], 0);
        Assert.True(author.Surface.InsertFigure(image));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "p", "fig", "fig" }, body.ElementChildren().Select(n => n.Name).ToArray());
        Assert.Equal("b.png", body.ElementChildren().ElementAt(1).FirstElement("image")!.GetAttribute("href"));

        // Подпись «Рисунок N.» в «Авторе»: по порядку в топике.
        var badges = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is not null && t.StartsWith("Рисунок ")).ToList();
        Assert.Equal(new[] { "Рисунок 1.", "Рисунок 2." }, badges);
        window.Close();
    }

    private const string InsertMenuTopic =
        "<concept id=\"c\"><title>Т</title><conbody>" +
        "<div><p>Первый div.</p></div><div><p>Второй div.</p></div>" +
        "<table><tgroup cols=\"1\"><tbody><row><entry>Ячейка</entry></row></tbody></tgroup></table>" +
        "</conbody></concept>";

    /// <summary>В12: Ctrl+Enter в ячейке — меню допустимых элементов; выйти из таблицы можно выбором «после table».</summary>
    [AvaloniaFact]
    public void CtrlEnter_InTableCell_OffersElementsAfterTable_NotAfterCellsOrRows()
    {
        var (window, author, document, _) = Show(InsertMenuTopic);
        var body = document.Root.FirstElement("conbody")!;
        var entry = document.Root.DescendantsAndSelf().First(n => n.Name == "entry");
        Focus(window, author, entry, 0);
        Press(window, Key.Enter, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        var menu = author.InsertMenu;
        Assert.NotNull(menu);
        var titles = menu!.Visible.Select(i => i.Title).ToList();
        Assert.Contains(titles, t => t.Contains("<p>") && t.Contains("после <table>"));
        Assert.DoesNotContain(titles, t => t.Contains("после <entry>") || t.Contains("после <row>") || t.Contains("после этого блока"));

        menu.Filter("после <table>");
        var paragraph = menu.Visible.First(i => i.Element == "p");
        menu.Select(paragraph);
        menu.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "div", "div", "table", "p" }, body.ElementChildren().Select(n => n.Name).ToArray());
        window.Close();
    }

    /// <summary>В12: двойной щелчок по свободному месту под последним блоком и между блоками — то же меню.</summary>
    [AvaloniaFact]
    public void DoubleClickOnFreeSpace_ShowsInsertMenu_UnderLastBlock_AndBetweenBlocks()
    {
        var (window, author, document, _) = Show(InsertMenuTopic);
        var body = document.Root.FirstElement("conbody")!;
        var panelOf = author.GetVisualDescendants().OfType<StackPanel>().First(sp => sp.GetVisualDescendants().OfType<BlockEditor>().Any());

        // Под последней таблицей: место после таблицы (внешний блок), не «после ячейки».
        var bottom = author.Editors.Max(e => e.TranslatePoint(new Point(0, e.Bounds.Height), panelOf)?.Y ?? 0);
        Assert.True(author.ShowInsertMenuAt(new Point(40, bottom + 40)));
        Dispatcher.UIThread.RunJobs();
        var titles = author.InsertMenu!.Visible.Select(i => i.Title).ToList();
        Assert.Contains(titles, t => t.Contains("после <table>"));
        Assert.DoesNotContain(titles, t => t.Contains("после <entry>") || t.Contains("после <row>"));
        author.InsertMenu.Filter("<note>");
        author.InsertMenu.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "div", "div", "table", "note" }, body.ElementChildren().Select(n => n.Name).ToArray());

        // Между двумя div: точка у верхней кромки второго div — ближайший блок сверху это первый div.
        var second = author.EditorFor(body.ElementChildren().ElementAt(1).FirstElement("p")!)!;
        var top = second.TranslatePoint(new Point(0, 0), panelOf)!.Value.Y;
        Assert.True(author.ShowInsertMenuAt(new Point(40, top - 2)));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(author.InsertMenu!.Visible.Select(i => i.Title), t => t.Contains("после <div>"));
        author.InsertMenu.Cancel();
        window.Close();
    }

    /// <summary>В12: настоящий двойной щелчок мыши по пустому месту открывает меню; двойной щелчок в тексте — нет.</summary>
    [AvaloniaFact]
    public void DoubleClick_RealMouse_FreeSpaceOpensMenu_TextDoesNot()
    {
        var (window, author, document, _) = Show(InsertMenuTopic);
        var panelOf = author.GetVisualDescendants().OfType<StackPanel>().First(sp => sp.GetVisualDescendants().OfType<BlockEditor>().Any());
        var bottom = author.Editors.Max(e => e.TranslatePoint(new Point(0, e.Bounds.Height), window)?.Y ?? 0);

        window.MouseDown(new Point(60, bottom + 60), Avalonia.Input.MouseButton.Left);
        window.MouseUp(new Point(60, bottom + 60), Avalonia.Input.MouseButton.Left);
        window.MouseDown(new Point(60, bottom + 60), Avalonia.Input.MouseButton.Left);
        window.MouseUp(new Point(60, bottom + 60), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(author.InsertMenu);
        author.InsertMenu!.Cancel();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(author.InsertMenu);

        var text = author.Editors[0].TranslatePoint(new Point(10, 8), window)!.Value;
        window.MouseDown(text, Avalonia.Input.MouseButton.Left);
        window.MouseUp(text, Avalonia.Input.MouseButton.Left);
        window.MouseDown(text, Avalonia.Input.MouseButton.Left);
        window.MouseUp(text, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(author.InsertMenu);
        window.Close();
    }

    /// <summary>В12: в пустом div меню предлагает вставить элемент внутрь.</summary>
    [AvaloniaFact]
    public void DoubleClick_InsideEmptyDiv_OffersInsert()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Текст.</p><div/></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var div = body.FirstElement("div")!;
        var panelOf = author.GetVisualDescendants().OfType<StackPanel>().First(sp => sp.GetVisualDescendants().OfType<BlockEditor>().Any());
        var view = author.ViewFor(div)!;
        var origin = view.TranslatePoint(new Point(0, 0), panelOf)!.Value;
        Assert.True(author.ShowInsertMenuAt(new Point(origin.X + 4, origin.Y + Math.Max(1, view.Bounds.Height / 2))));
        Assert.Contains(author.InsertMenu!.Visible.Select(i => i.Title), t => t.Contains("внутрь <div>"));
        author.InsertMenu.Filter("внутрь");
        author.InsertMenu.Select(author.InsertMenu.Visible.First(i => i.Element == "p"));
        author.InsertMenu.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("p", div.ElementChildren().Single().Name);
        window.Close();
    }

    /// <summary>В2: размер, цвет и выравнивание контейнера (note, section, div) — щелчок по рамке и кнопка;
    /// класс встаёт на сам контейнер, а абзацы внутри в «Авторе» показывают его оформление.</summary>
    [AvaloniaFact]
    public void TextFormat_OnContainer_SetsClassOnContainer_AndChildrenShowIt()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody>" +
            "<note><p>Первый в заметке</p><p>Второй в заметке</p></note>" +
            "<section><title>Раздел</title><p>В разделе</p></section>" +
            "<p>Снаружи</p></conbody></concept>");
        var note = document.Root.DescendantsAndSelf().First(n => n.Name == "note");
        var section = document.Root.DescendantsAndSelf().First(n => n.Name == "section");
        var inside = note.ElementChildren().ToList();
        var outside = document.Root.FirstElement("conbody")!.ElementChildren().Last();

        // Щелчок по рамке выбирает контейнер (у него нет своего редактора).
        var frame = (Avalonia.Controls.Border)author.ViewFor(note)!;
        var point = frame.TranslatePoint(new Point(1, frame.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(note, author.CurrentNode);

        Assert.True(author.Surface.ApplyTextFormat(Core.Publishing.TextFormatting.SizePrefix, "size-18"));
        Assert.True(author.Surface.ApplyTextFormat(Core.Publishing.TextFormatting.ColorPrefix, "color-red"));
        Assert.True(author.Surface.SetCurrentBlockFormat(Core.Publishing.TextFormatting.AlignPrefix, "align-center"));
        author.FlushPendingEdits();
        Dispatcher.UIThread.RunJobs();
        var classes = note.GetAttribute("outputclass") ?? string.Empty;
        Assert.Contains("size-18", classes);
        Assert.Contains("color-red", classes);
        Assert.Contains("align-center", classes);
        Assert.All(inside, p => Assert.Null(p.GetAttribute("outputclass")));
        Assert.All(inside, p =>
        {
            var editor = author.EditorFor(p)!;
            Assert.Equal(24, editor.FontSize);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, editor.HorizontalAlignment);
        });
        Assert.NotEqual(24, author.EditorFor(outside)!.FontSize);

        // Раздел — так же; снять размер с контейнера можно тем же списком.
        author.CurrentNode = section;
        Assert.True(author.Surface.ApplyTextFormat(Core.Publishing.TextFormatting.SizePrefix, "size-14"));
        Assert.Equal("size-14", section.GetAttribute("outputclass"));
        author.CurrentNode = note;
        Assert.True(author.Surface.ApplyTextFormat(Core.Publishing.TextFormatting.SizePrefix, null));
        Assert.DoesNotContain("size-", note.GetAttribute("outputclass") ?? string.Empty);
        window.Close();
    }

    /// <summary>Г13: щелчок по свободному месту снимает выделение — и рамки выбранного контейнера, и выделенного текста;
    /// переход в другой блок снимает выделение текста в прежнем.</summary>
    [AvaloniaFact]
    public void Click_OutsideSelection_ClearsIt()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody>" +
            "<note><p>Первый в заметке</p><p>Второй в заметке</p></note>" +
            "<p>Снаружи один</p><p>Снаружи два</p></conbody></concept>");
        var note = document.Root.DescendantsAndSelf().First(n => n.Name == "note");
        var body = document.Root.FirstElement("conbody")!;
        var outsideOne = body.ElementChildren().ElementAt(1);
        var outsideTwo = body.ElementChildren().ElementAt(2);
        var bottom = author.Editors.Max(e => e.TranslatePoint(new Point(0, e.Bounds.Height), window)?.Y ?? 0);
        var free = new Point(80, bottom + 80);

        // Рамка контейнера выбрана — щелчок по пустому месту её снимает.
        var frame = (Avalonia.Controls.Border)author.ViewFor(note)!;
        var onFrame = frame.TranslatePoint(new Point(1, frame.Bounds.Height / 2), window)!.Value;
        window.MouseDown(onFrame, Avalonia.Input.MouseButton.Left);
        window.MouseUp(onFrame, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(note, author.CurrentNode);
        window.MouseDown(free, Avalonia.Input.MouseButton.Left);
        window.MouseUp(free, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(note, author.CurrentNode);

        // Выделенный текст: щелчок по пустому месту снимает.
        var otherFree = new Point(400, bottom + 120); // другая точка: тот же щелчок подряд был бы двойным
        var editor = Focus(window, author, outsideOne, 0);
        editor.Select(0, 4);
        Assert.False(editor.TextArea.Selection.IsEmpty);
        window.MouseDown(otherFree, Avalonia.Input.MouseButton.Left);
        window.MouseUp(otherFree, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.Selection.IsEmpty);

        // Выделили в одном блоке и перешли в другой — прежнее выделение снято.
        editor.Select(0, 4);
        Focus(window, author, outsideTwo, 0);
        Assert.True(editor.TextArea.Selection.IsEmpty);
        window.Close();
    }

    /// <summary>Г15: полоса прокрутки «Автора» следует за курсором — и при переходе в блок далеко за экраном, и внутри длинного абзаца.</summary>
    [AvaloniaFact]
    public void ScrollBar_FollowsCaret_ToFarBlock_AndInsideLongParagraph()
    {
        var paragraphs = string.Concat(Enumerable.Range(1, 80).Select(i => $"<p>Абзац номер {i}.</p>"));
        var longText = string.Join(" ", Enumerable.Repeat("длинный абзац с множеством слов, чтобы он занял много строк", 120));
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Первый.</p><p>" + longText + "</p>" + paragraphs + "</conbody></concept>");
        window.Height = 500;
        Dispatcher.UIThread.RunJobs();
        var scroll = author.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is Panel && v.GetVisualDescendants().OfType<BlockEditor>().Any());
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height * 3);
        Assert.Equal(0, scroll.Offset.Y);

        bool Visible(BlockEditor editor, double y)
        {
            var top = editor.TranslatePoint(new Point(0, y), scroll)!.Value.Y;
            return top >= -1 && top <= scroll.Viewport.Height + 1;
        }

        // Перешли в блок далеко внизу — он в поле зрения.
        var far = author.EditorFor(Paragraphs(document)[70])!;
        far.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);
        Assert.True(Visible(far, 0), "блок далеко внизу должен быть виден");

        // Внутри длинного абзаца курсор ушёл в конец — строка с курсором видна.
        var longEditor = author.EditorFor(Paragraphs(document)[1])!;
        longEditor.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        longEditor.FocusEditor(longEditor.Content.Length);
        Dispatcher.UIThread.RunJobs();
        var caret = longEditor.TextArea.Caret.CalculateCaretRectangle();
        var caretTop = longEditor.TextArea.TextView.TranslatePoint(new Point(0, caret.Y - longEditor.TextArea.TextView.ScrollOffset.Y + caret.Height / 2), scroll)!.Value.Y;
        Assert.True(caretTop >= 0 && caretTop <= scroll.Viewport.Height, $"курсор в конце длинного абзаца виден: {caretTop} из {scroll.Viewport.Height}");
        window.Close();
    }

    // Граф «что можно вставить в что» по каталогу (поиск в ширину от корней топиков), считается один раз: имя → его предок на кратчайшем пути.
    private static readonly Lazy<Dictionary<string, string?>> ShortestPaths = new(() =>
    {
        var catalog = DitaCatalog.Default;
        var previous = new Dictionary<string, string?>();
        var queue = new Queue<string>();
        foreach (var root in catalog.TopicTypes.Select(t => t.Name).Where(n => n is "concept" or "task" or "reference" or "topic" or "troubleshooting" or "glossentry"))
        {
            previous[root] = null;
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            var node = catalog.CreateElement(name);
            var count = DitaCatalog.ChildNames(node).Count;
            for (var index = 0; index <= count; index++)
            {
                foreach (var candidate in catalog.InsertableAt(node, index))
                {
                    if (!previous.ContainsKey(candidate.Name))
                    {
                        previous[candidate.Name] = name;
                        queue.Enqueue(candidate.Name);
                    }
                }
            }
        }

        return previous;
    });

    // Путь от корня топика до элемента: корень → … → родитель → элемент; null — до него из корня не добраться.
    private static List<string>? PathTo(string target)
    {
        var previous = ShortestPaths.Value;
        if (!previous.ContainsKey(target))
        {
            return null;
        }

        var path = new List<string>();
        for (var at = (string?)target; at is not null; at = previous[at])
        {
            path.Insert(0, at);
        }

        return path;
    }

    // Минимальный допустимый топик, в котором есть элемент target; null — до него из корня не добраться.
    private static (DitaDocument Document, DitaNode Target)? DocumentWith(string target, string? parent = null)
    {
        var path = parent is null ? PathTo(target) : PathTo(parent) is { } toParent ? toParent.Append(target).ToList() : null;
        if (path is null)
        {
            return null;
        }

        var root = DitaCatalog.Default.CreateElement(path[0]);
        var current = root;
        foreach (var name in path.Skip(1))
        {
            var child = current.ElementChildren().FirstOrDefault(c => c.Name == name);
            if (child is null)
            {
                var count = DitaCatalog.ChildNames(current).Count;
                for (var index = 0; index <= count && child is null; index++)
                {
                    child = EditCommands.InsertInto(current, name, index);
                }

                if (child is null)
                {
                    return null;
                }
            }

            current = child;
        }

        return (new DitaDocument(root), current);
    }

    // Элементы, которых «Автор» не рисует отдельным блоком ни в каком из допустимых мест: раскладка таблицы (ею управляют команды таблицы,
    // а ячейки выделяются сами), метаданные заголовка и части изображения/кода/фразовых элементов. Список закрытый: появление
    // нового неотображаемого или неудаляемого блока ломает тест.
    private static readonly string[] BlocksNotShownInAuthor =
    {
        "tgroup", "colspec", "thead", "row", "strow", "sthead", "chrow", "chhead", "property", "prophead",
        "alt", "longdescref", "coderef", "navtitle", "searchtitle", "subtitle", "boolean", "state"
    };

    /// <summary>Д4: выделенный целиком блок удаляется клавишей Delete или Backspace — для каждого блочного элемента каталога, а не для
    /// нескольких избранных. Ячейка таблицы очищается, но остаётся; Esc снимает выделение. Блок проверяется в первом допустимом
    /// контексте, где «Автор» его рисует (по кратчайшему пути от корня, затем внутри каждого контейнера каталога).</summary>
    [AvaloniaFact]
    public void SelectedBlock_DeletedWithKey_ForEveryBlockElementOfCatalog()
    {
        var catalog = DitaCatalog.Default;
        var checkedCount = 0;
        var problems = new List<string>();
        var notShown = new List<string>();
        // Контексты, где блок может быть виден, если в кратчайшем (по пути от корня) он не рисуется.
        var containers = new[] { "conbody", "refbody", "taskbody", "body", "section", "fig", "note", "li", "dd", "example", "p", "entry", "context", "info", "stepxmp" }
            .Where(n => catalog.Get(n) is not null).ToList();
        var names = catalog.Elements.Values.Where(e => e.Display is DisplayKind.Block or DisplayKind.Container or DisplayKind.Table or DisplayKind.Empty)
            .Select(e => e.Name).Where(n => !catalog.TopicTypes.Any(t => t.Name == n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        foreach (var name in names)
        {
            // Первый контекст, где блок виден: сначала кратчайший путь, потом внутри каждого контейнера.
            AuthorView? author = null;
            (DitaDocument Document, DitaNode Target)? built = null;
            var anyContext = false;
            foreach (var parent in new string?[] { null }.Concat(containers))
            {
                if (DocumentWith(name, parent) is not { } candidate)
                {
                    continue;
                }

                anyContext = true;
                var view = new AuthorView();
                view.Load(candidate.Document);
                if (view.CanSelectBlock(candidate.Target))
                {
                    author = view;
                    built = candidate;
                    break;
                }
            }

            if (!anyContext)
            {
                continue; // элемент не вставляется в топик (только в карту, метаданные, подсхему…)
            }

            if (built is null)
            {
                notShown.Add(name);
                continue;
            }

            var (document, target) = built.Value;
            var undo = new List<string>();
            author!.BeforeStructuralEdit += (_, description) => undo.Add(description);
            try
            {
                if (!author.SelectBlock(target))
                {
                    problems.Add($"<{name}>: показан, но рамки выделения нет");
                    continue;
                }

                if (!ReferenceEquals(author.SelectedBlock, target))
                {
                    problems.Add($"<{name}>: после выделения SelectedBlock другой");
                    continue;
                }

                var isCell = name is "entry" or "stentry";
                // Клавиша — прямо в «Авторе» (без окна: раскладка для проверки не нужна).
                author.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = name.Length % 2 == 0 ? Key.Delete : Key.Back });
                checkedCount++;
                if (isCell)
                {
                    if (target.Parent is null || target.Children.Count != 0)
                    {
                        problems.Add($"<{name}>: ячейка должна остаться пустой");
                    }
                }
                else if (target.Parent is not null)
                {
                    problems.Add($"<{name}>: блок остался в документе");
                }
                else if (!undo.Any(u => u.Contains($"<{name}>")))
                {
                    problems.Add($"<{name}>: нет шага отмены «Удаление <{name}>» ({string.Join(", ", undo)})");
                }

                if (author.SelectedBlock is not null)
                {
                    problems.Add($"<{name}>: после удаления выделение не снято");
                }
            }
            finally
            {
                author.Load(new DitaDocument(DitaCatalog.Default.CreateElement("concept"))); // освободить представления
            }
        }

        Assert.True(checkedCount > 100, $"проверено блоков: {checkedCount}");
        Assert.True(problems.Count == 0, "Не удаляются: " + string.Join("; ", problems));
        Assert.True(notShown.Except(BlocksNotShownInAuthor).Count() == 0,
            "Не показываются в «Авторе» и не внесены в список исключений: " + string.Join(", ", notShown.Except(BlocksNotShownInAuthor)));
    }

    /// <summary>Д3: маркер — выделенный текст оборачивается в `ph` с классом mark-…; другой цвет заменяет прежний, «Нет цвета» снимает;
    /// весь абзац тоже закрашивается фразой (не классом на абзаце); маркер, цвет и размер независимы.</summary>
    [AvaloniaFact]
    public void Marker_AppliesToSelection_ReplacesAndRemoves_AsInlineElement()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Один два три четыре.</p><p>Второй абзац целиком.</p></conbody></concept>");
        var paragraphs = Paragraphs(document);
        var first = paragraphs[0];
        var editor = Focus(window, author, first, 0);
        editor.Select(5, 3); // «два»
        Assert.True(author.Surface.HasTextSelection);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.MarkPrefix, "mark-yellow"));
        Dispatcher.UIThread.RunJobs();

        var ph = first.ElementChildren().Single(n => n.Name == "ph");
        Assert.Equal("два", ph.InnerText);
        Assert.Equal("mark-yellow", ph.GetAttribute("outputclass"));
        Assert.Null(first.GetAttribute("outputclass"));
        Assert.Equal("Один два три четыре.", first.InnerText);

        // Другой цвет на том же слове — класс заменяется, вложенных обёрток нет.
        editor = author.EditorFor(first)!;
        editor.FocusEditor(5);
        editor.Select(5, 3);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.MarkPrefix, "mark-red"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("mark-red", first.ElementChildren().Single(n => n.Name == "ph").GetAttribute("outputclass"));
        Assert.Empty(first.ElementChildren().Single(n => n.Name == "ph").ElementChildren());

        // Свой цвет — класс по коду.
        editor = author.EditorFor(first)!;
        editor.FocusEditor(5);
        editor.Select(5, 3);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.MarkPrefix, DitaStudio.Core.Publishing.TextFormatting.MarkToken("#ff8800")));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("mark-ff8800", first.ElementChildren().Single(n => n.Name == "ph").GetAttribute("outputclass"));

        // «Нет цвета» — маркер снят, слово на месте.
        editor = author.EditorFor(first)!;
        editor.FocusEditor(5);
        editor.Select(5, 3);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.MarkPrefix, null));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(first.DescendantsAndSelf(), n => (n.GetAttribute("outputclass") ?? string.Empty).Contains("mark-"));
        Assert.Equal("Один два три четыре.", first.InnerText);

        // Весь абзац: маркер — фразой вокруг текста, класса на самом абзаце нет (в DOCX это выделение знаков, а не заливка абзаца).
        var second = paragraphs[1];
        editor = Focus(window, author, second, 0);
        editor.Select(0, editor.Document.TextLength);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.MarkPrefix, "mark-green"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(second.GetAttribute("outputclass"));
        Assert.Equal("mark-green", second.ElementChildren().Single(n => n.Name == "ph").GetAttribute("outputclass"));

        // Маркер и цвет текста — независимые группы: оба на одном слове.
        editor = author.EditorFor(second)!;
        editor.FocusEditor(0);
        editor.Select(0, 6);
        Assert.True(author.Surface.ApplyTextFormat(DitaStudio.Core.Publishing.TextFormatting.ColorPrefix, "color-red"));
        Dispatcher.UIThread.RunJobs();
        var classes = string.Join(" ", second.DescendantsAndSelf().Select(n => n.GetAttribute("outputclass")).Where(c => c is not null));
        Assert.Contains("mark-green", classes);
        Assert.Contains("color-red", classes);

        window.Close();
    }

    /// <summary>Д3: режим кисти — выделение мышью сразу закрашивается; Esc выключает; ластик снимает маркер.</summary>
    [AvaloniaFact]
    public void MarkerPen_PaintsMouseSelection_UntilEscape_AndEraserRemoves()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Раз два три четыре пять шесть.</p></conbody></concept>");
        var paragraph = Paragraphs(document)[0];
        var editor = Focus(window, author, paragraph, 0);
        var changed = 0;
        author.SelectionChanged += (_, _) => changed++;

        Assert.False(author.Surface.MarkerPenActive);
        Assert.True(author.Surface.StartMarkerPen("mark-green"));
        Assert.True(author.Surface.MarkerPenActive);
        Assert.Equal("mark-green", author.Surface.MarkerPenToken);
        Assert.True(changed > 0); // панель инструментов узнаёт о включении

        // Протяжка мышью по тексту: от начала слова «два» до конца «три».
        Point At(int offset)
        {
            var location = editor.TextArea.TextView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(editor.Document.GetLocation(offset)), AvaloniaEdit.Rendering.VisualYPosition.TextMiddle);
            return editor.TextArea.TextView.TranslatePoint(location - editor.TextArea.TextView.ScrollOffset, window)!.Value;
        }

        void Drag(int from, int to)
        {
            window.MouseDown(At(from), Avalonia.Input.MouseButton.Left);
            window.MouseMove(At((from + to) / 2), RawInputModifiers.LeftMouseButton);
            window.MouseMove(At(to), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(to), Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        }

        Drag(5, 13);
        var marked = paragraph.ElementChildren().Where(n => n.Name == "ph").ToList();
        Assert.Single(marked);
        Assert.Equal("mark-green", marked[0].GetAttribute("outputclass"));
        // Границы выделения мышью — по ближайшей букве: закрашено «два три» плюс-минус символ, но не весь абзац и не начало.
        Assert.Contains("три", marked[0].InnerText);
        Assert.DoesNotContain("Раз", marked[0].InnerText);
        Assert.DoesNotContain("шесть", marked[0].InnerText);
        Assert.True(author.Surface.MarkerPenActive); // кисть остаётся включённой

        // Ластик: то же выделение снимает маркер.
        editor = author.EditorFor(paragraph)!;
        Assert.True(author.Surface.StartMarkerPen(null));
        Drag(5, 13);
        Assert.DoesNotContain(paragraph.DescendantsAndSelf(), n => (n.GetAttribute("outputclass") ?? string.Empty).Contains("mark-"));
        Assert.Equal("Раз два три четыре пять шесть.", paragraph.InnerText);

        // Esc выключает кисть; выделение после этого текст не меняет.
        Press(window, Key.Escape, RawInputModifiers.None);
        Assert.False(author.Surface.MarkerPenActive);
        editor = author.EditorFor(paragraph)!;
        Drag(14, 18);
        Assert.DoesNotContain(paragraph.DescendantsAndSelf(), n => (n.GetAttribute("outputclass") ?? string.Empty).Contains("mark-"));
        window.Close();
    }

    /// <summary>Снимки для глаз: маркеры четырёх цветов, свой цвет, вместе с цветом и размером текста — светлая и тёмная темы.</summary>
    [AvaloniaFact]
    public void Marker_Screenshots_LightAndDark()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            try
            {
                var (window, author, _, _) = Show(
                    "<concept id=\"c\"><title>Маркер</title><conbody>" +
                    "<p>Обычный текст, <ph outputclass=\"mark-yellow\">жёлтый маркер</ph>, <ph outputclass=\"mark-green\">зелёный маркер</ph>, " +
                    "<ph outputclass=\"mark-red\">красный маркер</ph> и <ph outputclass=\"mark-blue\">синий маркер</ph>.</p>" +
                    "<p>Свой цвет: <ph outputclass=\"mark-ff8800\">оранжевый</ph>, <ph outputclass=\"mark-c9c9ff\">светло-сиреневый</ph>; " +
                    "вместе с размером: <ph outputclass=\"mark-yellow size-18\">крупный жёлтый</ph> и с цветом текста: <ph outputclass=\"mark-yellow color-red\">красный по жёлтому</ph>.</p>" +
                    "<p>Маркер внутри <b>жирного <ph outputclass=\"mark-green\">слова</ph></b> и в <note><p><ph outputclass=\"mark-yellow\">заметке</ph></p></note></p>" +
                    "</conbody></concept>");
                window.Width = 900;
                window.Height = 420;
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"author-marker-{theme}.png"));
                window.Close();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            }
        }
    }

    // Точка внутри блока (для протяжки и щелчков): левее центра, на середине высоты редактора.
    private static Point InsideBlock(Window window, AuthorView author, DitaNode node) =>
        author.EditorFor(node)!.TranslatePoint(new Point(60, author.EditorFor(node)!.Bounds.Height / 2), window)!.Value;

    private static void DragMouse(Window window, Point from, params Point[] through)
    {
        window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        foreach (var point in through)
        {
            window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        }

        window.MouseUp(through[^1], Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Д5а: протяжка мыши от текста одного блока к тексту другого (вниз и вверх) выделяет блоки целиком — рамками, а не текст;
    /// Delete удаляет все одним шагом отмены, Esc снимает; протяжка внутри одного блока остаётся выделением текста.</summary>
    [AvaloniaFact]
    public void DragAcrossBlocks_SelectsWholeBlocks_DeleteRemovesAll_EscClears()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Первый абзац.</p><p>Второй абзац.</p><p>Третий абзац.</p>" +
            "<p>Четвёртый абзац.</p><p>Пятый абзац.</p></conbody></concept>");
        var ps = Paragraphs(document);

        // Вниз: со 2-го на 4-й.
        DragMouse(window, InsideBlock(window, author, ps[1]), InsideBlock(window, author, ps[2]), InsideBlock(window, author, ps[3]));
        Assert.Equal(new[] { ps[1], ps[2], ps[3] }, author.SelectedBlocks);
        Assert.Null(author.SelectedBlock); // несколько блоков
        Assert.All(author.Editors, e => Assert.True(e.TextArea.Selection.IsEmpty)); // текст не выделен — выделены блоки

        // Вверх от другого блока: выделение заменяется, направление не важно.
        DragMouse(window, InsideBlock(window, author, ps[4]), InsideBlock(window, author, ps[3]), InsideBlock(window, author, ps[2]));
        Assert.Equal(new[] { ps[2], ps[3], ps[4] }, author.SelectedBlocks);

        // Esc снимает; блоки на месте.
        Press(window, Key.Escape, RawInputModifiers.None);
        Assert.Empty(author.SelectedBlocks);
        Assert.Equal(5, Paragraphs(document).Count);

        // Протяжка внутри одного блока — выделение текста, блоки не выделяются.
        var editor = author.EditorFor(ps[0])!;
        var start = editor.TranslatePoint(new Point(40, editor.Bounds.Height / 2), window)!.Value;
        DragMouse(window, start, new Point(start.X + 40, start.Y), new Point(start.X + 90, start.Y));
        Assert.Empty(author.SelectedBlocks);
        Assert.False(editor.TextArea.Selection.IsEmpty);

        // Выделили 2–4 и удалили клавишей: один шаг отмены, остались 1-й и 5-й.
        DragMouse(window, InsideBlock(window, author, ps[1]), InsideBlock(window, author, ps[2]), InsideBlock(window, author, ps[3]));
        Assert.Equal(3, author.SelectedBlocks.Count);
        Press(window, Key.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Первый абзац.", "Пятый абзац." }, Paragraphs(document).Select(n => n.InnerText).ToArray());
        Assert.Equal(1, undo.Count(u => u == "Удаление блоков: 3"));
        Assert.Empty(author.SelectedBlocks);
        window.Close();
    }

    /// <summary>Д5а: Shift+щелчок в другом блоке продолжает диапазон от текущего; протяжка от абзаца внутри заметки наружу берёт заметку
    /// целиком; через границу ячеек таблицы блоки не выделяются (это выделение ячеек).</summary>
    [AvaloniaFact]
    public void ShiftClick_ExtendsRange_NestedBlocksTakeContainer_CellsAreNotBlocks()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Раз.</p><p>Два.</p><note><p>Внутри заметки.</p></note><p>Четыре.</p>" +
            "<table><tgroup cols=\"2\"><tbody><row><entry>Левая</entry><entry>Правая</entry></row></tbody></tgroup></table></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var ps = body.ElementChildren().Where(n => n.Name == "p").ToList(); // Раз, Два, Четыре
        var note = body.FirstElement("note")!;
        var inNote = note.FirstElement("p")!;

        // Курсор в «Раз», Shift+щелчок в «Четыре» — диапазон из четырёх блоков (заметка целиком).
        Focus(window, author, ps[0], 0);
        var click = InsideBlock(window, author, ps[2]);
        window.MouseDown(click, Avalonia.Input.MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(click, Avalonia.Input.MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new DitaNode[] { ps[0], ps[1], note, ps[2] }, author.SelectedBlocks);
        Press(window, Key.Escape, RawInputModifiers.None);

        // Протяжка от абзаца внутри заметки к абзацу после неё: заметка (внешний блок) и «Четыре».
        DragMouse(window, InsideBlock(window, author, inNote), InsideBlock(window, author, ps[2]));
        Assert.Equal(new DitaNode[] { note, ps[2] }, author.SelectedBlocks);
        Press(window, Key.Escape, RawInputModifiers.None);

        // Из ячейки в соседнюю ячейку — блоки не выделяются.
        var cells = body.FirstElement("table")!.DescendantsAndSelf().Where(n => n.Name == "entry").ToList();
        DragMouse(window, InsideBlock(window, author, cells[0]), InsideBlock(window, author, cells[1]));
        Assert.Empty(author.SelectedBlocks);
        window.Close();
    }

    /// <summary>Д5б: «Обернуть в…» — те же окно и список, что у Enter, но только допустимые обёртки для выделенных блоков; результат — один шаг
    /// отмены, блоки внутри нового элемента, он выделен. Enter у выделенных блоков открывает то же окно; без выделения — блок под курсором.</summary>
    [AvaloniaFact]
    public void WrapIn_OffersOnlyValidElements_WrapsSelectedBlocks_ViaPopupAndEnter()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Первый.</p><p>Второй.</p><p>Третий.</p><ul><li>Один</li><li>Два</li></ul></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var ps = Paragraphs(document);

        // Выделили 2 и 3 абзацы; список вариантов — допустимые обёртки.
        DragMouse(window, InsideBlock(window, author, ps[1]), InsideBlock(window, author, ps[2]));
        Assert.Equal(2, author.SelectedBlocks.Count);
        var options = author.WrapOptions();
        Assert.Contains("note", options);
        Assert.Contains("div", options);
        Assert.Contains("section", options);
        Assert.DoesNotContain("ul", options);
        Assert.DoesNotContain("table", options);

        // Enter открывает окно с тем же списком: фильтр, выбор, применение.
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var popup = Assert.IsType<ElementSuggestions>(author.Suggestions);
        Assert.True(popup.IsOpen);
        Assert.Equal(options.OrderBy(n => n).ToList(), popup.Visible.Select(i => i.Element!).OrderBy(n => n).ToList());
        popup.Filter("<note>");
        Assert.Equal("note", popup.Visible[0].Element);
        popup.Apply();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background); // отложенный перенос фокуса в текст не должен снимать выделение обёртки

        var note = body.FirstElement("note")!;
        Assert.Equal(new[] { "Второй.", "Третий." }, note.ElementChildren().Select(n => n.InnerText).ToArray());
        Assert.Equal(new[] { "p", "note", "ul" }, body.ElementChildren().Select(n => n.Name).ToArray());
        Assert.Equal(1, undo.Count(u => u == "Обернуть в <note>"));
        Assert.Same(note, author.SelectedBlock); // обёртка выделена
        Assert.Empty(new DitaStudio.Core.Validation.DitaValidator { CheckStyleRules = false }.Validate(document)
            .Where(i => i.Severity == DitaStudio.Core.Validation.IssueSeverity.Error));

        // Без выделения — блок под курсором: один абзац в div.
        Press(window, Key.Escape, RawInputModifiers.None);
        Focus(window, author, ps[0], 3);
        Assert.True(author.WrapSelection("div"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("div", body.ElementChildren().First().Name);
        Assert.Equal("Первый.", body.ElementChildren().First().InnerText);

        // Пункты списка: ни note, ни div — у списка другая модель.
        author.SelectBlocks(body.FirstElement("ul")!.ElementChildren().First(), body.FirstElement("ul")!.ElementChildren().Last());
        Assert.DoesNotContain("note", author.WrapOptions());
        Assert.DoesNotContain("div", author.WrapOptions());
        window.Close();
    }

    /// <summary>Копирование, вырезание и вставка выделенных блоков: Ctrl+C копирует (текст XML в буфер), Ctrl+X вырезает одним шагом
    /// отмены, Ctrl+V вставляет копии после выделенных блоков; одинаковые id заменяются, вставленное выделяется; вставка в абзаце, если в
    /// буфере блоки, — тоже вставка блоков; недопустимое место отклоняется с пояснением.</summary>
    [AvaloniaFact]
    public async Task Blocks_CopyCutPaste_WithKeys_ValidPlace_UniqueIds_AndStatus()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p id=\"p1\">Первый.</p><p id=\"p2\">Второй.</p><note id=\"n1\"><p>В заметке.</p></note>" +
            "<p id=\"p4\">Четвёртый.</p><ul><li>Один</li><li>Два</li></ul></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var status = new List<string>();
        author.StatusRequested += (_, message) => status.Add(message);
        List<string> Names() => body.ElementChildren().Select(n => n.Name + ":" + n.InnerText).ToList();
        DitaStudio.Core.Editing.BlockClipboard.LastCopiedText = null;

        // Ctrl+C: два блока (абзац и заметка) копируются; документ не меняется, выделение остаётся.
        var ps = Paragraphs(document);
        var note = body.FirstElement("note")!;
        Assert.True(author.SelectBlocks(ps[1], note));
        Press(window, Key.C, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, author.SelectedBlocks.Count);
        Assert.Contains("Скопировано блоков: 2", status[^1]);
        Assert.Contains("<note id=\"n1\">", DitaStudio.Core.Editing.BlockClipboard.LastCopiedText);
        Assert.Equal(5, body.ElementChildren().Count());

        // Ctrl+V после четвёртого абзаца: копии встают за ним, id уникальны, вставленные выделены, один шаг отмены.
        Assert.True(author.SelectBlock(ps[2]));
        Press(window, Key.V, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background); // отложенный перенос фокуса не должен снять выделение
        Assert.Equal(new[] { "p:Первый.", "p:Второй.", "note:В заметке.", "p:Четвёртый.", "p:Второй.", "note:В заметке.", "ul:ОдинДва" }, Names());
        var ids = document.Root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element).Select(n => n.GetAttribute("id")).Where(i => i is not null).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("p2-копия", ids);
        Assert.Contains("n1-копия", ids);
        Assert.True(author.SelectedBlocks.Count == 2, "выделение после вставки");
        Assert.Equal(1, undo.Count(u => u == "Вставка блоков: 2"));
        Assert.Contains("Вставлено блоков: 2", status[^1]);
        Assert.Contains("id заменено", status[^1]);

        // Ctrl+X: блоки вырезаются (в буфере, из документа ушли, одна отмена), и вставляются обратно в другое место.
        var pasted = author.SelectedBlocks.ToList();
        Press(window, Key.X, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, body.ElementChildren().Count());
        Assert.All(pasted, n => Assert.Null(n.Parent));
        Assert.Equal(1, undo.Count(u => u == "Удаление блоков: 2"));
        Assert.Contains("Вырезано блоков: 2", status[^1]);
        var first = Paragraphs(document)[0];
        Assert.True(author.SelectBlock(first));
        Press(window, Key.V, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "p:Первый.", "p:Второй.", "note:В заметке.", "p:Второй.", "note:В заметке.", "p:Четвёртый.", "ul:ОдинДва" }.Length, body.ElementChildren().Count());
        Assert.Equal("p:Первый.", Names()[0]);
        Assert.Equal("note", body.ElementChildren().ElementAt(2).Name);

        // Вставка в абзаце: курсор в тексте, в буфере блоки — блоки встают после абзаца, а не текстом разметки.
        var before = body.ElementChildren().Count();
        var target = Paragraphs(document)[0];
        Focus(window, author, target, 3);
        Press(window, Key.V, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 2, body.ElementChildren().Count());
        Assert.Equal("Первый.", target.InnerText); // текст абзаца не тронут
        Assert.Equal("p", body.ElementChildren().ElementAt(body.IndexOf(target) + 1).Name);

        // Недопустимое место: пункты списка вставляются в список, но не после абзаца в теле.
        var ul = body.FirstElement("ul")!;
        Assert.True(author.SelectBlocks(ul.ElementChildren().First(), ul.ElementChildren().Last()));
        Press(window, Key.C, RawInputModifiers.Control);
        await Task.Delay(50);
        Assert.True(author.SelectBlock(Paragraphs(document)[0]));
        var count = body.ElementChildren().Count();
        Press(window, Key.V, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, body.ElementChildren().Count());
        Assert.Contains("вставить нельзя", status[^1]);

        // …а после пункта списка — тот же список: два пункта добавились.
        Assert.True(author.SelectBlock(ul.ElementChildren().First()));
        Press(window, Key.V, RawInputModifiers.Control);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, ul.ElementChildren().Count());

        // Обычный текст в буфере — не блоки: ничего не вставляется, сообщение.
        DitaStudio.Core.Editing.BlockClipboard.LastCopiedText = "просто слова";
        if (TopLevel.GetTopLevel(author)?.Clipboard is { } systemClipboard)
        {
            await systemClipboard.SetTextAsync("просто слова");
        }

        Assert.False(await author.PasteBlocksAsync());
        Assert.Contains("нет блоков DITA", status[^1]);
        window.Close();
    }

    private const string CellTopic =
        "<concept id=\"c\"><title>Т</title><conbody><p>До таблицы.</p><table><tgroup cols=\"3\">" +
        "<colspec colname=\"c1\" colnum=\"1\"/><colspec colname=\"c2\" colnum=\"2\"/><colspec colname=\"c3\" colnum=\"3\"/>" +
        "<thead><row><entry>Ш1</entry><entry>Ш2</entry><entry>Ш3</entry></row></thead><tbody>" +
        "<row><entry>а1</entry><entry>б1</entry><entry>в1</entry></row><row><entry>а2</entry><entry>б2</entry><entry>в2</entry></row>" +
        "<row><entry>а3</entry><entry>б3</entry><entry>в3</entry></row></tbody></tgroup></table><p>После таблицы.</p></conbody></concept>";

    private static DitaNode CellAt(DitaDocument document, int row, int column) =>
        document.Root.DescendantsAndSelf().First(n => n.Name == "table").DescendantsAndSelf().Where(n => n.Name == "row")
            .ElementAt(row).ElementChildren().ElementAt(column);

    /// <summary>Д10: протяжка мышью от ячейки к ячейке выделяет прямоугольник ячеек (подсветка, а не текст), Shift+щелчок продолжает,
    /// возврат в начальную ячейку — снова текст; Delete очищает, Esc снимает.</summary>
    [AvaloniaFact]
    public void DragAcrossCells_SelectsRectangle_ShiftClickExtends_DeleteClears_EscCancels()
    {
        var (window, author, document, undo) = Show(CellTopic);
        var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");

        // От «а1» к «б2»: прямоугольник 2×2 (строки 1–2, столбцы 0–1 сетки: шапка — строка 0).
        DragMouse(window, InsideBlock(window, author, CellAt(document, 1, 0)), InsideBlock(window, author, CellAt(document, 1, 1)), InsideBlock(window, author, CellAt(document, 2, 1)));
        Assert.Equal(new DitaStudio.Core.Editing.CellRange(1, 2, 0, 1), author.SelectedCells!.Value.Range);
        Assert.Same(table, author.SelectedCells!.Value.Table);
        Assert.Equal(new[] { "а1", "б1", "а2", "б2" }, author.SelectedCellEntries.Select(c => c.InnerText).ToArray());
        Assert.True(author.Surface.HasCellSelection);
        Assert.All(author.Editors, e => Assert.True(e.TextArea.Selection.IsEmpty)); // текст не выделен
        Assert.Empty(author.SelectedBlocks); // это не выделение блоков

        // Протяжка вернулась в начальную ячейку — выделение ячеек снято, дальше выделяется текст.
        author.ClearCellSelection();
        var editor = author.EditorFor(CellAt(document, 1, 0))!;
        var start = editor.TranslatePoint(new Point(10, editor.Bounds.Height / 2), window)!.Value;
        DragMouse(window, start, new Point(start.X + 6, start.Y), new Point(start.X + 12, start.Y));
        Assert.Null(author.SelectedCells);

        // Shift+щелчок в другой ячейке: прямоугольник от текущей до неё.
        Focus(window, author, CellAt(document, 1, 0), 0);
        var click = InsideBlock(window, author, CellAt(document, 3, 2));
        window.MouseDown(click, Avalonia.Input.MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(click, Avalonia.Input.MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new DitaStudio.Core.Editing.CellRange(1, 3, 0, 2), author.SelectedCells!.Value.Range);
        Assert.Equal(9, author.SelectedCellEntries.Count);

        // Esc снимает выделение, ячейки целы.
        Press(window, Key.Escape, RawInputModifiers.None);
        Assert.Null(author.SelectedCells);
        Assert.Equal("а1", CellAt(document, 1, 0).InnerText);

        // Delete очищает выделенные ячейки (одна отмена), сами ячейки остаются.
        DragMouse(window, InsideBlock(window, author, CellAt(document, 2, 1)), InsideBlock(window, author, CellAt(document, 3, 2)));
        Assert.Equal(4, author.SelectedCellEntries.Count);
        Press(window, Key.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "", "", "", "" }, new[] { CellAt(document, 2, 1), CellAt(document, 2, 2), CellAt(document, 3, 1), CellAt(document, 3, 2) }.Select(c => c.InnerText).ToArray());
        Assert.Equal("а2", CellAt(document, 2, 0).InnerText);
        Assert.Equal(1, undo.Count(u => u == "Очистка ячеек"));
        Assert.NotNull(author.SelectedCells); // выделение остаётся на очищенных ячейках
        window.Close();
    }

    /// <summary>Д10: действия над выделенными ячейками: строки и столбцы столько, сколько выделено; объединение; выравнивание — у всех;
    /// меню по правой кнопке; выделение строки и столбца.</summary>
    [AvaloniaFact]
    public void SelectedCells_RowsColumnsMergeAlign_Menu()
    {
        var (window, author, document, undo) = Show(CellTopic);
        var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");
        int Rows() => table.DescendantsAndSelf().Count(n => n.Name == "row");
        int Columns() => table.FirstElement("tgroup")!.ElementChildren().Count(n => n.Name == "colspec");

        // Две строки выделены (а1…в2) — «вставить строку выше» даёт две.
        Assert.True(author.SelectCellRange(table, new DitaStudio.Core.Editing.CellRange(1, 2, 0, 2)));
        Assert.True(author.Surface.EditCurrentTable(DitaStudio.Core.Editing.TableOperation.InsertRowAbove));
        Assert.Equal(6, Rows());
        Assert.Equal("а1", CellAt(document, 3, 0).InnerText);
        Assert.Null(author.SelectedCells); // строки сдвинулись — выделение снято

        // Два столбца → два столбца слева.
        Assert.True(author.SelectCellRange(table, new DitaStudio.Core.Editing.CellRange(0, 5, 1, 2)));
        Assert.True(author.Surface.EditCurrentTable(DitaStudio.Core.Editing.TableOperation.InsertColumnLeft));
        Assert.Equal(5, Columns());

        // Удаление выделенных строк: две строки уходят.
        Assert.True(author.SelectCellRange(table, new DitaStudio.Core.Editing.CellRange(1, 2, 0, 0)));
        Assert.True(author.Surface.EditCurrentTable(DitaStudio.Core.Editing.TableOperation.DeleteRow));
        Assert.Equal(4, Rows());
        Assert.Equal("а1", CellAt(document, 1, 0).InnerText);

        // Объединение 2×2 и выравнивание всех выбранных; шапка и тело — по отдельности.
        var d2 = DitaStudio.Core.Model.DitaDocument.Parse(CellTopic);
        var (w2, a2, doc2, undo2) = Show(CellTopic);
        var t2 = doc2.Root.DescendantsAndSelf().First(n => n.Name == "table");
        Assert.True(a2.SelectCellRange(t2, new DitaStudio.Core.Editing.CellRange(1, 2, 1, 2)));
        Assert.True(a2.Surface.MergeCurrentCellRight());
        Dispatcher.UIThread.RunJobs();
        var merged = CellAt(doc2, 1, 1);
        Assert.Equal("c2", merged.GetAttribute("namest"));
        Assert.Equal("c3", merged.GetAttribute("nameend"));
        Assert.Equal("1", merged.GetAttribute("morerows"));
        Assert.Contains("б1", merged.InnerText);
        Assert.Contains("в2", merged.InnerText);
        Assert.Equal(1, undo2.Count(u => u == "Объединение выделенных ячеек"));
        Assert.Single(a2.SelectedCellEntries); // выделена объединённая ячейка

        Assert.True(a2.SelectCellRange(t2, new DitaStudio.Core.Editing.CellRange(3, 3, 0, 2)));
        Assert.True(a2.Surface.SetCurrentBlockFormat(DitaStudio.Core.Publishing.TextFormatting.AlignPrefix, "align-center"));
        Assert.All(new[] { CellAt(doc2, 3, 0), CellAt(doc2, 3, 1), CellAt(doc2, 3, 2) }, c => Assert.Equal("center", c.GetAttribute("align")));
        Assert.True(a2.Surface.ClearSelectedCells());
        Assert.All(new[] { CellAt(doc2, 3, 0), CellAt(doc2, 3, 1), CellAt(doc2, 3, 2) }, c => Assert.Equal(string.Empty, c.InnerText));

        // Строка и столбец целиком.
        a2.FocusNode(CellAt(doc2, 2, 0));
        Assert.True(a2.Surface.SelectCurrentRow());
        Assert.Equal(new DitaStudio.Core.Editing.CellRange(1, 2, 0, 2), a2.SelectedCells!.Value.Range); // объединённая ячейка 2×2 входит целиком
        a2.FocusNode(CellAt(doc2, 3, 0));
        Assert.True(a2.Surface.SelectCurrentRow());
        Assert.Equal(new DitaStudio.Core.Editing.CellRange(3, 3, 0, 2), a2.SelectedCells!.Value.Range);
        a2.FocusNode(CellAt(doc2, 3, 0));
        Assert.True(a2.Surface.SelectCurrentColumn());
        Assert.Equal(0, a2.SelectedCells!.Value.Range.Col0);
        Assert.Equal(0, a2.SelectedCells!.Value.Range.Col1);

        // Меню по правой кнопке: все пункты на месте, «Объединить» доступно только когда можно.
        Assert.True(a2.SelectCellRange(t2, new DitaStudio.Core.Editing.CellRange(0, 0, 0, 1))); // две ячейки шапки
        var menu = a2.BuildCellMenu();
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        foreach (var expected in new[] { "Вставить строку выше", "Вставить строку ниже", "Вставить столбец слева", "Вставить столбец справа", "Удалить строку", "Удалить столбцы: 2",
                     "Объединить ячейки", "Разделить ячейку", "Очистить содержимое", "Выровнять", "Границы", "Выделить строку", "Выделить столбец", "Снять выделение" })
        {
            Assert.Contains(expected, headers);
        }

        Assert.True(menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Объединить ячейки").IsEnabled);
        Assert.False(menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Разделить ячейку").IsEnabled);
        var borders = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Границы").Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(new[] { "Нижняя граница", "Верхняя граница", "Левая граница", "Правая граница", "Нет границы", "Все границы", "Внешние границы", "Внутренние границы",
            "Внутренняя горизонтальная граница", "Внутренняя вертикальная граница" }, borders);
        w2.Close();
        window.Close();
        _ = d2;
        _ = undo;
    }

    /// <summary>Снимки для глаз: выделенные ячейки (светлая и тёмная тема) и таблица с границами по сторонам.</summary>
    [AvaloniaFact]
    public void CellSelection_Screenshots()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            try
            {
                var (window, author, document, _) = Show(CellTopic);
                window.Height = 420;
                var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");
                DragMouse(window, InsideBlock(window, author, CellAt(document, 1, 0)), InsideBlock(window, author, CellAt(document, 1, 1)), InsideBlock(window, author, CellAt(document, 2, 1)));
                window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"author-cells-selected-{theme}.png"));

                // Границы как в Word: внешние линии выделения убраны, внутренние остались; верх таблицы целиком снят.
                Assert.NotNull(author.Surface.SetCellBorders(DitaStudio.Core.Publishing.BorderEdges.Outer)); // левый край таблицы у части строк не снимается — false
                author.SelectCellRange(table, new DitaStudio.Core.Editing.CellRange(0, 0, 0, 2));
                author.Surface.SetCellBorders(DitaStudio.Core.Publishing.BorderEdges.Top);
                author.ClearCellSelection();
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"author-cells-borders-{theme}.png"));
                window.Close();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            }
        }
    }

    /// <summary>Снимки для глаз: выделенные протяжкой блоки, окно «Обернуть в…», результат.</summary>
    [AvaloniaFact]
    public void BlockSelection_Screenshots()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Выделение блоков</title><conbody><p>Первый абзац остаётся как есть.</p>" +
            "<p>Второй абзац попадёт в выделение.</p><ul><li>Пункт списка</li><li>Ещё пункт</li></ul>" +
            "<p>Четвёртый абзац — тоже в выделении.</p><p>Последний абзац снаружи.</p></conbody></concept>");
        var ps = Paragraphs(document);
        DragMouse(window, InsideBlock(window, author, ps[1]), InsideBlock(window, author, ps[1]), InsideBlock(window, author, ps[2]));
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-blocks-selected.png"));

        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        author.Suggestions!.Filter("<note>");
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-blocks-wrap-popup.png"));
        author.Suggestions.Apply();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-blocks-wrapped.png"));
        window.Close();
    }

    /// <summary>Д4, как у пользователя: щелчок мышью по рамке или подписи блока выделяет его целиком (контур), Backspace или Delete
    /// удаляют одним шагом отмены, Esc снимает выделение. Абзац, примечание, рисунок и XML-комментарий.</summary>
    [AvaloniaFact]
    public void ClickOnBlockFrame_ThenBackspace_DeletesBlock_EscCancels()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Первый.</p><!-- служебный комментарий --><note><p>Внутри заметки.</p></note>" +
            "<fig><title>Рис</title><image href=\"none.png\"/></fig><p>Последний.</p></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;

        void Click(Control control, double x, double y)
        {
            var point = control.TranslatePoint(new Point(x, y), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        // Комментарий: щелчок по нему выделяет, Backspace удаляет.
        var comment = body.Children.First(n => n.Kind == NodeKind.Comment);
        var commentView = (Control)author.ViewFor(comment)!;
        Click(commentView, 4, 4);
        Assert.Same(comment, author.SelectedBlock);
        Press(window, Key.Back, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(body.Children, n => n.Kind == NodeKind.Comment);
        Assert.Null(author.SelectedBlock);

        // Примечание: щелчок по рамке (не по тексту внутри) выделяет его целиком; Esc снимает выделение, блок цел.
        var note = body.FirstElement("note")!;
        var noteFrame = (Control)author.ViewFor(note)!;
        Click(noteFrame, 2, 2);
        Assert.Same(note, author.SelectedBlock);
        Press(window, Key.Escape, RawInputModifiers.None);
        Assert.Null(author.SelectedBlock);
        Assert.NotNull(note.Parent);

        // Рисунок: выделить и удалить клавишей Delete.
        var fig = body.FirstElement("fig")!;
        Assert.True(author.SelectBlock(fig));
        Press(window, Key.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(fig.Parent);
        Assert.Contains("Удаление <fig>", undo);

        // Абзац после удалений остался и редактируется как прежде.
        var last = body.ElementChildren().Last(n => n.Name == "p");
        Assert.Equal("Последний.", last.InnerText);
        Assert.NotNull(author.EditorFor(last));
        window.Close();
    }

    /// <summary>Д8: щелчок мышью в видимый текст внутри таблицы не прокручивает полосу «Автора»: курсор ставится куда указали,
    /// полоса остаётся на месте.</summary>
    [AvaloniaFact]
    public void ScrollBar_StaysPut_WhenClickingTextInsideTable()
    {
        var paragraphs = string.Concat(Enumerable.Range(1, 40).Select(i => $"<p>Абзац номер {i}.</p>"));
        var longCell = string.Join(" ", Enumerable.Repeat("текст ячейки таблицы со множеством слов", 12));
        var rows = string.Concat(Enumerable.Range(1, 8).Select(i => $"<row><entry>Строка {i}</entry><entry>{longCell}</entry></row>"));
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody>" + paragraphs +
            "<table><tgroup cols=\"2\"><tbody>" + rows + "</tbody></tgroup></table>" + paragraphs + "</conbody></concept>");
        window.Height = 500;
        Dispatcher.UIThread.RunJobs();
        var scroll = author.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is Panel && v.GetVisualDescendants().OfType<BlockEditor>().Any());
        var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");
        var frame = (Avalonia.Controls.Border)author.ViewFor(table)!;

        // Прокрутили так, что таблица занимает окно; щелчки — по тексту разных ячеек и строк, в том числе у нижнего края.
        scroll.Offset = new Vector(0, frame.TranslatePoint(new Point(0, 0), scroll)!.Value.Y + scroll.Offset.Y - 20);
        Dispatcher.UIThread.RunJobs();
        var before = scroll.Offset.Y;
        Assert.True(before > 0);

        var entries = table.DescendantsAndSelf().Where(n => n.Name == "entry").ToList();
        foreach (var entry in new[] { entries[1], entries[3], entries[5], entries[7] })
        {
            var editor = author.EditorFor(entry)!;
            var visibleHeight = Math.Min(editor.Bounds.Height, scroll.Viewport.Height);
            foreach (var y in new[] { 8d, visibleHeight / 2, visibleHeight - 8 })
            {
                var topLeft = editor.TranslatePoint(new Point(60, y), scroll)!.Value;
                if (topLeft.Y < 0 || topLeft.Y > scroll.Viewport.Height)
                {
                    continue; // вне поля зрения — щелчка по нему не будет
                }

                var point = editor.TranslatePoint(new Point(60, y), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(before, scroll.Offset.Y);
            }
        }

        window.Close();
    }

    /// <summary>Д6: окно подсказки растягивается за любой из четырёх углов; за левый и верхний окно сдвигается так, чтобы
    /// противоположный угол остался на месте; размер ограничен.</summary>
    [AvaloniaFact]
    public void ElementSuggestions_ResizesFromAnyCorner_KeepingOppositeCornerFixed()
    {
        var file = Path.Combine(Path.GetTempPath(), "DitaStudioPopup", Guid.NewGuid().ToString("N"), "suggestions-size.txt");
        var previous = DitaStudio.Presentation.PopupSizeSettings.SettingsPath;
        DitaStudio.Presentation.PopupSizeSettings.SettingsPath = file;
        try
        {
            var (window, author, document, _) = Show();
            var first = Paragraphs(document)[0];
            Focus(window, author, first, InlineContent.FromNode(first).Length);
            Press(window, Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var popup = author.Suggestions!;
            var (w0, h0) = popup.ContentSize;

            // Четыре ручки — по одной на угол.
            var grips = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(popup.Child!).OfType<Avalonia.Controls.Border>().Where(b => b.Tag is ElementSuggestions.PopupCorner).Select(b => (ElementSuggestions.PopupCorner)b.Tag!).ToList();
            Assert.Equal(4, grips.Distinct().Count());

            // Правый нижний: растёт вправо и вниз, окно не двигается.
            var (offX, offY) = (popup.HorizontalOffset, popup.VerticalOffset);
            popup.ResizeFromCorner(ElementSuggestions.PopupCorner.BottomRight, 40, 30);
            Assert.Equal((w0 + 40, h0 + 30), popup.ContentSize);
            Assert.Equal((offX, offY), (popup.HorizontalOffset, popup.VerticalOffset));

            // Левый верхний: тянем угол влево и вверх — окно растёт и едет за углом.
            popup.ResizeFromCorner(ElementSuggestions.PopupCorner.TopLeft, -50, -20);
            Assert.Equal((w0 + 90, h0 + 50), popup.ContentSize);
            Assert.Equal((offX - 50, offY - 20), (popup.HorizontalOffset, popup.VerticalOffset));

            // Правый верхний: вправо растёт, вверх — растёт и едет вверх; левый нижний — наоборот.
            popup.ResizeFromCorner(ElementSuggestions.PopupCorner.TopRight, 10, -10);
            Assert.Equal((w0 + 100, h0 + 60), popup.ContentSize);
            Assert.Equal((offX - 50, offY - 30), (popup.HorizontalOffset, popup.VerticalOffset));
            popup.ResizeFromCorner(ElementSuggestions.PopupCorner.BottomLeft, -15, 5);
            Assert.Equal((w0 + 115, h0 + 65), popup.ContentSize);
            Assert.Equal((offX - 65, offY - 30), (popup.HorizontalOffset, popup.VerticalOffset));

            // Предел размера: сжатие за левый верхний угол до минимума двигает окно только на фактическое изменение.
            var before = (popup.HorizontalOffset, popup.VerticalOffset);
            var applied = popup.ResizeFromCorner(ElementSuggestions.PopupCorner.TopLeft, 5000, 5000);
            Assert.Equal(DitaStudio.Presentation.PopupSizeSettings.Clamp(0, 0), popup.ContentSize);
            Assert.Equal((before.HorizontalOffset - applied.Width, before.VerticalOffset - applied.Height), (popup.HorizontalOffset, popup.VerticalOffset));
            Assert.True(applied.Width < 0 && applied.Height < 0);
            window.Close();
        }
        finally
        {
            DitaStudio.Presentation.PopupSizeSettings.SettingsPath = previous;
        }
    }

    /// <summary>Г2: окно подсказки элементов растягивается, а выбранный размер запоминается на следующий раз.</summary>
    [AvaloniaFact]
    public void ElementSuggestions_CanBeResized_AndRemembersSize()
    {
        var file = Path.Combine(Path.GetTempPath(), "DitaStudioPopup", Guid.NewGuid().ToString("N"), "suggestions-size.txt");
        var previous = DitaStudio.Presentation.PopupSizeSettings.SettingsPath;
        DitaStudio.Presentation.PopupSizeSettings.SettingsPath = file;
        try
        {
            var (window, author, document, _) = Show();
            var first = Paragraphs(document)[0];
            Focus(window, author, first, InlineContent.FromNode(first).Length);
            Press(window, Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var popup = author.Suggestions!;
            Assert.Equal((DitaStudio.Presentation.PopupSizeSettings.DefaultWidth, DitaStudio.Presentation.PopupSizeSettings.DefaultHeight), popup.ContentSize);

            popup.ResizeBy(200, 120);
            Assert.Equal((DitaStudio.Presentation.PopupSizeSettings.DefaultWidth + 200, DitaStudio.Presentation.PopupSizeSettings.DefaultHeight + 120), popup.ContentSize);

            // Границы: не меньше минимума и не больше максимума.
            popup.ResizeBy(-5000, -5000);
            Assert.Equal((DitaStudio.Presentation.PopupSizeSettings.MinWidth, DitaStudio.Presentation.PopupSizeSettings.MinHeight), popup.ContentSize);
            popup.ResizeBy(9000, 9000);
            Assert.Equal((DitaStudio.Presentation.PopupSizeSettings.MaxWidth, DitaStudio.Presentation.PopupSizeSettings.MaxHeight), popup.ContentSize);
            popup.ResizeBy(-(DitaStudio.Presentation.PopupSizeSettings.MaxWidth - 700), -(DitaStudio.Presentation.PopupSizeSettings.MaxHeight - 400));
            DitaStudio.Presentation.PopupSizeSettings.Save(popup.ContentSize.Width, popup.ContentSize.Height); // как по окончании перетаскивания ручки
            popup.Cancel();
            Dispatcher.UIThread.RunJobs();

            // Следующее окно открывается таким же — и по Enter, и по Ctrl+Enter.
            Focus(window, author, first, InlineContent.FromNode(first).Length);
            Press(window, Key.Enter, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((700d, 400d), author.Suggestions!.ContentSize);
            author.Suggestions.Cancel();
            window.Close();

            Assert.Equal("700 400", File.ReadAllText(file));
        }
        finally
        {
            DitaStudio.Presentation.PopupSizeSettings.SettingsPath = previous;
        }
    }

    /// <summary>Г4: подпись рисунка/таблицы в «Авторе» — «Рисунок N» при пустом названии; команда добавляет пустой title или убирает его.</summary>
    [AvaloniaFact]
    public void Caption_EmptyTitleShowsNumberOnly_AndToggleAddsOrRemovesTitle()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>Текст.</p>" +
            "<fig><title>С названием</title><image href=\"a.png\"/></fig>" +
            "<fig><image href=\"b.png\"/></fig>" +
            "<table><title/><tgroup cols=\"1\"><tbody><row><entry>Ячейка</entry></row></tbody></tgroup></table></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var figures = body.ElementChildren().Where(n => n.Name == "fig").ToList();
        var table = body.FirstElement("table")!;
        List<string?> Badges() => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Where(t => t is not null && (t.StartsWith("Рисунок ") || t.StartsWith("Таблица "))).ToList();

        Assert.Equal(new List<string?> { "Рисунок 1.", "Таблица 1" }, Badges()); // у второго рисунка title нет — подписи нет

        // Курсор в рисунке без title: «добавить подпись» — пустой title, подпись «Рисунок 2» (без точки).
        author.CurrentNode = figures[1];
        Assert.True(author.Surface.ToggleCaption());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("title", figures[1].ElementChildren().First().Name);
        Assert.Contains("Подпись: добавить", undo);
        Assert.Equal(new List<string?> { "Рисунок 1.", "Рисунок 2", "Таблица 1" }, Badges());

        // Убрать: title исчезает, нумерация пересчитана.
        Assert.False(author.Surface.ToggleCaption());
        Dispatcher.UIThread.RunJobs();
        Assert.Null(figures[1].FirstElement("title"));
        Assert.Equal(new List<string?> { "Рисунок 1.", "Таблица 1" }, Badges());

        // В таблице (курсор в ячейке) — то же.
        author.CurrentNode = table.DescendantsAndSelf().First(n => n.Name == "entry");
        Assert.False(author.Surface.ToggleCaption());
        Assert.Null(table.FirstElement("title"));
        Assert.True(author.Surface.ToggleCaption());
        Assert.Equal("title", table.ElementChildren().First().Name);

        // Не рисунок и не таблица — команда ничего не делает.
        author.CurrentNode = body.ElementChildren().First();
        Assert.Null(author.Surface.ToggleCaption());
        window.Close();
    }

    /// <summary>Г16: Enter в ячейке таблицы работает как вне таблицы — новый абзац внутри ячейки, ячейка не делится; меню без соседних ячеек.</summary>
    [AvaloniaFact]
    public void Enter_InTableCell_SplitsIntoParagraphsInsideCell_NotIntoCells()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><table><tgroup cols=\"2\"><tbody>" +
            "<row><entry>Первая ячейка</entry><entry>Вторая</entry></row></tbody></tgroup></table></conbody></concept>");
        var row = document.Root.DescendantsAndSelf().First(n => n.Name == "row");
        var cell = row.ElementChildren().First();

        // Enter в середине текста: ячейка остаётся одной, текст — в двух абзацах внутри неё.
        var editor = Focus(window, author, cell, 5);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, row.ElementChildren().Count());
        Assert.Equal(new[] { "p", "p" }, cell.ElementChildren().Select(n => n.Name).ToArray());
        Assert.Equal("Перва", cell.ElementChildren().First().InnerText);
        Assert.Equal("я ячейка", cell.ElementChildren().Last().InnerText);
        Assert.Empty(new DitaStudio.Core.Validation.DitaValidator { CheckStyleRules = false }.Validate(document)
            .Where(i => i.Severity == DitaStudio.Core.Validation.IssueSeverity.Error));

        // Enter в конце последнего абзаца ячейки: меню как вне таблицы — «абзац как обычно», элементы после абзаца, но не «entry» и не «row».
        var last = cell.ElementChildren().Last();
        Focus(window, author, last, InlineContent.FromNode(last).Length);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var titles = author.Suggestions!.Visible.Select(i => i.Title).ToList();
        Assert.Contains(titles, t => t.Contains("<p>"));
        Assert.DoesNotContain(titles, t => t.Contains("<entry>") || t.Contains("<row>"));
        author.Suggestions.Apply(); // первая строка — «как обычно»: новый абзац
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, cell.ElementChildren().Count());
        Assert.Equal(2, row.ElementChildren().Count());

        // Ячейка с текстом: Enter в конце — меню предлагает «Новый абзац в ячейке» и блоки внутрь ячейки.
        var second = row.ElementChildren().Last();
        Focus(window, author, second, second.InnerText.Length);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var cellMenu = author.Suggestions!.Visible.Select(i => i.Title).ToList();
        Assert.Contains("Новый абзац в ячейке — как обычно", cellMenu);
        Assert.Contains(cellMenu, t => t.Contains("<ul>") && t.Contains("в ячейке"));
        Assert.DoesNotContain(cellMenu, t => t.Contains("<entry>"));
        author.Suggestions.Filter("<ul>");
        author.Suggestions.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(second.ElementChildren(), c => c.Name == "ul");
        Assert.Equal(2, row.ElementChildren().Count());
        window.Close();
    }

    /// <summary>Г17: свой размер шрифта — класс size-13_5, в «Авторе» виден сразу; команда принимает число с запятой.</summary>
    [AvaloniaFact]
    public void CustomFontSize_SetsFractionalClass_AndShowsInAuthor()
    {
        var (window, author, document, _) = Show();
        var second = Paragraphs(document)[1];
        var editor = Focus(window, author, second, 0);
        editor.Select(0, editor.Document.TextLength);
        Assert.True(author.Surface.ApplyTextFormat(Core.Publishing.TextFormatting.SizePrefix, Core.Publishing.TextFormatting.SizeToken(13.5)));
        author.FlushPendingEdits();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("size-13_5", second.GetAttribute("outputclass"));
        Assert.Equal(13.5 * 4 / 3, author.EditorFor(second)!.FontSize, 3);
        Assert.Equal(13.5, Core.Publishing.TextFormatting.SizeOf(second));

        // Размер за пределами — ограничивается границей.
        Assert.Equal("size-200", Core.Publishing.TextFormatting.SizeToken(999));
        window.Close();
    }

    /// <summary>Г18: таблица выделяется целиком (щелчок по её рамке), удаляется клавишей Delete одним шагом отмены;
    /// название таблицы добавляется и убирается в любой момент.</summary>
    [AvaloniaFact]
    public void Table_SelectWhole_DeleteWithKey_AndToggleTitleAnytime()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>До.</p>" +
            "<table><tgroup cols=\"1\"><tbody><row><entry>Ячейка</entry></row></tbody></tgroup></table>" +
            "<p>После.</p></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var table = body.FirstElement("table")!;

        var frame = (Avalonia.Controls.Border)author.ViewFor(table)!;
        var point = frame.TranslatePoint(new Point(30, 2), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(table, author.SelectedTable);
        Assert.Same(table, author.CurrentNode);
        Assert.Equal(2, frame.BorderThickness.Left); // контур выделения

        // Название можно добавить и убрать, пока таблица выделена.
        Assert.True(author.Surface.ToggleCaption());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("title", table.ElementChildren().First().Name);
        Assert.False(author.Surface.ToggleCaption());
        Assert.Null(table.FirstElement("title"));

        Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs();
        // Щелчок в тексте снимает выделение таблицы.
        var cell = table.DescendantsAndSelf().First(n => n.Name == "entry");
        Focus(window, author, cell, 0);
        Assert.Null(author.SelectedTable);

        // Выбрали заново и нажали Delete: таблицы нет, абзацы на месте, отмена возвращает.
        frame = (Avalonia.Controls.Border)author.ViewFor(table)!;
        point = frame.TranslatePoint(new Point(30, 2), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(table, author.SelectedTable);
        Press(window, Key.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "p", "p" }, body.ElementChildren().Select(n => n.Name).ToArray());
        Assert.Contains(undo, d => d.Contains("Удаление <table>"));
        Assert.Null(author.SelectedTable);
        window.Close();
    }

    /// <summary>Г18: команды «Выделить таблицу» и «Удалить таблицу» работают с курсором в любой ячейке; вне таблицы — отказ.</summary>
    [AvaloniaFact]
    public void TableCommands_SelectAndDelete_FromCellCursor()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><p>До.</p>" +
            "<table><tgroup cols=\"2\"><tbody><row><entry>А</entry><entry>Б</entry></row></tbody></tgroup></table></conbody></concept>");
        var body = document.Root.FirstElement("conbody")!;
        var cell = body.DescendantsAndSelf().Last(n => n.Name == "entry");

        author.CurrentNode = body.ElementChildren().First();
        Assert.False(author.Surface.SelectCurrentTable());
        Assert.False(author.Surface.DeleteCurrentTable());

        author.CurrentNode = cell;
        Assert.True(author.Surface.SelectCurrentTable());
        Assert.Same(body.FirstElement("table"), author.SelectedTable);
        author.Deselect();
        Assert.Null(author.SelectedTable);

        author.CurrentNode = cell; // Deselect сбросил текущий блок
        Assert.True(author.Surface.DeleteCurrentTable());
        Dispatcher.UIThread.RunJobs();
        Assert.Null(body.FirstElement("table"));
        Assert.Contains(undo, d => d.Contains("Удаление <table>"));
        window.Close();
    }

    /// <summary>Снимки для глаз: таблицы в «Авторе» — с названием, без границ, с убранной линией, выделенная целиком (Г4, Г12, Г16, Г18).</summary>
    [AvaloniaFact]
    public void TableVisuals_Screenshots()
    {
        static string Table(string title, string attrs = "") =>
            "<table" + attrs + ">" + title + "<tgroup cols=\"3\"><thead><row><entry>Параметр</entry><entry>Тип</entry><entry>Описание</entry></row></thead><tbody>" +
            "<row><entry>width</entry><entry>число</entry><entry>Ширина</entry></row><row><entry>height</entry><entry>число</entry><entry>Высота</entry></row></tbody></tgroup></table>";
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Таблицы</title><conbody><p>Все границы, с названием:</p>" + Table("<title>Параметры изображения</title>") +
            "<p>Без границ, название пустое:</p>" + Table("<title/>", " frame=\"none\" rowsep=\"0\" colsep=\"0\"") +
            "<p>Только горизонтальные линии, без названия:</p>" + Table("", " frame=\"topbot\" colsep=\"0\"") +
            "<p>Выделена целиком:</p>" + Table("<title>Выделенная таблица</title>") + "</conbody></concept>");
        window.Width = 900;
        window.Height = 1000;
        Dispatcher.UIThread.RunJobs();
        var tables = document.Root.DescendantsAndSelf().Where(n => n.Name == "table").ToList();
        author.CurrentNode = tables[3].DescendantsAndSelf().First(n => n.Name == "entry");
        Assert.True(author.Surface.SelectCurrentTable());
        Dispatcher.UIThread.RunJobs();
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-tables-light.png"));
        window.Close();
    }

    /// <summary>Снимки для глаз (Г16): Enter в ячейке — абзацы внутри ячейки; меню Enter в ячейке; список внутри ячейки.</summary>
    [AvaloniaFact]
    public void TableCellEnter_Screenshots()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Ячейки</title><conbody><table><title>Таблица с ячейками</title><tgroup cols=\"2\"><tbody>" +
            "<row><entry>Первая ячейка</entry><entry>Вторая</entry></row><row><entry>Третья</entry><entry>Четвёртая</entry></row></tbody></tgroup></table></conbody></concept>");
        window.Width = 900;
        window.Height = 600;
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var row = document.Root.DescendantsAndSelf().First(n => n.Name == "row");
        var cell = row.ElementChildren().First();
        Focus(window, author, cell, 5);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-cell-enter-1-split.png"));

        var second = row.ElementChildren().Last();
        Focus(window, author, second, second.InnerText.Length);
        Press(window, Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-cell-enter-2-menu.png"));

        author.Suggestions!.Filter("<ul>");
        author.Suggestions.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("#text,ul", string.Join(",", second.Children.Select(n => n.Kind == NodeKind.Element ? n.Name : "#text")));
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "author-cell-enter-3-list.png"));
        window.Close();
    }

    /// <summary>Г12: границы таблицы — всей, строки и столбца: атрибуты frame/rowsep/colsep, отмена, вид ячеек в «Авторе».</summary>
    [AvaloniaFact]
    public void TableBorders_Commands_SetAttributes_AndAuthorShowsLines()
    {
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Т</title><conbody><table><tgroup cols=\"2\"><tbody>" +
            "<row><entry>А</entry><entry>Б</entry></row><row><entry>В</entry><entry>Г</entry></row></tbody></tgroup></table></conbody></concept>");
        var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");
        var entries = table.DescendantsAndSelf().Where(n => n.Name == "entry").ToList();
        author.CurrentNode = entries[0];

        Thickness LinesOf(DitaNode entry) => author.EditorFor(entry)!.FindAncestorOfType<Avalonia.Controls.Border>()!.BorderThickness;
        Assert.Equal(new Thickness(1, 1, 1, 1), LinesOf(entries[0]));

        // «Без границ»: рамка и линии убраны, в «Авторе» у ячеек нет границ.
        Assert.True(author.Surface.SetTableBorders(Core.Publishing.TableBorderMode.None));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("none", table.GetAttribute("frame"));
        Assert.Contains("Границы таблицы", undo);
        Assert.Equal(new Thickness(0, 0, 0, 0), LinesOf(author.EditorFor(entries[0])!.Node));
        Assert.Equal(new Thickness(0, 0, 0, 0), LinesOf(entries[3]));

        // «Все границы» возвращает.
        author.CurrentNode = entries[0];
        Assert.True(author.Surface.SetTableBorders(Core.Publishing.TableBorderMode.All));
        Dispatcher.UIThread.RunJobs();
        Assert.False(table.HasAttribute("frame"));
        Assert.Equal(new Thickness(1, 1, 1, 1), LinesOf(entries[0]));

        // Линия под строкой и справа от столбца — у той строки и того столбца, где курсор.
        author.CurrentNode = entries[0];
        Assert.True(author.Surface.SetRowBorder(false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0", entries[0].Parent!.GetAttribute("rowsep"));
        Assert.Equal(0, LinesOf(entries[0]).Bottom);
        Assert.Equal(1, LinesOf(entries[2]).Bottom); // нижняя рамка таблицы осталась

        author.CurrentNode = entries[0];
        Assert.True(author.Surface.SetColumnBorder(false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0", entries[0].GetAttribute("colsep"));
        Assert.Equal("0", entries[2].GetAttribute("colsep"));
        Assert.Null(entries[1].GetAttribute("colsep"));
        Assert.Equal(0, LinesOf(entries[0]).Right);

        // Вне таблицы — отказ.
        author.CurrentNode = document.Root.FirstElement("title");
        Assert.False(author.Surface.SetTableBorders(Core.Publishing.TableBorderMode.None));
        Assert.False(author.Surface.SetRowBorder(true));
        Assert.False(author.Surface.SetColumnBorder(true));
        window.Close();
    }

    /// <summary>Г11: у блока с product / audience / outputclass в «Авторе» — серая пометка; оформление текста в неё не попадает; выключается.</summary>
    [AvaloniaFact]
    public void AttributeNotes_ShowProductAndClass_InGray_AndCanBeSwitchedOff()
    {
        var (window, author, document, _) = Show(
            "<concept id=\"c\"><title>Т</title><conbody>" +
            "<p product=\"Альфа Бета\">Для продуктов.</p>" +
            "<note outputclass=\"warning-box size-18 align-center\" audience=\"admin\"><p>Внутри заметки.</p></note>" +
            "<p outputclass=\"size-14\">Без пометки.</p><p>Обычный.</p></conbody></concept>");
        List<string?> Notes() => window.GetVisualDescendants().OfType<TextBlock>().Where(t => Equals(t.Tag, "attribute-note")).Select(t => t.Text).ToList();

        Assert.Equal(new List<string?> { "product: Альфа, Бета", "audience: admin · class: warning-box" }, Notes());

        // Пометка серая и вмещает всё нужное; сами блоки остаются редактируемыми.
        var first = Paragraphs(document)[0];
        Assert.NotNull(author.EditorFor(first));

        var previous = AuthorView.AttributeNotesEnabled;
        try
        {
            AuthorView.AttributeNotesEnabled = false;
            author.Rebuild();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Notes());
        }
        finally
        {
            AuthorView.AttributeNotesEnabled = previous;
        }

        Assert.Equal("product: Альфа, Бета", AuthorView.AttributeNote(first));
        Assert.Null(AuthorView.AttributeNote(Paragraphs(document)[^1]));
        window.Close();
    }

    /// <summary>Д12: серая пометка — у блока с любыми атрибутами (rev, status, conref…), кроме служебных, раскладки таблицы и классов оформления;
    /// условные атрибуты — первыми, остальные по алфавиту, длинное значение обрезается.</summary>
    [AvaloniaFact]
    public void AttributeNotes_ShowAnyAttribute_ExceptServiceAndTableLayout()
    {
        var longValue = new string('в', 80);
        var (window, author, document, _) = Show(
            "<concept id=\"c\" xml:lang=\"ru\"><title>Т</title><conbody>" +
            "<p id=\"x1\" xml:lang=\"en\" status=\"new\" rev=\"2\" importance=\"high\" product=\"A\" conref=\"other.dita#o/x\" outputclass=\"size-14 fancy\">Много атрибутов.</p>" +
            "<p id=\"x2\">Только id.</p>" +
            "<p translate=\"" + longValue + "\">Длинное значение.</p>" +
            "<table frame=\"all\" colsep=\"1\"><tgroup cols=\"1\" align=\"left\"><tbody><row rowsep=\"0\"><entry colsep=\"0\" valign=\"top\">Ячейка</entry></row></tbody></tgroup></table>" +
            "</conbody></concept>");
        var ps = Paragraphs(document);
        Assert.Equal("product: A · conref: other.dita#o/x · importance: high · rev: 2 · status: new · class: fancy", AuthorView.AttributeNote(ps[0]));
        Assert.Null(AuthorView.AttributeNote(ps[1])); // id — служебный
        var truncated = AuthorView.AttributeNote(ps[2])!;
        Assert.Equal("translate: " + new string('в', 59) + "…", truncated);

        // Раскладка таблицы (frame, colsep, rowsep, valign, align) пометок не даёт — ею управляют рамка и команды таблицы.
        var table = document.Root.DescendantsAndSelf().First(n => n.Name == "table");
        foreach (var node in table.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
        {
            Assert.Null(AuthorView.AttributeNote(node));
        }

        var notes = window.GetVisualDescendants().OfType<TextBlock>().Where(t => Equals(t.Tag, "attribute-note")).Select(t => t.Text).ToList();
        Assert.Equal(2, notes.Count);
        Assert.StartsWith("product: A", notes[0]);
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

    [AvaloniaFact]
    public void TableBorders_DragResizesColumnsAndRows()
    {
        // Замечание: растягивать столбцы и строки мышью, без правки XML.
        var (window, author, document, undo) = Show(
            "<concept id=\"c\"><title>Таблица</title><conbody><table><tgroup cols=\"2\">" +
            "<colspec colname=\"c1\"/><colspec colname=\"c2\"/><tbody>" +
            "<row><entry>Параметр</entry><entry>Значение</entry></row>" +
            "<row><entry>a</entry><entry>b</entry></row></tbody></tgroup></table></conbody></concept>");
        var handles = author.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "column-handle" or "row-handle").ToList();
        var column = handles.Single(h => h.Tag is "column-handle");
        var grid = (Grid)column.Parent!;
        var before = grid.ColumnDefinitions.Select(d => d.ActualWidth).ToArray();
        Assert.Equal(before[0], before[1], 1);

        window.CaptureRenderedFrame(); // проверка попадания идёт по отрисованному кадру
        var at = column.TranslatePoint(new Point(3, 10), window)!.Value;
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(at + new Vector(100, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(at + new Vector(100, 0), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var colspecs = document.Root.Descendants().Where(n => n.Name == "colspec").ToList();
        var widths = colspecs.Select(c => double.Parse(c.GetAttribute("colwidth")!.TrimEnd('*'), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(widths[0] > widths[1], $"первый столбец шире: {string.Join(", ", widths)}");
        Assert.Equal(100, widths.Sum(), 1);
        Assert.Contains("Ширина столбцов", undo);
        Assert.True(document.IsDirty);

        // Нижняя граница первой строки — ниже; высота пишется классом.
        window.CaptureRenderedFrame();
        var rowHandle = author.GetVisualDescendants().OfType<Border>().First(b => b.Tag is "row-handle");
        var rowAt = rowHandle.TranslatePoint(new Point(40, 3), window)!.Value;
        window.MouseDown(rowAt, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(rowAt + new Vector(0, 40), RawInputModifiers.LeftMouseButton);
        window.MouseUp(rowAt + new Vector(0, 40), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var firstRow = document.Root.Descendants().First(n => n.Name == "row");
        Assert.Matches(@"^row-height-\d+mm$", firstRow.GetAttribute("outputclass"));
        Assert.Contains("Высота строки", undo);

        // Двойной щелчок по нижней границе — высота снова по содержимому.
        rowHandle.RaiseEvent(new Avalonia.Input.TappedEventArgs(InputElement.DoubleTappedEvent, null!));
        Assert.Null(firstRow.GetAttribute("outputclass"));
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
