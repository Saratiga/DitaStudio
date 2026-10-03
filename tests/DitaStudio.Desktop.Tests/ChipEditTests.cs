using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Presentation.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Правка атрибутов плашек «Автора» (изображение, пустая ссылка) во всплывающем окне.</summary>
public sealed class ChipEditTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAJ0lEQVR4nGM8oaHBMBCAaUBsHbV41OJRi0ctHrV41OJRi0ctHhAAABx5AUDfR9jWAAAAAElFTkSuQmCC";

    private static Dictionary<string, string> Values(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    // ---------------------------------------------------------------- чистая логика

    [Fact]
    public void Image_AltWidthHref_WrittenToModel()
    {
        var image = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"a.png\" height=\"10px\"><alt>Старая</alt></image></p></conbody></concept>")
            .Root.Descendants().First(n => n.Name == "image");

        var fields = ChipEdit.Fields(image);
        Assert.Equal(new[] { "alt", "width", "href" }, fields.Select(f => f.Key).ToArray());
        Assert.Equal("Старая", fields[0].Value);

        var result = ChipEdit.Apply(image, Values(("alt", "Схема"), ("width", " 320,5 "), ("href", "img/b.png")));
        Assert.True(result.Changed);
        Assert.False(result.ChipDissolved);
        Assert.Equal("Схема", image.FirstElement("alt")!.InnerText);
        Assert.Equal("320.5px", image.GetAttribute("width"));
        Assert.Null(image.GetAttribute("height")); // высота подбирается по пропорциям
        Assert.Equal("img/b.png", image.GetAttribute("href"));
        Assert.Single(image.ElementChildren());
    }

    [Fact]
    public void Image_EmptyAltRemovesElement_EmptyWidthRemovesAttribute_NoChangeIsReported()
    {
        var image = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"a.png\" width=\"50%\"><alt>Текст</alt></image></p></conbody></concept>")
            .Root.Descendants().First(n => n.Name == "image");

        Assert.False(ChipEdit.Apply(image, Values(("alt", "Текст"), ("width", "50%"), ("href", "a.png"))).Changed);

        Assert.True(ChipEdit.Apply(image, Values(("alt", ""), ("width", ""))).Changed);
        Assert.Empty(image.ElementChildren());
        Assert.Null(image.GetAttribute("width"));
        Assert.Equal("a.png", image.GetAttribute("href"));
    }

    [Fact]
    public void Image_InvalidInput_LeavesFieldAsIs()
    {
        var image = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"a.png\" width=\"40px\"/></p></conbody></concept>")
            .Root.Descendants().First(n => n.Name == "image");

        Assert.False(ChipEdit.Apply(image, Values(("width", "широкая"), ("href", ""))).Changed);
        Assert.Equal("40px", image.GetAttribute("width"));
        Assert.Equal("a.png", image.GetAttribute("href")); // изображение без другой цели без адреса не остаётся

        Assert.True(ChipEdit.Apply(image, Values(("alt", "Новая"))).Changed); // alt создаётся, когда элемента не было
        Assert.Equal("alt", image.ElementChildren().Single().Name);
    }

    [Fact]
    public void Link_ScopeAndText_TextDissolvesTheChip()
    {
        var xref = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><xref href=\"other.dita\"/></p></conbody></concept>")
            .Root.Descendants().First(n => n.Name == "xref");

        Assert.Equal(new[] { "text", "href", "scope" }, ChipEdit.Fields(xref).Select(f => f.Key).ToArray());
        Assert.False(ChipEdit.Apply(xref, Values(("scope", "bogus"))).Changed);

        var scope = ChipEdit.Apply(xref, Values(("scope", "peer"), ("href", "https://example.com")));
        Assert.True(scope.Changed);
        Assert.False(scope.ChipDissolved);
        Assert.Equal("peer", xref.GetAttribute("scope"));

        var text = ChipEdit.Apply(xref, Values(("text", "Сайт")));
        Assert.True(text.ChipDissolved);
        Assert.Equal("Сайт", xref.InnerText);

        Assert.True(ChipEdit.Apply(xref, Values(("scope", ""))).Changed);
        Assert.Null(xref.GetAttribute("scope"));
    }

    [Fact]
    public void OtherChips_HaveNothingToEdit()
    {
        Assert.False(ChipEdit.CanEdit(DitaNode.Element("fn")));
        Assert.Empty(ChipEdit.Fields(DitaNode.Element("data")));
    }

    // ---------------------------------------------------------------- окно в «Авторе»

    private static (Window Window, AuthorView Author, DitaDocument Document, List<string> Undo) Show(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioImageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(Png));
        var path = Path.Combine(dir, "topic.dita");
        File.WriteAllText(path, "<concept id=\"c\"><title>Плашки</title><conbody>" + body + "</conbody></concept>");
        var document = DitaDocument.Load(path);
        var author = new AuthorView();
        var undo = new List<string>();
        author.BeforeStructuralEdit += (_, description) => undo.Add(description);
        author.Load(document);
        var window = new Window { Width = 900, Height = 500, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, author, document, undo);
    }

    private static void Click(Window window, Visual target)
    {
        window.CaptureRenderedFrame(); // проверка попадания идёт по отрисованному кадру
        var point = target.TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ClickOnInlineImage_OpensPopup_CommitIsOneUndoStep()
    {
        var (window, author, document, undo) = Show("<p>Значок <image href=\"pic.png\"><alt>Был</alt></image> в строке.</p>");
        var image = document.Root.Descendants().First(n => n.Name == "image");
        var picture = author.GetVisualDescendants().OfType<ResizableImage>().Single();

        Click(window, picture);
        Assert.NotNull(author.ChipEditor);
        Assert.Equal(new[] { "alt", "width", "href" }, author.ChipEditor!.Editors.Keys.ToArray());
        Assert.Equal("Был", ((TextBox)author.ChipEditor.Editors["alt"]).Text);

        author.ChipEditor.SetValue("alt", "Новая подпись");
        author.ChipEditor.SetValue("width", "64");
        author.ChipEditor.Commit();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(author.ChipEditor);
        Assert.Equal("Новая подпись", image.FirstElement("alt")!.InnerText);
        Assert.Equal("64px", image.GetAttribute("width"));
        Assert.Equal(new[] { ChipEdit.Title(image) }, undo);
        Assert.True(document.IsDirty);

        // Плашка осталась на месте и показывает новую ширину; текст вокруг цел.
        var shown = author.GetVisualDescendants().OfType<ResizableImage>().Single();
        Assert.Equal(64, shown.ShownWidth, 1);
        author.FlushPendingEdits();
        Assert.Equal("Новая подпись", image.FirstElement("alt")!.InnerText);
        Assert.Contains("Значок ", XmlSerializer.ToXml(document.Root));

        window.Close();
    }

    [AvaloniaFact]
    public void EscCancels_NoChangeCommitWithoutChangesWritesNothing()
    {
        var (window, author, document, undo) = Show("<p>Текст <image href=\"pic.png\"><alt>Был</alt></image>.</p>");
        var image = document.Root.Descendants().First(n => n.Name == "image");
        var picture = author.GetVisualDescendants().OfType<ResizableImage>().Single();

        Click(window, picture);
        author.ChipEditor!.SetValue("alt", "Не запишется");
        var box = (TextBox)author.ChipEditor.Editors["alt"];
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.Null(author.ChipEditor);
        Assert.Equal("Был", image.FirstElement("alt")!.InnerText);
        Assert.Empty(undo);
        Assert.False(document.IsDirty);

        // Открыли и закрыли без правок — отмены и пометки «изменён» нет.
        Click(window, author.GetVisualDescendants().OfType<ResizableImage>().Single());
        Assert.NotNull(author.ChipEditor);
        author.ChipEditor!.Commit();
        Assert.Empty(undo);
        Assert.False(document.IsDirty);

        window.Close();
    }

    [AvaloniaFact]
    public void BlockImage_Click_EditsHrefAndRebuilds()
    {
        var (window, author, document, undo) = Show("<fig><title>Рис</title><image href=\"pic.png\"/></fig>");
        var image = document.Root.Descendants().First(n => n.Name == "image");
        var picture = author.GetVisualDescendants().OfType<ResizableImage>().Single();

        Click(window, picture);
        Assert.NotNull(author.ChipEditor);
        author.ChipEditor!.SetValue("alt", "Рисунок");
        author.ChipEditor.Commit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Рисунок", image.FirstElement("alt")!.InnerText);
        Assert.Single(undo);
        Assert.Single(author.GetVisualDescendants().OfType<ResizableImage>()); // блок перестроен, картинка одна

        window.Close();
    }

    [AvaloniaFact]
    public void EmptyLinkChip_ScopeAndTextEditing_Screenshot()
    {
        var (window, author, document, undo) = Show("<p>Смотрите <xref href=\"other.dita\"/> и <image href=\"pic.png\"/>.</p>");
        var xref = document.Root.Descendants().First(n => n.Name == "xref");
        var editor = author.EditorFor(document.Root.Descendants().First(n => n.Name == "p"))!;
        var chip = author.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("🔗") == true);

        Click(window, chip);
        Assert.NotNull(author.ChipEditor);
        Assert.Equal(new[] { "text", "href", "scope" }, author.ChipEditor!.Editors.Keys.ToArray());

        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, "author-chip-edit.png"));

        author.ChipEditor.SetValue("scope", "peer");
        author.ChipEditor.SetValue("text", "другой топик");
        author.ChipEditor.Commit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("peer", xref.GetAttribute("scope"));
        Assert.Equal("другой топик", xref.InnerText);
        Assert.Single(undo);
        // Со своим текстом ссылка перестаёт быть плашкой: текст виден в блоке, на месте остаётся только картинка.
        var rebuilt = author.EditorFor(document.Root.Descendants().First(n => n.Name == "p"))!;
        Assert.Contains("другой топик", rebuilt.Text);
        Assert.NotNull(editor);

        window.Close();
    }
}
