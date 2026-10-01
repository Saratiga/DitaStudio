using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Вкладки документов: открытие, сохранение, закрытие. NewDocument оставлен
// в MainWindow.Documents.cs — завязан на дерево проекта (ещё не мигрировано).
public partial class DocumentsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? selectedTab;

    public DocumentsViewModel(MainViewModel main)
    {
        _main = main;
    }

    partial void OnSelectedTabChanged(TabViewModel? value) => _main.RefreshEditorContext?.Invoke();

    public IDocumentView? OpenDocument(string path)
    {
        var project = _main.Project;
        if (project is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (_main.Panes.TryGetValue(full, out var existing))
        {
            SelectedTab = Tabs.FirstOrDefault(t => ReferenceEquals(t.Pane, existing));
            return existing;
        }

        DitaDocument document;
        try
        {
            document = project.GetDocument(full);
        }
        catch (Exception ex)
        {
            _ = _main.Dialogs.MessageAsync("Открытие файла", $"Не удалось разобрать {Path.GetFileName(full)}:\n\n{ex.Message}");
            return null;
        }

        var pane = _main.Services.CreateDocumentView(project, document);
        var tab = new TabViewModel(this, pane, full);
        if (IsPinnedPath(project, full))
        {
            tab.IsPinned = true;
        }

        pane.DirtyChanged += (_, _) => tab.RefreshTitle();
        pane.SelectionChanged += (_, _) => _main.RefreshEditorContext?.Invoke();
        pane.RootTitleCommitted += (_, _) => _ = _main.ProjectPanel.OfferRenameByTitleAsync(pane);
        pane.OpenFileRequested += (_, path) => _main.OpenDocument?.Invoke(path);
        pane.StatusRequested += (_, message) => _main.StatusText = message;
        pane.Saved += (_, _) =>
        {
            if (pane.FilePath is { } saved)
            {
                _main.Recovery.Forget(saved);
            }
        };

        // Закреплённые вкладки стоят слева, после уже закреплённых.
        Tabs.Insert(tab.IsPinned ? Tabs.TakeWhile(t => t.IsPinned).Count() : Tabs.Count, tab);
        SelectedTab = tab;
        _main.Panes[full] = pane;

        _main.StatusText = $"Открыт {Path.GetFileName(full)}";
        return pane;
    }

    public void RefreshAllTabTitles()
    {
        foreach (var tab in Tabs)
        {
            tab.RefreshTitle();
        }
    }

    /// <summary>Закрывает вкладку файла без сохранения (файл удалён).</summary>
    public void DiscardTab(string fullPath)
    {
        foreach (var tab in Tabs.Where(t => string.Equals(t.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _main.Recovery.Forget(tab.FullPath);
            _main.Panes.Remove(tab.FullPath);
            Tabs.Remove(tab);
            (tab.Pane as IDisposable)?.Dispose();
        }
    }

    public async Task CloseTabAsync(TabViewModel tab)
    {
        var pane = tab.Pane;
        pane.CommitPendingEdits();
        if (pane.IsDirty)
        {
            var answer = await _main.Dialogs.AskAsync("DITA Studio", $"Сохранить изменения в «{pane.Title}»?", AskButtons.YesNoCancel);

            if (answer == AskResult.Cancel)
            {
                return;
            }

            if (answer == AskResult.Yes && !pane.Save(out var error))
            {
                await _main.Dialogs.MessageAsync("Сохранение", error ?? "Не удалось сохранить файл.");
                return;
            }
        }

        // Сохранено или пользователь отказался от правок — копия для восстановления не нужна.
        _main.Recovery.Forget(tab.FullPath);
        _main.Panes.Remove(tab.FullPath);
        Tabs.Remove(tab);
        if (tab.IsPinned)
        {
            SavePinned(); // закрытая вкладка больше не закреплена — при следующем открытии проекта сама не откроется
        }

        // Вкладка оболочки может держать тяжёлые ресурсы (встроенный браузер предпросмотра).
        (pane as IDisposable)?.Dispose();
    }

    [RelayCommand]
    private async Task CloseCurrentTab()
    {
        // Ctrl+W и меню «Закрыть вкладку» закреплённую вкладку не закрывают — для этого есть «Закрыть все» и «Закрыть эту».
        if (SelectedTab is { IsPinned: false } tab)
        {
            await CloseTabAsync(tab);
        }
    }

    // ------------------------------------------------------------ закрепление и групповое закрытие

    private static bool IsPinnedPath(DitaProject project, string fullPath) =>
        project.PinnedFiles.Contains(RelativeOf(project, fullPath), StringComparer.OrdinalIgnoreCase);

    private static string RelativeOf(DitaProject project, string fullPath) =>
        Path.GetRelativePath(project.RootPath, fullPath).Replace('\\', '/');

    /// <summary>Закрепляет или открепляет вкладку и запоминает выбор в проекте (`.ditastudio-pinned`).</summary>
    public void SetPinned(TabViewModel tab, bool pinned)
    {
        tab.IsPinned = pinned;
        // Закреплённые — слева: вкладка перебирается в начало (или сразу после закреплённых).
        var index = Tabs.IndexOf(tab);
        var target = Tabs.Where(t => !ReferenceEquals(t, tab) && t.IsPinned).Count();
        if (pinned && index != target)
        {
            Tabs.Move(index, target);
        }

        SavePinned();
        SelectedTab = tab;
    }

    private void SavePinned()
    {
        if (_main.Project is { } project)
        {
            project.SetPinnedFiles(Tabs.Where(t => t.IsPinned).Select(t => RelativeOf(project, t.FullPath)));
        }
    }

    /// <summary>Закрывает вкладки по очереди — с обычным вопросом о несохранённом; «Отмена» останавливает закрытие.
    /// Закреплённые тоже закрываются (закрепление защищает только от «✕» и Ctrl+W).</summary>
    private async Task CloseManyAsync(IEnumerable<TabViewModel> tabs)
    {
        foreach (var tab in tabs.ToList())
        {
            var before = Tabs.Count;
            await CloseTabAsync(tab);
            if (Tabs.Contains(tab) && Tabs.Count == before)
            {
                return; // отмена или ошибка сохранения — остальные не трогаем
            }
        }
    }

    public Task CloseAllAsync() => CloseManyAsync(Tabs);

    public Task CloseOthersAsync(TabViewModel keep) => CloseManyAsync(Tabs.Where(t => !ReferenceEquals(t, keep)));

    public Task CloseToRightAsync(TabViewModel from) => CloseManyAsync(Tabs.Skip(Tabs.IndexOf(from) + 1));

    [RelayCommand]
    private Task CloseAllTabs() => CloseAllAsync();

    /// <summary>После открытия проекта открывает закреплённые вкладки (файлы, которых уже нет, пропускаются).</summary>
    public void RestorePinnedTabs()
    {
        if (_main.Project is not { } project)
        {
            return;
        }

        foreach (var relative in project.PinnedFiles)
        {
            var full = Path.GetFullPath(Path.Combine(project.RootPath, relative));
            if (File.Exists(full) && !Tabs.Any(t => string.Equals(t.FullPath, full, StringComparison.OrdinalIgnoreCase)))
            {
                _main.OpenDocument?.Invoke(full);
            }
        }

        SelectedTab = Tabs.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveCurrent()
    {
        var tab = SelectedTab;
        var pane = tab?.Pane;
        if (pane is null)
        {
            return;
        }

        if (!pane.Save(out var error))
        {
            await _main.Dialogs.MessageAsync("Сохранение", error ?? "Не удалось сохранить файл.");
            return;
        }

        _main.Project?.RebuildKeySpace();
        _main.ProjectPanel.RefreshKeysList();
        tab!.RefreshTitle();
        _main.StatusText = $"Сохранено: {Path.GetFileName(pane.FilePath ?? pane.Title)}";
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        var saved = 0;
        foreach (var tab in Tabs.ToList())
        {
            if (!tab.Pane.IsDirty)
            {
                continue;
            }

            if (tab.Pane.Save(out var error))
            {
                saved++;
                tab.RefreshTitle();
            }
            else
            {
                await _main.Dialogs.MessageAsync("Сохранение", error ?? "Не удалось сохранить файл.");
            }
        }

        _main.Project?.RebuildKeySpace();
        _main.ProjectPanel.RefreshKeysList();
        _main.StatusText = $"Сохранено файлов: {saved}";
    }

    // Вызывается из MainWindow.OnClosing — там же живой Window.OnClosing,
    // которого у VM быть не может, — и перед сменой проекта.
    // true — можно закрывать: всё сохранено или пользователь отказался от
    // правок (тогда и копии для восстановления удаляются). Если сохранить
    // не удалось, возвращает false — иначе правки пропали бы молча.
    public async Task<bool> ConfirmCloseAsync()
    {
        foreach (var tab in Tabs)
        {
            tab.Pane.CommitPendingEdits();
        }

        var dirty = Tabs.Count(t => t.Pane.IsDirty);
        if (dirty == 0)
        {
            return true;
        }

        var answer = await _main.Dialogs.AskAsync("DITA Studio",
            $"Не сохранено документов: {dirty}. Сохранить перед выходом?", AskButtons.YesNoCancel);

        if (answer == AskResult.Cancel)
        {
            return false;
        }

        if (answer == AskResult.Yes)
        {
            await SaveAll();
            if (Tabs.Any(t => t.Pane.IsDirty))
            {
                return false;
            }
        }

        _main.Recovery.ForgetAll();
        return true;
    }
}
