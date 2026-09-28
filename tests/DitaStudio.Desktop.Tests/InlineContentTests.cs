using DitaStudio.Core.Model;
using DitaStudio.Presentation.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Плоская модель блока режима «Автор» и структурные правки по клавишам — без UI.</summary>
public sealed class InlineContentTests
{
    private const char Chip = InlineContent.ChipChar;

    private static DitaNode Block(string xml) =>
        DitaDocument.Parse($"<concept id=\"c\"><title>t</title><conbody>{xml}</conbody></concept>")
            .Root.FirstElement("conbody")!.ElementChildren().First();

    private static string Xml(DitaNode node) => XmlSerializer.ToXml(node);

    [Fact]
    public void RoundTrip_KeepsNestedInlineElementsAndChips()
    {
        var p = Block("<p>Нажмите <uicontrol>OK</uicontrol> и <b>см. <i>тут</i></b><xref href=\"a.dita\"/>.</p>");
        var before = Xml(p);

        var content = InlineContent.FromNode(p);
        Assert.Equal($"Нажмите OK и см. тут{Chip}.", content.Text);
        Assert.Equal(new[] { "b", "i" }, content.ChainAt(content.Text.IndexOf("тут", StringComparison.Ordinal)).Nodes.Select(n => n.Name));
        Assert.Equal("xref", content.ChipAt(content.Text.IndexOf(Chip))!.Name);

        content.WriteBack();
        Assert.Equal(before, Xml(p));
    }

    [Fact]
    public void Typing_InheritsFormattingOnTheLeft_AndNotTheChip()
    {
        var p = Block("<p>a <b>bold</b><xref href=\"x.dita\"/>z</p>");
        var content = InlineContent.FromNode(p);

        content.Replace("a bold".Length, 0, "er");
        content.Replace(content.Text.IndexOf(Chip) + 1, 0, "!");
        content.Replace(0, 0, "Q");
        content.WriteBack();

        Assert.Equal("<p>Qa <b>bolder</b><xref href=\"x.dita\"/>!z</p>", Xml(p));
    }

    [Fact]
    public void Wrap_PutsNewElementUnderCommonAncestors()
    {
        var p = Block("<p>xx <b>yy</b> zz</p>");
        var content = InlineContent.FromNode(p);

        Assert.True(content.Wrap(1, 4, "codeph"));
        content.WriteBack();
        Assert.Equal("<p>x<codeph>x <b>yy</b></codeph> zz</p>", Xml(p));

        var inner = InlineContent.FromNode(p);
        Assert.True(inner.Wrap(inner.Text.IndexOf("yy", StringComparison.Ordinal), 1, "i"));
        inner.WriteBack();
        Assert.Equal("<p>x<codeph>x <b><i>y</i>y</b></codeph> zz</p>", Xml(p));
    }

    [Fact]
    public void ClearFormatting_KeepsChips()
    {
        var p = Block("<p><b>a<image href=\"i.png\"/>b</b>c</p>");
        var content = InlineContent.FromNode(p);

        content.ClearFormatting(0, content.Length);
        content.WriteBack();
        Assert.Equal("<p>a<image href=\"i.png\"/>bc</p>", Xml(p));
    }

    [Fact]
    public void ReinsertingRemovedText_RestoresFormattingAndChips()
    {
        // Отмена в редакторе блока или «вырезать — вставить» возвращает кусок с разметкой.
        var p = Block("<p>x <b>bold<fn>note</fn></b> y</p>");
        var content = InlineContent.FromNode(p);
        var start = content.Text.IndexOf('b');
        var removed = content.Text.Substring(start, 5);

        content.Replace(start, 5, string.Empty);
        content.Replace(start, 0, removed);
        content.WriteBack();

        Assert.Equal("<p>x <b>bold<fn>note</fn></b> y</p>", Xml(p));
    }

    [Fact]
    public void LayoutWhitespace_IsCollapsed_TypedSpacesAreKept()
    {
        var p = Block("<p>\n    Первая строка\n    вторая  строка\n  </p>");
        Assert.Equal("Первая строка вторая  строка", InlineContent.FromNode(p).Text);
    }

    [Fact]
    public void SplitBlock_KeepsInlineElementsOnBothSides()
    {
        var p = Block("<p id=\"p1\" audience=\"admin\">one <b>two three</b> four</p>");
        var created = BlockOperations.SplitBlock(p, "one two".Length);

        Assert.NotNull(created);
        Assert.Equal("<p id=\"p1\" audience=\"admin\">one <b>two</b></p>", Xml(p));
        Assert.Equal("<p audience=\"admin\"><b> three</b> four</p>", Xml(created!));
        Assert.Same(p.NextSibling, created);
    }

    [Fact]
    public void SplitBlock_RefusedWhereContentModelForbidsSecondElement()
    {
        var doc = DitaDocument.Parse("<concept id=\"c\"><title>Заголовок</title><conbody/></concept>");
        Assert.Null(BlockOperations.SplitBlock(doc.Root.FirstElement("title")!, 3));
        Assert.Equal("Заголовок", doc.Root.FirstElement("title")!.InnerText);
    }

    [Fact]
    public void IndentAndOutdent_MoveListItemsBetweenLevels()
    {
        var ul = Block("<ul><li>a</li><li>b</li></ul>");
        var second = ul.ElementChildren().Last();

        var first = ul.ElementChildren().First();
        Assert.True(BlockOperations.IndentItem(second));
        Assert.Equal("a", first.Children[0].Value);
        Assert.Equal("ul", first.Children[^1].Name);
        Assert.Same(first.Children[^1], second.Parent);

        Assert.True(BlockOperations.OutdentItem(second));
        Assert.Equal(new[] { "a", "b" }, ul.ElementChildren().Select(li => li.InnerText.Trim()));
        Assert.Single(first.Children);

        Assert.False(BlockOperations.IndentItem(ul.ElementChildren().First()));
    }
}
