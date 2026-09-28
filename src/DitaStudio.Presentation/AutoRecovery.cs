using System.Text;
using DitaStudio.Core.IO;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;

namespace DitaStudio.Presentation;

/// <summary>
/// Копии несохранённых документов на случай сбоя. Раз в <see cref="Interval"/> текст каждой
/// вкладки с несохранёнными правками пишется в <see cref="RecoveryStore"/> — оригиналы не
/// трогаются. После сохранения или явного отказа от правок копия удаляется; копии, оставшиеся
/// после аварийного завершения, предлагаются к восстановлению при следующем открытии проекта.
/// </summary>
public sealed class AutoRecovery
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly MainViewModel _main;
    private readonly IUiTimer _timer;

    // Последняя записанная копия по пути (текст + отпечаток оригинала) — чтобы не
    // переписывать её без изменений.
    private readonly Dictionary<string, (string Text, FileStamp? Stamp)> _written = new(StringComparer.OrdinalIgnoreCase);
    private RecoveryStore? _store;
    private bool _reportedFailure;

    public AutoRecovery(MainViewModel main)
    {
        _main = main;
        _timer = main.Services.Platform.CreateTimer(Interval, SnapshotAll);
    }

    public void Attach(DitaProject project)
    {
        Detach();
        _store = RecoveryStore.ForProject(RecoveryStore.DefaultRoot, project.RootPath);
        _timer.Start();
    }

    public void Detach()
    {
        _timer.Stop();
        _store = null;
        _written.Clear();
        _reportedFailure = false;
    }

    /// <summary>Записывает копии всех вкладок с несохранёнными правками. Вызывается по таймеру
    /// и сразу при непредвиденной ошибке в приложении.</summary>
    public void SnapshotAll()
    {
        if (_store is null)
        {
            return;
        }

        foreach (var pane in _main.Panes.Values.ToList())
        {
            var path = pane.FilePath;
            if (path is null)
            {
                continue;
            }

            try
            {
                if (!pane.IsDirty)
                {
                    if (_written.Remove(path))
                    {
                        _store.Remove(path);
                    }

                    continue;
                }

                var text = pane.SnapshotXml();
                var stamp = pane.Document.DiskStamp;
                if (_written.TryGetValue(path, out var previous) && previous.Text == text && previous.Stamp == stamp)
                {
                    continue;
                }

                _store.Save(path, text, stamp);
                _written[path] = (text, stamp);
            }
            catch (Exception ex)
            {
                // Копия — страховка, а не основное сохранение: работа продолжается,
                // но один раз за сеанс проекта сообщаем, что страховки нет.
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    _main.StatusText = $"Не удалось записать копию для восстановления ({Path.GetFileName(path)}): {ex.Message}";
                }
            }
        }
    }

    /// <summary>Правки документа сохранены или отброшены — копия больше не нужна.</summary>
    public void Forget(string path)
    {
        _written.Remove(path);
        try
        {
            _store?.Remove(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // лишняя копия лишь предложит восстановление, которое можно отклонить
        }
    }

    /// <summary>Пользователь закрыл окно или проект и отказался сохранять оставшиеся правки.</summary>
    public void ForgetAll()
    {
        _written.Clear();
        try
        {
            _store?.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Если с прошлого сеанса остались копии — предлагает восстановить их.
    /// Вызывать сразу после открытия проекта.</summary>
    public async Task OfferRestoreAsync()
    {
        if (_store is null)
        {
            return;
        }

        IReadOnlyList<RecoveryEntry> entries;
        try
        {
            entries = _store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (entries.Count == 0)
        {
            return;
        }

        var root = _main.Project?.RootPath ?? string.Empty;
        var text = new StringBuilder();
        text.AppendLine("Прошлый сеанс завершился, не сохранив правки. Найдены копии документов:");
        text.AppendLine();
        foreach (var entry in entries)
        {
            var name = Path.GetRelativePath(root, entry.OriginalPath);
            var when = entry.SavedAtUtc.ToLocalTime().ToString("g");
            var note = !File.Exists(entry.OriginalPath)
                ? " — файла больше нет"
                : entry.OriginalChangedSince ? " — файл с тех пор менялся" : string.Empty;
            text.AppendLine($"  • {name} ({when}){note}");
        }

        text.AppendLine();
        text.AppendLine("Да — открыть документы с восстановленными правками (они не сохранены, проверьте и сохраните).");
        text.AppendLine("Нет — удалить копии.");
        text.Append("Отмена — решить позже (копии останутся до следующего открытия проекта).");

        var answer = await _main.Dialogs.AskAsync("Восстановление после сбоя", text.ToString(),
            AskButtons.YesNoCancel, AskIcon.Warning);

        if (answer == AskResult.No)
        {
            ForgetAll();
            return;
        }

        if (answer != AskResult.Yes)
        {
            return;
        }

        var restored = 0;
        var skipped = new List<string>();
        foreach (var entry in entries)
        {
            IDocumentView? pane = File.Exists(entry.OriginalPath) ? _main.Documents.OpenDocument(entry.OriginalPath) : null;
            if (pane is null)
            {
                skipped.Add(Path.GetFileName(entry.OriginalPath));
                continue;
            }

            pane.RestoreFromRecovery(entry.Content);
            _written[entry.OriginalPath] = (entry.Content, entry.OriginalStamp);
            restored++;
        }

        _main.Documents.RefreshAllTabTitles();
        _main.StatusText = $"Восстановлено документов: {restored}. Сохраните их, чтобы записать правки в файлы.";

        if (skipped.Count > 0)
        {
            await _main.Dialogs.MessageAsync("Восстановление после сбоя",
                "Не удалось открыть (файла нет или он не разбирается): " + string.Join(", ", skipped) +
                $".\n\nКопии оставлены в папке:\n{_store.Directory}");
        }
    }
}
