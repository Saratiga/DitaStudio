using DitaStudio.Core.IO;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;

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

    private readonly MainViewModel _main;
    private readonly IUiTimer _debounce;
    private FileSystemWatcher? _watcher;
    private bool _checking;

    public ExternalChangeWatcher(MainViewModel main)
    {
        _main = main;

        // Одна запись файла порождает серию событий — проверяем один раз, когда серия утихла.
        _debounce = main.Services.Platform.CreateTimer(TimeSpan.FromMilliseconds(400), OnDebounceTick);
    }

    private void OnDebounceTick()
    {
        _debounce.Stop();
        _ = CheckNowAsync();
    }

    public void Attach(DitaProject project)
    {
        Detach();
        try
        {
            _watcher = new FileSystemWatcher(project.RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += (_, _) => Schedule(); // переполнение буфера — просто проверяем всё
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Папку не удалось поставить на наблюдение — остаётся проверка при активации окна.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Detach()
    {
        _debounce.Stop();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
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
    private void Schedule() => _main.Services.Platform.Post(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    /// <summary>Сверяет открытые документы с диском и реагирует на изменения.</summary>
    public async Task CheckNowAsync()
    {
        var project = _main.Project;
        if (_checking || project is null)
        {
            return;
        }

        // Диалог ниже активирует окно заново — повторный вход не нужен.
        _checking = true;
        try
        {
            var changed = false;
            foreach (var pane in _main.Panes.Values.ToList())
            {
                if (pane.Document.HasChangedOnDisk())
                {
                    changed |= await HandleChangedPaneAsync(pane);
                }
            }

            // Документы, прочитанные проектом для ссылок и ключей, но не открытые во вкладках:
            // выбрасываем из кэша, следующее обращение перечитает свежую версию.
            var openDocuments = _main.Panes.Values.Select(p => p.Document).ToHashSet();
            foreach (var doc in project.OpenDocuments.ToList())
            {
                if (!openDocuments.Contains(doc) && !doc.IsDirty && doc.FilePath is not null && doc.HasChangedOnDisk())
                {
                    project.Invalidate(doc.FilePath);
                    changed = true;
                }
            }

            if (changed)
            {
                project.RebuildKeySpace();
                _main.ProjectPanel.RefreshKeysList();
                _main.Documents.RefreshAllTabTitles();
                _main.RefreshEditorContext?.Invoke();
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
            _main.StatusText = $"Файл {name} удалён или переименован другой программой. Сохраните вкладку, чтобы записать его заново.";
            return true;
        }

        if (!pane.IsDirty)
        {
            return TryReload(pane, name, $"Файл {name} изменён другой программой — перечитан с диска.");
        }

        var answer = await _main.Dialogs.AskAsync(
            "Файл изменён извне",
            $"Файл «{name}» изменён другой программой, а во вкладке есть несохранённые правки.\n\n" +
            "Да — загрузить версию с диска (ваши правки можно вернуть через «Правка → Отменить структурное изменение», Ctrl+Alt+Z).\n" +
            "Нет — оставить свою версию (при сохранении она заменит файл на диске).",
            AskButtons.YesNo,
            AskIcon.Warning);

        if (answer == AskResult.Yes)
        {
            if (TryReload(pane, name, $"Файл {name} перечитан с диска."))
            {
                return true;
            }

            // Не перечитался — не спрашиваем снова на каждой активации окна.
            pane.Document.DiskStamp = FileStamp.Of(path);
            return false;
        }

        // Свою версию оставили — считаем текущее состояние диска «увиденным».
        pane.Document.DiskStamp = FileStamp.Of(path);
        _main.StatusText = $"Оставлена своя версия {name}; при сохранении она заменит файл на диске.";
        return false;
    }

    private bool TryReload(IDocumentView pane, string name, string status)
    {
        try
        {
            pane.ReloadFromDisk();
            _main.StatusText = status;
            return true;
        }
        catch (Exception ex)
        {
            // Чаще всего файл ещё дописывается или временно невалиден (конфликт слияния).
            // Отпечаток не обновляем — следующая запись файла вызовет новую попытку.
            _main.StatusText = $"Не удалось перечитать {name}: {ex.Message}";
            return false;
        }
    }
}
