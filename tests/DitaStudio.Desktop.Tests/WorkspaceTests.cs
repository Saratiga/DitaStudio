using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.ViewModels;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Рабочая область (открытые проекты и активный) сама по себе — без окна и остальных ViewModel'ей.</summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DitaStudioWorkspaceTests", Guid.NewGuid().ToString("N"));

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

    private DitaProject Make(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return new DitaProject(path);
    }

    [Fact]
    public void ProjectOf_PicksDeepestOpenFolder_AndNullOutside()
    {
        var outer = Make("outer");
        var inner = Make(Path.Combine("outer", "inner"));
        var workspace = new Workspace();
        workspace.Projects.Add(outer);
        workspace.Projects.Add(inner);

        Assert.Same(inner, workspace.ProjectOf(Path.Combine(_root, "outer", "inner", "a.dita")));
        Assert.Same(outer, workspace.ProjectOf(Path.Combine(_root, "outer", "b.dita")));
        Assert.Null(workspace.ProjectOf(Path.Combine(_root, "outer2", "c.dita"))); // «outer2» — не внутри «outer»
    }

    [Fact]
    public void ActivateProject_SetsCatalogAndConditions_RaisesEventOnce_AndIgnoresRepeat()
    {
        var project = Make("p");
        project.SetConditions(new Dictionary<string, HashSet<string>> { ["audience"] = new() { "admin" } }, true);
        var workspace = new Workspace();
        workspace.Projects.Add(project);
        var raised = 0;
        workspace.ActiveProjectChanged += (_, _) => raised++;

        workspace.ActivateProject(project);
        workspace.ActivateProject(project);

        Assert.Equal(1, raised);
        Assert.Same(project, workspace.Project);
        Assert.Same(project.Catalog, DitaCatalog.Default);
        Assert.True(workspace.Conditions!.ShowDraftComments);
        Assert.Contains("admin", workspace.Conditions.Exclude["audience"]);
    }

    [Fact]
    public void Deactivate_ClearsProjectAndConditions_AndRaisesEvent()
    {
        var project = Make("p");
        var workspace = new Workspace();
        workspace.Projects.Add(project);
        workspace.ActivateProject(project);
        var raised = 0;
        workspace.ActiveProjectChanged += (_, _) => raised++;

        workspace.Deactivate();

        Assert.Null(workspace.Project);
        Assert.Null(workspace.Conditions);
        Assert.Equal(1, raised);
    }
}
