using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Открытие/сканирование папки проекта и список ключей. Дерево файлов
// (BuildProjectTree и связанное) — императивное построение WPF-дерева,
// вызываемое из многих мест (Insert/Documents/Help) — сознательно оставлено
// в MainWindow.Project.cs, тот же класс риска, что и SidePanels (шаг 4).
public partial class ProjectViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<KeyDefinition> Keys { get; } = new();

    [ObservableProperty]
    private KeyDefinition? selectedKey;

    [ObservableProperty]
    private bool scopedKeysHintVisible;

    [ObservableProperty]
    private string scopedKeysHintText = string.Empty;

    /// <summary>Дерево файлов проекта (корень — папка проекта). Строит <see cref="RebuildTree"/>;
    /// WPF-оболочка пока строит своё дерево сама.</summary>
    public ObservableCollection<ProjectTreeNode> Tree { get; } = new();

    [ObservableProperty]
    private ProjectTreeNode? selectedTreeNode;

    public ProjectViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        var folder = await _main.Files.OpenFolderAsync("Выберите папку с проектом DITA");
        if (folder is null)
        {
            return;
        }

        await LoadProjectAsync(folder);
    }

    public async Task LoadProjectAsync(string path)
    {
        // Смена проекта закрывает все вкладки — несохранённые правки не должны пропасть молча.
        if (!await _main.Documents.ConfirmCloseAsync())
        {
            return;
        }

        _main.Recovery.Detach();
        _main.ExternalChanges.Detach();

        var project = new DitaProject(path);
        // Сбой записи настройки проекта (.ditastudio-*) — сразу сообщаем: иначе пользователь
        // считает, что условия/CSS/DTD сохранены, а после перезапуска их не окажется.
        project.SettingsWarning += message => _ = _main.Dialogs.MessageAsync("Настройки проекта", message);
        _main.Project = project;
        foreach (var pane in _main.Panes.Values)
        {
            (pane as IDisposable)?.Dispose();
        }

        _main.Panes.Clear();

        // Каталог проекта активируем до Scan: тип документа (топик/карта) определяется по нему,
        // и корневые элементы специализации из внешнего DTD должны уже быть известны.
        var dtdNote = ActivateProjectCatalog(project);
        _main.Documents.Tabs.Clear();

        try
        {
            project.Scan();
        }
        catch (Exception ex)
        {
            await _main.Dialogs.MessageAsync("Проект", $"Не удалось прочитать папку: {ex.Message}");
            return;
        }

        _main.WindowTitle = $"DITA Studio — {project.Name}";
        _main.Conditions = new ConditionsResult(
            project.ExcludedConditionValues.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
            project.ShowDraftComments);
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        RecentProjects.Add(path);
        _main.RefreshRecentProjectsMenu?.Invoke();

        _main.StatusText = $"Проект открыт: {project.Files.Count} файлов, {project.Keys.Count} ключей.{dtdNote}";

        if (project.SettingsWarnings.Count > 0)
        {
            await _main.Dialogs.MessageAsync("Настройки проекта", string.Join("\n", project.SettingsWarnings));
        }

        _main.Recovery.Attach(project);
        _main.ExternalChanges.Attach(project);
        await _main.Recovery.OfferRestoreAsync();
        _main.Documents.RestorePinnedTabs();
    }

    /// <summary>Строит каталог проекта (встроенный + элементы внешнего DTD, если подключён) и делает
    /// его активным — <see cref="DitaCatalog.Default"/> для палитры, режима «Автор» и остального
    /// кода без ссылки на проект. Элементы DTD прежнего проекта при этом пропадают.
    /// Возвращает короткую приписку к статусной строке (пусто, если DTD не подключён).</summary>
    private static string ActivateProjectCatalog(DitaProject project)
    {
        var (catalog, result) = project.LoadCatalog();
        DitaCatalog.Activate(catalog);
        if (result is null)
        {
            return string.Empty;
        }

        var warningsNote = result.Warnings.Count > 0 ? $", предупреждений {result.Warnings.Count}" : string.Empty;
        return $" Из внешнего DTD подключено элементов: {result.Elements.Count}{warningsNote}.";
    }

    [RelayCommand]
    private void RescanProject()
    {
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        foreach (var pane in _main.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        // Пересканирование перечитывает и внешний DTD — правки .dtd подхватываются без переоткрытия.
        var dtdNote = ActivateProjectCatalog(project);
        project.Scan();
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        _main.RefreshEditorContext?.Invoke();
        _main.StatusText = $"Проект обновлён: {project.Files.Count} файлов.{dtdNote}";
    }

    public void RefreshKeysList()
    {
        var project = _main.Project;
        Keys.Clear();
        if (project is not null)
        {
            foreach (var key in project.Keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Keys.Add(key);
            }
        }

        var hidden = (project?.TotalKeyCount ?? 0) - (project?.Keys.Count ?? 0);
        var referencedCount = project?.ReferencedProjectPaths.Count ?? 0;

        var hints = new List<string>();
        if (hidden > 0)
        {
            hints.Add($"Показаны только ключи корневой области. Ещё {hidden} — внутри keyscope-областей карты.");
        }

        if (referencedCount > 0)
        {
            hints.Add($"Подключено проектов-источников ключей: {referencedCount} — их ключи в списке не показаны, " +
                      "но доступны через keyref/conref, если не найдены в этом проекте.");
        }

        ScopedKeysHintVisible = hints.Count > 0;
        ScopedKeysHintText = string.Join(" ", hints);
    }

    [RelayCommand]
    private void OpenSelectedKey()
    {
        if (SelectedKey is { ResolvedPath: { } path } && File.Exists(path))
        {
            _main.OpenDocument?.Invoke(path);
        }
    }

    /// <summary>Подключает другой проект как источник ключей (мультипроектный workspace):
    /// его карты не публикуются вместе с текущим проектом, но keyref/conref на ключ, которого
    /// нет в своём проекте, теперь ищется и там. Связь сохраняется вместе с проектом.</summary>
    [RelayCommand]
    private async Task AddReferencedProject()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Проект", "Сначала откройте папку проекта.");
            return;
        }

        var folder = await _main.Files.OpenFolderAsync("Подключить проект как источник ключей");
        if (folder is null)
        {
            return;
        }

        if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(project.RootPath), StringComparison.OrdinalIgnoreCase))
        {
            await _main.Dialogs.MessageAsync("Проект", "Нельзя подключить проект сам к себе.");
            return;
        }

        project.AddReferencedProject(folder);
        RefreshKeysList();
        _main.StatusText = $"Подключён проект-источник ключей: {folder} " +
                            $"(всего подключено: {project.ReferencedProjectPaths.Count}).";
    }

    [RelayCommand]
    private void ClearReferencedProjects()
    {
        var project = _main.Project;
        if (project is null || project.ReferencedProjectPaths.Count == 0)
        {
            return;
        }

        foreach (var path in project.ReferencedProjectPaths.ToList())
        {
            project.RemoveReferencedProject(path);
        }

        RefreshKeysList();
        _main.StatusText = "Все проекты-источники ключей отключены.";
    }

    /// <summary>Подключает внешний .dtd (кастомная специализация DITA) — его элементы попадают в
    /// каталог проекта (DitaProject.Catalog), который становится активным, поэтому доступны везде:
    /// в контент-моделях, палитре вставки, валидации, публикации. Другие проекты их не видят.
    /// Связь сохраняется вместе с проектом и подхватывается заново при каждом открытии/пересканировании.</summary>
    [RelayCommand]
    private async Task AttachExternalDtd()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Внешний DTD", "Сначала откройте папку проекта.");
            return;
        }

        var file = await _main.Files.OpenFileAsync("Подключить внешний DTD", new[] { new FileFilter("Файлы DTD", "*.dtd"), FileFilter.All }, project.RootPath);
        if (file is null)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(project.RootPath, file).Replace('\\', '/');
        project.SetExternalDtdPath(relativePath);

        var (catalog, result) = project.LoadCatalog();
        DitaCatalog.Activate(catalog);
        _main.RefreshEditorContext?.Invoke();
        if (result is null)
        {
            _main.StatusText = $"Подключён внешний DTD: {relativePath}.";
            return;
        }

        var warningsNote = result.Warnings.Count > 0 ? $", предупреждений {result.Warnings.Count}" : string.Empty;
        _main.StatusText = $"Подключён внешний DTD: {relativePath}. Элементов подключено: {result.Elements.Count}{warningsNote}.";

        if (result.Warnings.Count > 0)
        {
            await _main.Dialogs.MessageAsync("Внешний DTD — предупреждения", string.Join("\n", result.Warnings));
        }
    }

    [RelayCommand]
    private void DetachExternalDtd()
    {
        var project = _main.Project;
        if (project is null || project.ExternalDtdPath is null)
        {
            return;
        }

        project.SetExternalDtdPath(null);
        DitaCatalog.Activate(project.LoadCatalog().Catalog);
        _main.RefreshEditorContext?.Invoke();
        _main.StatusText = "Внешний DTD отключён — его элементы убраны из каталога проекта.";
    }

    // ------------------------------------------------------------ дерево файлов

    public void RebuildTree()
    {
        Tree.Clear();
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        var root = new ProjectTreeNode(project.Name, project.RootPath, null);
        var folders = new Dictionary<string, ProjectTreeNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };

        foreach (var file in project.Files)
        {
            var directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            EnsureFolder(project, folders, directory).Children.Add(new ProjectTreeNode(file.FileName, null, file));
        }

        Tree.Add(root);
    }

    private static ProjectTreeNode EnsureFolder(DitaProject project, Dictionary<string, ProjectTreeNode> folders, string relativeDirectory)
    {
        if (folders.TryGetValue(relativeDirectory, out var existing))
        {
            return existing;
        }

        var parent = EnsureFolder(project, folders, Path.GetDirectoryName(relativeDirectory) ?? string.Empty);
        var node = new ProjectTreeNode(Path.GetFileName(relativeDirectory), Path.Combine(project.RootPath, relativeDirectory), null);
        parent.Children.Add(node);
        folders[relativeDirectory] = node;
        return node;
    }

    [RelayCommand]
    private void OpenSelectedFile()
    {
        if (SelectedTreeNode?.File is { } file)
        {
            _main.OpenDocument?.Invoke(file.FullPath);
        }
    }

    /// <summary>Переименовывает/переносит выбранный в дереве файл и обновляет ссылки на него по
    /// всему проекту (RefactorService.MoveFile).</summary>
    [RelayCommand]
    private async Task MoveSelectedFile()
    {
        if (SelectedTreeNode?.File is { } file)
        {
            await MoveFileAsync(file);
        }
    }

    /// <summary>Переименовывает/переносит файл с запросом нового имени и обновлением ссылок.</summary>
    public async Task MoveFileAsync(ProjectFile file)
    {
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        var newRelative = await _main.Dialogs.RenameFileAsync(file.RelativePath);
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
        var project = _main.Project;
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

        var agreed = await _main.Dialogs.ConfirmAsync("Название топика",
            $"Заголовок топика изменён.\n\nПереименовать файл «{file.RelativePath}» в «{newRelative}»? " +
            "Ссылки на него в проекте будут обновлены.");
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
        var project = _main.Project;
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
            if (!await _main.Dialogs.ConfirmAsync("Перенос файла", $"Файл {newRelative} уже существует. Заменить?"))
            {
                return;
            }

            File.Delete(newFull);
        }

        foreach (var pane in _main.Panes.Values)
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
            await _main.Dialogs.MessageAsync("Перенос файла", ex.Message);
            return;
        }

        if (_main.Panes.Remove(file.FullPath, out var movedPane))
        {
            _main.Panes[newFull] = movedPane;
            foreach (var tab in _main.Documents.Tabs.Where(t => ReferenceEquals(t.Pane, movedPane)))
            {
                tab.FullPath = newFull;
            }
        }

        _main.ApplyRefactorResult?.Invoke(result);
        project.Scan();
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        _main.StatusText = $"Файл перенесён: {file.RelativePath} → {newRelative}. Обновлено ссылок: {result.UpdatedReferences}.";
    }

    /// <summary>
    /// Удаляет файл с диска без вопросов (подтверждение — забота вызывающего): открытая вкладка
    /// закрывается без сохранения, файл уходит из проекта. null — удалён, иначе текст ошибки.
    /// </summary>
    public string? DeleteFile(ProjectFile file)
    {
        var project = _main.Project;
        if (project is null)
        {
            return "Проект не открыт.";
        }

        try
        {
            File.Delete(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        _main.Documents.DiscardTab(file.FullPath);
        project.RemoveFile(file.FullPath);
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        return null;
    }

    /// <summary>Создаёт документ по шаблону — в папке, выбранной в дереве проекта (или в папке
    /// выбранного файла), и открывает его.</summary>
    [RelayCommand]
    private async Task NewDocument()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Создание документа", "Сначала откройте папку проекта.");
            return;
        }

        var folders = new List<string> { project.RootPath };
        folders.AddRange(Directory.EnumerateDirectories(project.RootPath, "*", SearchOption.AllDirectories)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));

        var selected = SelectedTreeNode switch
        {
            { FolderPath: { } folder } when Directory.Exists(folder) => folder,
            { File: { } selectedFile } => Path.GetDirectoryName(selectedFile.FullPath),
            _ => null
        };

        var result = await _main.Dialogs.NewDocumentAsync(project.RootPath, folders, selected);
        if (result is null)
        {
            return;
        }

        var path = Path.Combine(result.Folder, result.FileName);
        if (File.Exists(path) && !await _main.Dialogs.ConfirmAsync("Создание документа", $"Файл {result.FileName} уже существует. Перезаписать?"))
        {
            return;
        }

        var document = DocumentTemplates.Create(result.Template.Key, result.Title);
        document.FilePath = path;

        try
        {
            document.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.Dialogs.MessageAsync("Создание документа", ex.Message);
            return;
        }

        project.Register(document);
        project.AddFile(path);
        project.RebuildKeySpace();
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();

        // Топик сразу попадает в карту, открытую на вкладке «Карта» (карта — нет: её место в
        // иерархии выбирают вручную).
        var isMap = DitaCatalog.Default.Get(document.Root.Name)?.IsMapType ?? false;
        var added = !isMap && _main.Map.AddCreatedTopic(path);
        _main.OpenDocument?.Invoke(path);
        if (added)
        {
            _main.StatusText = $"Создан {result.FileName} и добавлен в карту {_main.Map.SelectedMap!.RelativePath} (не забудьте сохранить карту).";
        }
    }
}
