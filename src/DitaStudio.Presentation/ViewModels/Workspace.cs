using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>
/// Открытые проекты окна и активный из них. Активный проект — тот, с которым работают поиск, проверка, публикация, ключи,
/// условия и список продуктов. Всё, что должно перерисоваться при смене проекта (заголовок, карты, ключи), подписывается
/// на <see cref="ActiveProjectChanged"/>.
/// </summary>
public sealed partial class Workspace : ObservableObject, IWorkspace
{
    [ObservableProperty]
    private DitaProject? project;

    [ObservableProperty]
    private ConditionsResult? conditions;

    public ObservableCollection<DitaProject> Projects { get; } = new();

    public event EventHandler? ActiveProjectChanged;

    public DitaProject? ProjectOf(string path)
    {
        var full = Path.GetFullPath(path);
        return Projects
            .Where(p => IsInside(p.RootPath, full))
            .OrderByDescending(p => Path.GetFullPath(p.RootPath).Length)
            .FirstOrDefault();
    }

    private static bool IsInside(string root, string full)
    {
        var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(folder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, folder, StringComparison.OrdinalIgnoreCase);
    }

    public void ActivateProject(DitaProject project)
    {
        if (ReferenceEquals(Project, project))
        {
            return;
        }

        Project = project;
        DitaCatalog.Activate(project.Catalog); // у проектов с внешним DTD каталог свой
        Conditions = new ConditionsResult(
            project.ExcludedConditionValues.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
            project.ShowDraftComments);
        ActiveProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        Project = null;
        DitaCatalog.Activate(null);
        Conditions = null;
        ActiveProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}
