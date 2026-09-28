using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
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
}
