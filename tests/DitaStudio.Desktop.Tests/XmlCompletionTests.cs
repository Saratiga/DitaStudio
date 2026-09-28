using DitaStudio.Presentation.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Автодополнение исходного XML по каталогу DITA (общее для обеих оболочек).</summary>
public class XmlCompletionTests
{
    [Fact]
    public void ElementName_SuggestsChildrenAllowedByParentModel()
    {
        var (items, prefix) = XmlCompletion.Suggest("<concept id=\"c\"><title>T</title><conbody><");
        Assert.Equal(string.Empty, prefix);
        Assert.Contains(items, i => i.Text == "p");
        Assert.Contains(items, i => i.Text == "section");
        Assert.DoesNotContain(items, i => i.Text == "conbody");
        Assert.Equal("p></p>", items.First(i => i.Text == "p").Insert);
    }

    [Fact]
    public void ElementName_FiltersByTypedPrefix()
    {
        var (items, prefix) = XmlCompletion.Suggest("<concept id=\"c\"><title>T</title><conbody><no");
        Assert.Equal("no", prefix);
        Assert.All(items, i => Assert.StartsWith("no", i.Text));
        Assert.Contains(items, i => i.Text == "note");
    }

    [Fact]
    public void AttributeName_And_EnumerationValue()
    {
        var (attributes, _) = XmlCompletion.Suggest("<concept id=\"c\"><conbody><note ");
        Assert.Contains(attributes, i => i.Text == "type" && i.Insert == "type=\"\"" && i.CaretBack == 1);

        var (values, _) = XmlCompletion.Suggest("<concept id=\"c\"><conbody><note type=\"");
        Assert.Contains(values, i => i.Text == "warning");
        Assert.Contains(values, i => i.Text == "tip");
    }

    [Fact]
    public void EmptyElement_InsertsSelfClosingTag()
    {
        var (items, _) = XmlCompletion.Suggest("<topic id=\"t\"><title>T</title><body><p>Текст <");
        var image = items.FirstOrDefault(i => i.Text == "image");
        Assert.NotNull(image);
        Assert.Equal("image></image>", image!.Insert);
        var (inside, _) = XmlCompletion.Suggest("<map><title>M</title><topicref href=\"a.dita\"><");
        Assert.NotEmpty(inside);
    }
}
