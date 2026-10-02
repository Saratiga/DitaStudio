using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Связь ViewModel'ей «вверх» идёт событиями, а не ссылками друг на друга: вкладки сообщают, что документ сохранён или закрыт
/// (копия для восстановления больше не нужна), а рабочая область — что ключи могли измениться (панель ключей перечитывается).
/// </summary>
public sealed class ViewModelEventsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DitaStudioEventTests", Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string? _recentBackup;
    private readonly string _recentPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public ViewModelEventsTests()
    {
        _recentBackup = File.Exists(_recentPath) ? File.ReadAllText(_recentPath) : null;
        _project = Path.Combine(_root, "GuideSample");
        CopyDirectory(Path.Combine(RepositoryRoot(), "samples", "GuideSample"), _project);
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

    private async Task<(MainWindow Window, Presentation.ViewModels.MainViewModel Vm)> OpenAsync()
    {
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_project);
        Dispatcher.UIThread.RunJobs();
        return (window, window.ViewModel);
    }

    [AvaloniaFact]
    public async Task Save_RaisesDocumentSettledAndKeysChanged()
    {
        var (window, vm) = await OpenAsync();
        var file = Path.GetFullPath(Path.Combine(_project, "concepts", "about.dita"));
        var pane = vm.OpenDocument(file)!;
        pane.Document.IsDirty = true;
        var settled = new List<string>();
        var keysChanged = 0;
        vm.Documents.DocumentSettled += settled.Add;
        vm.Workspace.KeysChanged += (_, _) => keysChanged++;

        await vm.Documents.SaveCurrentCommand.ExecuteAsync(null);

        Assert.Contains(file, settled, StringComparer.OrdinalIgnoreCase);
        Assert.True(keysChanged >= 1, "после сохранения панель ключей должна перечитаться");
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClosingTab_RaisesDocumentSettled_ForThatFile()
    {
        var (window, vm) = await OpenAsync();
        var file = Path.GetFullPath(Path.Combine(_project, "concepts", "about.dita"));
        vm.OpenDocument(file);
        var settled = new List<string>();
        vm.Documents.DocumentSettled += settled.Add;

        await vm.Documents.CloseCurrentTabCommand.ExecuteAsync(null);

        Assert.Equal(new[] { file }, settled.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        window.Close();
    }

    [AvaloniaFact]
    public async Task NotifyKeysChanged_ReachesProjectPanelWithoutDirectReference()
    {
        var (window, vm) = await OpenAsync();
        var before = vm.ProjectPanel.Keys.Count;
        Assert.True(before > 0);
        vm.ProjectPanel.Keys.Clear();

        vm.Workspace.NotifyKeysChanged();

        Assert.Equal(before, vm.ProjectPanel.Keys.Count); // список ключей перечитан по событию
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
