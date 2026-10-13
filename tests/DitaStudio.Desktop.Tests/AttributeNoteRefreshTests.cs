using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Серая пометка атрибутов у блока следует за атрибутами: убрали продукт или класс — пометка исчезает, добавили — появляется.</summary>
public sealed class AttributeNoteRefreshTests
{
    private static string[] Notes(AuthorView author) =>
        author.GetVisualDescendants().OfType<TextBlock>().Where(t => Equals(t.Tag, "attribute-note")).Select(t => t.Text ?? string.Empty).ToArray();

    [AvaloniaFact]
    public void NoteFollowsAttributes_WithoutRebuildingTheBlock()
    {
        var document = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody>" +
            "<p product=\"alpha\" outputclass=\"warning-box\">Первый</p><p>Второй</p></conbody></concept>");
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = 800, Height = 500, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var first = document.Root.Descendants().First(n => n.Name == "p");
        var editorBefore = author.EditorFor(first);
        Assert.Equal(new[] { "product: alpha · class: warning-box" }, Notes(author));

        first.RemoveAttribute("product");
        author.Surface.RefreshAttributeNotes(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "class: warning-box" }, Notes(author));

        first.RemoveAttribute("outputclass");
        author.Surface.RefreshAttributeNotes(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Notes(author));
        Assert.Same(editorBefore, author.EditorFor(first)); // блок не перестраивался

        first.SetAttribute("audience", "admin");
        author.Surface.RefreshAttributeNotes(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "audience: admin" }, Notes(author));
        Assert.NotNull(author.ViewFor(first));
        Assert.Equal(2, author.GetVisualDescendants().OfType<BlockEditor>().Count(e => e.Node.Name == "p"));

        window.Close();
    }
}
