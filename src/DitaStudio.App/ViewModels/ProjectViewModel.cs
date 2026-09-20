using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using Microsoft.Win32;

namespace DitaStudio.App.ViewModels;

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
    private void OpenProject()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с проектом DITA" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        LoadProject(dialog.FolderName);
    }

    public void LoadProject(string path)
    {
        var project = new DitaProject(path);
        _main.Project = project;
        _main.Panes.Clear();
        _main.Documents.Tabs.Clear();

        try
        {
            project.Scan();
        }
        catch (Exception ex)
        {
            Dialogs.Message("Проект", $"Не удалось прочитать папку: {ex.Message}");
            return;
        }

        _main.WindowTitle = $"DITA Studio — {project.Name}";
        _main.Conditions = new Dialogs.ConditionsResult(
            project.ExcludedConditionValues.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
            project.ShowDraftComments);
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        RecentProjects.Add(path);
        _main.RefreshRecentProjectsMenu?.Invoke();

        var dtdNote = MergeExternalDtdIfLinked(project);
        _main.StatusText = $"Проект открыт: {project.Files.Count} файлов, {project.Keys.Count} ключей.{dtdNote}";
    }

    /// <summary>Если к проекту подключён внешний DTD — разбирает его заново и вливает элементы в
    /// общий каталог (DitaCatalog.Default действует на весь сеанс редактора, см. DitaCatalog.Merge).
    /// Возвращает короткую приписку к статусной строке (пусто, если DTD не подключён).</summary>
    private static string MergeExternalDtdIfLinked(DitaProject project)
    {
        if (project.ExternalDtdPath is null)
        {
            return string.Empty;
        }

        var result = project.ResolveExternalDtd();
        if (result is null)
        {
            return string.Empty;
        }

        DitaCatalog.Default.Merge(result.Elements);
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

        project.Scan();
        _main.RefreshProjectTree?.Invoke();
        _main.RefreshMapSelector?.Invoke();
        RefreshKeysList();
        _main.StatusText = $"Проект обновлён: {project.Files.Count} файлов.";
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
    private void AddReferencedProject()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Проект", "Сначала откройте папку проекта.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Подключить проект как источник ключей" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (string.Equals(Path.GetFullPath(dialog.FolderName), Path.GetFullPath(project.RootPath), StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.Message("Проект", "Нельзя подключить проект сам к себе.");
            return;
        }

        project.AddReferencedProject(dialog.FolderName);
        RefreshKeysList();
        _main.StatusText = $"Подключён проект-источник ключей: {dialog.FolderName} " +
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

    /// <summary>Подключает внешний .dtd (кастомная специализация DITA) — элементы вливаются в
    /// общий каталог редактора (DitaCatalog.Default.Merge), поэтому доступны везде: в контент-
    /// моделях, палитре вставки, валидации, публикации. Связь сохраняется вместе с проектом и
    /// подхватывается заново при каждом открытии/пересканировании.</summary>
    [RelayCommand]
    private void AttachExternalDtd()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Внешний DTD", "Сначала откройте папку проекта.");
            return;
        }

        var dialog = new OpenFileDialog { Title = "Подключить внешний DTD", Filter = "Файлы DTD|*.dtd|Все файлы|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(project.RootPath, dialog.FileName).Replace('\\', '/');
        project.SetExternalDtdPath(relativePath);

        var result = project.ResolveExternalDtd();
        if (result is null)
        {
            _main.StatusText = $"Подключён внешний DTD: {relativePath}.";
            return;
        }

        DitaCatalog.Default.Merge(result.Elements);
        var warningsNote = result.Warnings.Count > 0 ? $", предупреждений {result.Warnings.Count}" : string.Empty;
        _main.StatusText = $"Подключён внешний DTD: {relativePath}. Элементов подключено: {result.Elements.Count}{warningsNote}.";

        if (result.Warnings.Count > 0)
        {
            Dialogs.Message("Внешний DTD — предупреждения", string.Join("\n", result.Warnings));
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
        _main.StatusText = "Внешний DTD отключён (уже влитые в каталог элементы остаются в этом сеансе редактора).";
    }
}
