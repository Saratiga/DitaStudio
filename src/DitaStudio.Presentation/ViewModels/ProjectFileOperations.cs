using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>
/// Операции над файлами проекта с обновлением ссылок: переименование и перенос (в том числе по заголовку топика), удаление.
/// Ими пользуются дерево проекта и дерево карты; сами они знают только о документах и рабочей области.
/// </summary>
public sealed class ProjectFileOperations
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly DocumentsViewModel _documents;

    public ProjectFileOperations(ShellContext context, DocumentsViewModel documents)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _documents = documents;

        // Фокус ушёл из изменённого заголовка топика — предложить переименовать файл.
        documents.RootTitleCommitted += pane => _ = OfferRenameByTitleAsync(pane);
    }

    /// <summary>Переименовывает/переносит файл с запросом нового имени и обновлением ссылок.</summary>
    public async Task MoveFileAsync(ProjectFile file)
    {
        var project = _workspace.ProjectOf(file.FullPath) ?? _workspace.Project;
        if (project is null)
        {
            return;
        }

        var newRelative = await _ui.Dialogs.RenameFileAsync(file.RelativePath);
        if (string.IsNullOrWhiteSpace(newRelative))
        {
            return;
        }

        await MoveFileToAsync(file, newRelative);
    }

    // Заголовки, для которых переименование файла уже предлагали и от него отказались: тот же заголовок не спрашиваем снова.
    private readonly Dictionary<string, string> _renameDeclined = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Фокус ушёл из изменённого заголовка топика: спрашивает, переименовать ли файл по новому заголовку
    /// (имя строится так же, как при создании документа), и при согласии переименовывает его с обновлением
    /// ссылок по всему проекту. Отказ запоминается до следующего изменения заголовка.
    /// </summary>
    public async Task OfferRenameByTitleAsync(IDocumentView pane)
    {
        var project = pane.FilePath is { } titlePath ? _workspace.ProjectOf(titlePath) ?? _workspace.Project : _workspace.Project;
        if (project is null || pane.FilePath is not { } path || project.FindFile(path) is not { } file ||
            DitaCatalog.Default.Get(pane.Document.Root.Name)?.IsTopicType != true)
        {
            return;
        }

        var title = string.Join(' ', (pane.Document.Root.FirstElement("title")?.InnerText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0)
        {
            return;
        }

        var extension = Path.GetExtension(file.RelativePath);
        var directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
        var name = DocumentTemplates.SuggestId(title, pane.Document.Root.Name);
        string Candidate(int n) => Path.Combine(directory, (n <= 1 ? name : name + "_" + n) + extension);

        // Уже называется по заголовку (с возможным числовым хвостом) — спрашивать не о чем.
        var currentName = Path.GetFileNameWithoutExtension(file.RelativePath);
        if (string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase) ||
            currentName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && int.TryParse(currentName[(name.Length + 1)..], out _))
        {
            return;
        }

        var number = 1;
        while (File.Exists(Path.Combine(project.RootPath, Candidate(number))))
        {
            number++;
        }

        var newRelative = Candidate(number).Replace('\\', '/');
        if (_renameDeclined.TryGetValue(path, out var declined) && string.Equals(declined, newRelative, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var agreed = await _ui.Dialogs.ConfirmAsync(Loc.T("Msg_TopicTitle"),
            Loc.T("Msg_TheTopicTitleWasChangedRename", file.RelativePath, newRelative) +
            Loc.T("Msg_ReferencesToItInTheProject"));
        if (!agreed)
        {
            _renameDeclined[path] = newRelative;
            return;
        }

        await MoveFileToAsync(file, newRelative);
    }

    /// <summary>Переносит файл на новый относительный путь и обновляет ссылки на него по всему проекту.</summary>
    public async Task MoveFileToAsync(ProjectFile file, string newRelative)
    {
        var project = _workspace.ProjectOf(file.FullPath) ?? _workspace.Project;
        if (project is null)
        {
            return;
        }

        var newFull = Path.GetFullPath(Path.Combine(project.RootPath, newRelative));
        if (string.Equals(newFull, file.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(newFull))
        {
            if (!await _ui.Dialogs.ConfirmAsync(Loc.T("Msg_MoveFile"), Loc.T("Msg_TheFile0AlreadyExistsReplace", newRelative)))
            {
                return;
            }

            File.Delete(newFull);
        }

        foreach (var pane in _documents.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        RefactorResult result;
        try
        {
            result = RefactorService.MoveFile(project, file.FullPath, newFull);
        }
        catch (IOException ex)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_MoveFile"), ex.Message);
            return;
        }

        if (_documents.Panes.Remove(file.FullPath, out var movedPane))
        {
            _documents.Panes[newFull] = movedPane;
            foreach (var tab in _documents.Tabs.Where(t => ReferenceEquals(t.Pane, movedPane)))
            {
                tab.FullPath = newFull;
            }
        }

        _documents.ApplyRefactorResult(result);
        project.Scan();
        _hooks.RefreshProjectTree?.Invoke();
        _hooks.RefreshMapSelector?.Invoke();
        _workspace.NotifyKeysChanged();
        _shell.StatusText = Loc.T("Msg_FileMoved01ReferencesUpdated", file.RelativePath, newRelative, result.UpdatedReferences);
    }

    /// <summary>
    /// Удаляет файл с диска без вопросов (подтверждение — забота вызывающего): открытая вкладка
    /// закрывается без сохранения, файл уходит из проекта. null — удалён, иначе текст ошибки.
    /// </summary>
    public string? DeleteFile(ProjectFile file)
    {
        var project = _workspace.ProjectOf(file.FullPath) ?? _workspace.Project;
        if (project is null)
        {
            return Loc.T("Msg_NoProjectIsOpen");
        }

        try
        {
            File.Delete(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        _documents.DiscardTab(file.FullPath);
        project.RemoveFile(file.FullPath);
        _hooks.RefreshProjectTree?.Invoke();
        _hooks.RefreshMapSelector?.Invoke();
        _workspace.NotifyKeysChanged();
        return null;
    }
}
