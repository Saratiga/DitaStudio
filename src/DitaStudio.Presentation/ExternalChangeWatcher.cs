using DitaStudio.Core.IO;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation;

/// <summary>
/// Замечает, что файлы открытых документов поменяла другая программа (git pull, соседний
/// редактор). Источник сигнала — <see cref="FileSystemWatcher"/> на папку проекта и активация
/// окна (на случай пропущенных событий, например в сетевой папке); решение принимается по
/// отпечатку файла <see cref="DitaStudio.Core.Model.DitaDocument.HasChangedOnDisk"/>, поэтому
/// собственные сохранения редактора тревоги не вызывают.
///
/// Вкладка без несохранённых правок перечитывается молча. Вкладка с правками — только после
/// вопроса; отказ запоминает новый отпечаток и больше не спрашивает до следующего изменения.
/// </summary>
public sealed class ExternalChangeWatcher : IDisposable
{
    private static readonly string[] WatchedExtensions = { ".dita", ".ditamap", ".xml", ".ditaval" };

    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly IDocumentHost _docs;
    private readonly IUiTimer _debounce;
    // По наблюдателю на папку каждого открытого проекта.
    private readonly Dictionary<DitaProject, FileSystemWatcher> _watchers = new();
    private bool _checking;

    public ExternalChangeWatcher(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _docs = docs;
        // Одна запись файла порождает серию событий — проверяем один раз, когда серия утихла.
        _debounce = context.Ui.Platform.CreateTimer(TimeSpan.FromMilliseconds(400), OnDebounceTick);
    }

    private void OnDebounceTick()
    {
        _debounce.Stop();
        _ = CheckNowAsync();
    }

    /// <summary>Ставит папку проекта на наблюдение; другие открытые проекты наблюдаются по-прежнему.</summary>
    public void Attach(DitaProject project)
    {
        Detach(project);
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(project.RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Error += (_, _) => Schedule(); // переполнение буфера — просто проверяем всё
            watcher.EnableRaisingEvents = true;
            _watchers[project] = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Папку не удалось поставить на наблюдение — остаётся проверка при активации окна.
            watcher?.Dispose();
        }
    }

    /// <summary>Снимает с наблюдения папку одного проекта (его закрыли).</summary>
    public void Detach(DitaProject project)
    {
        if (_watchers.Remove(project, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    public void Detach()
    {
        _debounce.Stop();
        foreach (var project in _watchers.Keys.ToList())
        {
            Detach(project);
        }
    }

    public void Dispose() => Detach();

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var relevant = IsWatched(e.FullPath) || (e is RenamedEventArgs renamed && IsWatched(renamed.OldFullPath));
        if (relevant)
        {
            Schedule();
        }
    }

    private static bool IsWatched(string path) =>
        !Path.GetFileName(path).StartsWith(AtomicFile.TempPrefix, StringComparison.Ordinal) &&
        WatchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // События FileSystemWatcher приходят из пула потоков.
    private void Schedule() => _ui.Platform.Post(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    /// <summary>Сверяет открытые документы с диском и реагирует на изменения.</summary>
    public async Task CheckNowAsync()
    {
        if (_checking || _workspace.Projects.Count == 0)
        {
            return;
        }

        // Диалог ниже активирует окно заново — повторный вход не нужен.
        _checking = true;
        try
        {
            var changed = false;
            foreach (var pane in _docs.Panes.Values.ToList())
            {
                if (pane.Document.HasChangedOnDisk())
                {
                    changed |= await HandleChangedPaneAsync(pane);
                }
            }

            // Документы, прочитанные проектом для ссылок и ключей, но не открытые во вкладках:
            // выбрасываем из кэша, следующее обращение перечитает свежую версию.
            var openDocuments = _docs.Panes.Values.Select(p => p.Document).ToHashSet();
            foreach (var project in _workspace.Projects.ToList())
            {
                foreach (var doc in project.OpenDocuments.ToList())
                {
                    if (!openDocuments.Contains(doc) && !doc.IsDirty && doc.FilePath is not null && doc.HasChangedOnDisk())
                    {
                        project.Invalidate(doc.FilePath);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                foreach (var project in _workspace.Projects)
                {
                    project.RebuildKeySpace();
                }

                _workspace.NotifyKeysChanged();
                _docs.RefreshAllTabTitles();
                _hooks.RefreshEditorContext?.Invoke();
            }
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task<bool> HandleChangedPaneAsync(IDocumentView pane)
    {
        var path = pane.FilePath!;
        var name = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            pane.MarkMissingOnDisk();
            _shell.StatusText = Loc.T("Msg_TheFile0WasDeletedOr", name);
            return true;
        }

        if (!pane.IsDirty)
        {
            return TryReload(pane, name, Loc.T("Msg_TheFile0WasChangedBy", name));
        }

        var answer = await _ui.Dialogs.AskAsync(
            Loc.T("Msg_FileChangedExternally"),
            Loc.T("Msg_TheFile0WasChangedBy2", name) +
            Loc.T("Msg_YesLoadTheVersionFromDisk") +
            Loc.T("Msg_NoKeepYourVersionWhenSaved"),
            AskButtons.YesNo,
            AskIcon.Warning);

        if (answer == AskResult.Yes)
        {
            if (TryReload(pane, name, Loc.T("Msg_TheFile0WasReloadedFrom", name)))
            {
                return true;
            }

            // Не перечитался — не спрашиваем снова на каждой активации окна.
            pane.Document.DiskStamp = FileStamp.Of(path);
            return false;
        }

        // Свою версию оставили — считаем текущее состояние диска «увиденным».
        pane.Document.DiskStamp = FileStamp.Of(path);
        _shell.StatusText = Loc.T("Msg_KeptYourVersionOf0When", name);
        return false;
    }

    private bool TryReload(IDocumentView pane, string name, string status)
    {
        try
        {
            pane.ReloadFromDisk();
            _shell.StatusText = status;
            return true;
        }
        catch (Exception ex)
        {
            // Чаще всего файл ещё дописывается или временно невалиден (конфликт слияния).
            // Отпечаток не обновляем — следующая запись файла вызовет новую попытку.
            _shell.StatusText = Loc.T("Msg_CouldNotReload01", name, ex.Message);
            return false;
        }
    }
}
