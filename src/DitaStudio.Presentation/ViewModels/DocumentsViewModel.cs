using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Вкладки документов: открытие, сохранение, закрытие. NewDocument оставлен
// в MainWindow.Documents.cs — завязан на дерево проекта (ещё не мигрировано).
public partial class DocumentsViewModel : ObservableObject, IDocumentHost
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    /// <summary>Открытые вкладки по полному пути.</summary>
    public Dictionary<string, IDocumentView> Panes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Документ выбранной вкладки или null.</summary>
    public IDocumentView? Current => SelectedTab?.Pane;

    /// <summary>Общий хвост рефакторинга (перенос файла, переименование id, вынесение в conref): документы, открытые во вкладках,
    /// помечаются несохранёнными и перерисовываются (пользователь сохранит сам, как обычную правку); закрытые документы
    /// сохраняются на диск сразу — иначе изменения в файлах, которые никто сейчас не видит, легко потерять.</summary>
    public void ApplyRefactorResult(RefactorResult result)
    {
        foreach (var doc in result.ChangedDocuments)
        {
            var openPane = Panes.Values.FirstOrDefault(p => ReferenceEquals(p.Document, doc));
            if (openPane is not null)
            {
                openPane.Document.IsDirty = true;
                openPane.ReloadViews();
            }
            else if (doc.FilePath is not null)
            {
                doc.Save(doc.FilePath);
            }
        }

        RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
    }

    [ObservableProperty]
    private TabViewModel? selectedTab;

    public DocumentsViewModel(ShellContext context)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
    }

    public event Action<string>? DocumentSettled;

    public event Action<DitaProject?>? ProjectSettled;

    public event Action<IDocumentView>? RootTitleCommitted;

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        // Вкладка чужого проекта делает его активным: каталог элементов, ключи, карты, поиск и публикация — его.
        if (value?.Project is { } project && _workspace.Projects.Contains(project))
        {
            _workspace.ActivateProject(project);
        }

        _hooks.RefreshEditorContext?.Invoke();
    }

    public IDocumentView? OpenDocument(string path)
    {
        var full = Path.GetFullPath(path);
        var project = _workspace.ProjectOf(full) ?? _workspace.Project;
        if (project is null)
        {
            return null;
        }

        if (Panes.TryGetValue(full, out var existing))
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
            _ = _ui.Dialogs.MessageAsync(Loc.T("Msg_OpenFile"), Loc.T("Msg_CouldNotParse01", Path.GetFileName(full), ex.Message));
            return null;
        }

        var pane = _ui.CreateDocumentView(project, document);
        var tab = new TabViewModel(this, pane, full) { Project = project };
        tab.RefreshTitle();
        if (IsPinnedPath(project, full))
        {
            tab.IsPinned = true;
        }

        pane.DirtyChanged += (_, _) => tab.RefreshTitle();
        pane.SelectionChanged += (_, _) => _hooks.RefreshEditorContext?.Invoke();
        pane.RootTitleCommitted += (_, _) => RootTitleCommitted?.Invoke(pane);
        pane.OpenFileRequested += (_, path) => OpenDocument(path);
        pane.StatusRequested += (_, message) => _shell.StatusText = message;
        pane.Saved += (_, _) =>
        {
            if (pane.FilePath is { } saved)
            {
                DocumentSettled?.Invoke(saved);
            }
        };

        // Закреплённые вкладки стоят слева, после уже закреплённых.
        Tabs.Insert(tab.IsPinned ? Tabs.TakeWhile(t => t.IsPinned).Count() : Tabs.Count, tab);
        SelectedTab = tab;
        Panes[full] = pane;

        _shell.StatusText = Loc.T("Msg_Opened0", Path.GetFileName(full));
        return pane;
    }

    internal bool ShowProjectInTitles => _workspace.Projects.Count > 1;

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
            DocumentSettled?.Invoke(tab.FullPath);
            Panes.Remove(tab.FullPath);
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
            var answer = await _ui.Dialogs.AskAsync("DITA Studio", Loc.T("Msg_SaveChangesTo0", pane.Title), AskButtons.YesNoCancel);

            if (answer == AskResult.Cancel)
            {
                return;
            }

            if (answer == AskResult.Yes && !pane.Save(out var error))
            {
                await _ui.Dialogs.MessageAsync(Loc.T("Msg_Save"), error ?? Loc.T("Msg_CouldNotSaveTheFile"));
                return;
            }
        }

        // Сохранено или пользователь отказался от правок — копия для восстановления не нужна.
        DocumentSettled?.Invoke(tab.FullPath);
        Panes.Remove(tab.FullPath);
        Tabs.Remove(tab);
        if (tab.IsPinned)
        {
            SavePinned(tab.Project ?? _workspace.Project); // закрытая вкладка больше не закреплена — при следующем открытии проекта сама не откроется
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

        SavePinned(tab.Project ?? _workspace.Project);
        SelectedTab = tab;
    }

    private void SavePinned(DitaProject? project)
    {
        if (project is not null)
        {
            project.SetPinnedFiles(Tabs.Where(t => t.IsPinned && ReferenceEquals(t.Project ?? _workspace.Project, project)).Select(t => RelativeOf(project, t.FullPath)));
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
    public void RestorePinnedTabs(DitaProject? target = null)
    {
        if ((target ?? _workspace.Project) is not { } project)
        {
            return;
        }

        foreach (var relative in project.PinnedFiles)
        {
            var full = Path.GetFullPath(Path.Combine(project.RootPath, relative));
            if (File.Exists(full) && !Tabs.Any(t => string.Equals(t.FullPath, full, StringComparison.OrdinalIgnoreCase)))
            {
                OpenDocument(full);
            }
        }

        SelectedTab = Tabs.FirstOrDefault(t => ReferenceEquals(t.Project, project)) ?? Tabs.FirstOrDefault();
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
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_Save"), error ?? Loc.T("Msg_CouldNotSaveTheFile"));
            return;
        }

        (tab!.Project ?? _workspace.Project)?.RebuildKeySpace();
        _workspace.NotifyKeysChanged();
        tab.RefreshTitle();
        _shell.StatusText = Loc.T("Msg_Saved0", Path.GetFileName(pane.FilePath ?? pane.Title));
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
                await _ui.Dialogs.MessageAsync(Loc.T("Msg_Save"), error ?? Loc.T("Msg_CouldNotSaveTheFile"));
            }
        }

        foreach (var project in _workspace.Projects)
        {
            project.RebuildKeySpace();
        }

        _workspace.Project?.RebuildKeySpace();
        _workspace.NotifyKeysChanged();
        _shell.StatusText = Loc.T("Msg_FilesSaved0", saved);
    }

    // Вызывается из MainWindow.OnClosing — там же живой Window.OnClosing,
    // которого у VM быть не может, — и перед сменой проекта.
    // true — можно закрывать: всё сохранено или пользователь отказался от
    // правок (тогда и копии для восстановления удаляются). Если сохранить
    // не удалось, возвращает false — иначе правки пропали бы молча.
    public async Task<bool> ConfirmCloseAsync(DitaProject? onlyProject = null)
    {
        var scope = onlyProject is null ? Tabs.ToList() : Tabs.Where(t => ReferenceEquals(t.Project, onlyProject)).ToList();
        foreach (var tab in scope)
        {
            tab.Pane.CommitPendingEdits();
        }

        var dirty = scope.Count(t => t.Pane.IsDirty);
        if (dirty == 0)
        {
            return true;
        }

        var answer = await _ui.Dialogs.AskAsync("DITA Studio",
            Loc.T("Msg_UnsavedDocuments0SaveBeforeExit", dirty), AskButtons.YesNoCancel);

        if (answer == AskResult.Cancel)
        {
            return false;
        }

        if (answer == AskResult.Yes)
        {
            await SaveAll();
            if (scope.Any(t => t.Pane.IsDirty))
            {
                return false;
            }
        }

        if (onlyProject is null)
        {
            ProjectSettled?.Invoke(null);
        }
        else
        {
            ProjectSettled?.Invoke(onlyProject);
        }

        return true;
    }

    /// <summary>Закрывает все вкладки проекта без вопросов (вопрос о несохранённом задаётся раньше — <see cref="ConfirmCloseAsync"/>).</summary>
    public void DiscardTabsOf(DitaProject project)
    {
        foreach (var tab in Tabs.Where(t => ReferenceEquals(t.Project, project)).ToList())
        {
            Panes.Remove(tab.FullPath);
            Tabs.Remove(tab);
            (tab.Pane as IDisposable)?.Dispose();
        }
    }
}
