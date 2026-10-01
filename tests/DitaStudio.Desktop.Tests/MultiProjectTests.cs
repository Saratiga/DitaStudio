using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.ViewModels;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Несколько проектов в одном окне (Д7): второй проект открывается рядом с первым, у каждого свои вкладки, каталог, карты;
/// закрытие одного не трогает другой. Проекты — две копии samples/GuideSample с одноимёнными топиками.
/// </summary>
public sealed class MultiProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DitaStudioDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly string _first;
    private readonly string _second;
    private readonly string? _recentBackup;
    private readonly string _recentPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public MultiProjectTests()
    {
        _recentBackup = File.Exists(_recentPath) ? File.ReadAllText(_recentPath) : null;
        _first = Path.Combine(_root, "FirstSample");
        _second = Path.Combine(_root, "SecondSample");
        var sample = Path.Combine(RepositoryRoot(), "samples", "GuideSample");
        CopyDirectory(sample, _first);
        CopyDirectory(sample, _second);
    }

    public void Dispose()
    {
        try
        {
            if (_recentBackup is null)
            {
                File.Delete(_recentPath);
            }
            else
            {
                File.WriteAllText(_recentPath, _recentBackup);
            }

            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // временные файлы удалятся системой
        }
    }

    private async Task<(MainWindow Window, MainViewModel Vm)> OpenBothAsync()
    {
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_first);
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_second);
        Dispatcher.UIThread.RunJobs();
        return (window, window.ViewModel);
    }

    [AvaloniaFact]
    public async Task SecondProject_OpensAlongsideFirst()
    {
        var (window, vm) = await OpenBothAsync();

        Assert.Equal(2, vm.Projects.Count);
        Assert.Equal(new[] { "FirstSample", "SecondSample" }, vm.ProjectPanel.Tree.Select(n => n.Name));
        Assert.All(vm.ProjectPanel.Tree, n => Assert.True(n.IsProjectRoot));
        Assert.Same(vm.Projects[1], vm.Project);
        Assert.Equal("DITA Studio — SecondSample", window.Title);
        Assert.Same(vm.Project!.Catalog, DitaCatalog.Default);
        Assert.Contains("Открыто проектов: 2", vm.StatusText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OpeningSameFolderAgain_JustActivatesIt()
    {
        var (window, vm) = await OpenBothAsync();

        await vm.ProjectPanel.LoadProjectAsync(_first);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Projects.Count);
        Assert.Equal(2, vm.ProjectPanel.Tree.Count);
        Assert.Same(vm.Projects[0], vm.Project);
        Assert.Equal("DITA Studio — FirstSample", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SameNamedTopics_OpenInTheirOwnProjects_AndTabSelectionActivatesProject()
    {
        var (window, vm) = await OpenBothAsync();
        var a = Path.Combine(_first, "concepts", "about.dita");
        var b = Path.Combine(_second, "concepts", "about.dita");

        vm.OpenDocument!(a);
        vm.OpenDocument!(b);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Documents.Tabs.Count);
        var tabA = vm.Documents.Tabs.Single(t => t.FullPath == Path.GetFullPath(a));
        var tabB = vm.Documents.Tabs.Single(t => t.FullPath == Path.GetFullPath(b));
        Assert.Same(vm.Projects[0], tabA.Project);
        Assert.Same(vm.Projects[1], tabB.Project);
        Assert.NotSame(tabA.Pane.Document, tabB.Pane.Document);
        Assert.Equal("about.dita · FirstSample", tabA.Title);
        Assert.Equal("about.dita · SecondSample", tabB.Title);

        vm.Documents.SelectedTab = tabA;
        Assert.Same(vm.Projects[0], vm.Project);
        Assert.Same(vm.Projects[0].Catalog, DitaCatalog.Default);
        vm.Documents.SelectedTab = tabB;
        Assert.Same(vm.Projects[1], vm.Project);
        Assert.Equal("DITA Studio — SecondSample", window.Title);

        // Повторное открытие того же файла не плодит вкладку.
        vm.OpenDocument!(a);
        Assert.Equal(2, vm.Documents.Tabs.Count);
        Assert.Same(tabA, vm.Documents.SelectedTab);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SelectingFileInTree_ActivatesItsProject()
    {
        var (window, vm) = await OpenBothAsync();
        var firstRoot = vm.ProjectPanel.Tree[0];
        var file = firstRoot.Children.First(c => c.File is not null);

        vm.ProjectPanel.SelectedTreeNode = file;

        Assert.Same(vm.Projects[0], vm.Project);
        vm.ProjectPanel.SelectedTreeNode = vm.ProjectPanel.Tree[1];
        Assert.Same(vm.Projects[1], vm.Project);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CloseProject_LeavesOtherProjectAndItsTabs()
    {
        var (window, vm) = await OpenBothAsync();
        var a = Path.Combine(_first, "concepts", "about.dita");
        var b = Path.Combine(_second, "concepts", "about.dita");
        vm.OpenDocument!(a);
        vm.OpenDocument!(b);
        Dispatcher.UIThread.RunJobs();

        Assert.True(await vm.ProjectPanel.CloseProjectAsync(vm.Projects[1]));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Projects);
        Assert.Equal("FirstSample", Assert.Single(vm.ProjectPanel.Tree).Name);
        var tab = Assert.Single(vm.Documents.Tabs);
        Assert.Equal(Path.GetFullPath(a), tab.FullPath);
        Assert.Same(vm.Projects[0], vm.Project);
        Assert.Equal("DITA Studio — FirstSample", window.Title);
        Assert.Same(vm.Project!.Catalog, DitaCatalog.Default);
        Assert.Contains("закрыт", vm.StatusText);

        // Последний проект закрывается — окно пустое.
        Assert.True(await vm.ProjectPanel.CloseProjectAsync(vm.Projects[0]));
        Assert.Empty(vm.Projects);
        Assert.Empty(vm.ProjectPanel.Tree);
        Assert.Empty(vm.Documents.Tabs);
        Assert.Null(vm.Project);
        Assert.Equal("DITA Studio", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MapTabs_OnePerProject_SwitchingActivatesProject_CloseWorks()
    {
        var (window, vm) = await OpenBothAsync();

        Assert.Equal(2, vm.Map.OpenMaps.Count);
        Assert.Equal(2, vm.Map.OpenMaps.Select(t => t.Project).Distinct().Count());
        Assert.Contains("FirstSample", vm.Map.OpenMaps[0].Title);
        Assert.Same(vm.Map.OpenMaps[1], vm.Map.SelectedMapTab);
        Assert.Same(vm.Map.SelectedMapTab!.File, vm.Map.SelectedMap);

        vm.Map.SelectedMapTab = vm.Map.OpenMaps[0];
        Assert.Same(vm.Projects[0], vm.Project);
        Assert.Same(vm.Map.OpenMaps[0], vm.Map.SelectedMapTab);
        Assert.Equal(vm.Map.OpenMaps[0].File.FullPath, vm.Map.SelectedMap!.FullPath);
        Assert.Equal(vm.Projects[0].Maps.Count(), vm.Map.Maps.Count);

        // Закрыть вкладку (ПКМ → «Закрыть»): выбирается соседняя — это карта другого проекта, он становится активным.
        var first = vm.Map.OpenMaps[0];
        vm.Map.CloseMapCommand.Execute(first);
        Assert.Single(vm.Map.OpenMaps);
        Assert.Same(vm.Projects[1], vm.Project);
        Assert.Same(vm.Map.OpenMaps[0], vm.Map.SelectedMapTab);

        // Карту можно открыть снова: выбор карты проекта создаёт вкладку.
        vm.ActivateProject(vm.Projects[0]);
        Assert.Contains(vm.Map.OpenMaps, t => ReferenceEquals(t.Project, vm.Projects[0]));
        window.Close();
    }

    [AvaloniaFact]
    public async Task CloseOtherMapTabs_KeepsOnlyChosen_AndCloseProjectDropsItsMaps()
    {
        var (window, vm) = await OpenBothAsync();
        var keep = vm.Map.OpenMaps[0];

        vm.Map.CloseOtherMapsCommand.Execute(keep);

        Assert.Same(keep, Assert.Single(vm.Map.OpenMaps));
        Assert.Same(vm.Projects[0], vm.Project);

        Assert.True(await vm.ProjectPanel.CloseProjectAsync(vm.Projects[0]));
        Assert.Same(vm.Projects[0], vm.Project); // остался второй проект
        var rest = Assert.Single(vm.Map.OpenMaps); // карты первого убраны, карта второго открылась при активации
        Assert.Same(vm.Project, rest.Project);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TwoProjects_Screenshot()
    {
        var (window, vm) = await OpenBothAsync();
        vm.OpenDocument!(Path.Combine(_first, "concepts", "about.dita"));
        vm.OpenDocument!(Path.Combine(_second, "concepts", "about.dita"));
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, "two-projects.png"));
        window.Close();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
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
