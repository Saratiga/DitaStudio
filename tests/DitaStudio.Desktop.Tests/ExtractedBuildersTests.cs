using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Статические помощники, выделенные из InsertViewModel и MapViewModel, — без окна и ViewModel'ей.</summary>
public sealed class ExtractedBuildersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DitaStudioBuilderTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // временные файлы удалятся системой
        }
    }

    [Fact]
    public void RelTableConverter_RoundTripsRowsThroughReltableNode()
    {
        Directory.CreateDirectory(_root);
        const string topic = "<?xml version=\"1.0\"?><topic id=\"{0}\"><title>T{0}</title><body/></topic>";
        File.WriteAllText(Path.Combine(_root, "a.dita"), string.Format(topic, "a"));
        File.WriteAllText(Path.Combine(_root, "b.dita"), string.Format(topic, "b"));
        File.WriteAllText(Path.Combine(_root, "m.ditamap"), "<?xml version=\"1.0\"?><map><title>M</title></map>");
        var project = new DitaProject(_root);
        project.Scan();
        var mapPath = Path.Combine(_root, "m.ditamap");
        var a = project.FindFile(Path.Combine(_root, "a.dita"))!;
        var b = project.FindFile(Path.Combine(_root, "b.dita"))!;
        var rows = new List<List<RelTableCell>>
        {
            new() { new RelTableCell { File = a }, new RelTableCell { File = b, TopicId = "b" }, new RelTableCell() }
        };

        var node = RelTableConverter.Build(rows, mapPath);

        Assert.Equal("reltable", node.Name);
        Assert.Equal(3, node.DescendantsAndSelf().Count(n => n.Name == "relcell"));
        Assert.Equal(2, node.DescendantsAndSelf().Count(n => n.Name == "topicref"));

        var parsed = RelTableConverter.Parse(project, node, mapPath);

        Assert.Single(parsed);
        Assert.Equal(3, parsed[0].Count);
        Assert.Same(a, parsed[0][0].File);
        Assert.Same(b, parsed[0][1].File);
        Assert.Equal("b", parsed[0][1].TopicId);
        Assert.Null(parsed[0][2].File);
    }

    [Fact]
    public void TableNodeBuilder_MakesCalsTableWithTitleHeaderAndRows()
    {
        var table = TableNodeBuilder.Build(new TableResult(Rows: 3, Columns: 4, Header: true, Title: "Параметры"));

        Assert.Equal("table", table.Name);
        Assert.Equal("Параметры", table.FindDescendant("title")!.InnerText);
        var tgroup = table.FindDescendant("tgroup")!;
        Assert.Equal("4", tgroup.GetAttribute("cols"));
        Assert.NotNull(tgroup.FindDescendant("thead"));
        var bodyRows = tgroup.FindDescendant("tbody")!.ElementChildren().Count(r => r.Name == "row");
        Assert.Equal(3, bodyRows); // шапка — отдельная строка сверх Rows
        Assert.Equal(4, tgroup.FindDescendant("thead")!.FindDescendant("row")!.ElementChildren().Count(e => e.Name == "entry"));
    }

    [Fact]
    public void TableNodeBuilder_WithoutTitleAndHeader_HasNoTitleNorThead()
    {
        var table = TableNodeBuilder.Build(new TableResult(Rows: 2, Columns: 2, Header: false, Title: " "));

        Assert.Null(table.FindDescendant("title"));
        Assert.Null(table.FindDescendant("thead"));
        Assert.Equal(2, table.FindDescendant("tbody")!.ElementChildren().Count(r => r.Name == "row"));
    }
}
