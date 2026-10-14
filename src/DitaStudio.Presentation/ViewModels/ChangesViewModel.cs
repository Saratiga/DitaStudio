using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Project;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>Строка панели «Правки»: правка и то, что о ней показывается.</summary>
public sealed class ChangeItem
{
    public ChangeItem(ProjectChange change)
    {
        Change = change;
    }

    public ProjectChange Change { get; }

    public string File => Change.File.RelativePath;

    public string Status => Change.Status;

    /// <summary>Подпись статуса на языке интерфейса: «новое», «изменено», «удалено».</summary>
    public string StatusLabel => Change.Status switch
    {
        "new" => Loc.T("Changes_StatusNew"),
        "deleted" => Loc.T("Changes_StatusDeleted"),
        _ => Loc.T("Changes_StatusChanged")
    };

    public string Element => "<" + Change.Element + ">";

    public bool IsNew => Change.Status == "new";

    public bool IsChanged => Change.Status == "changed";

    public bool IsDeleted => Change.Status == "deleted";

    public string Text => Change.Text.Length == 0 ? Loc.T("Changes_NoText") : Change.Text;
}

/// <summary>
/// Панель «Правки»: блоки с пометкой <c>status</c> (new / changed / deleted) по всему активному проекту. Щелчок открывает топик и ставит курсор в
/// блок. Список собирается заново кнопкой «Обновить», при открытии вкладки и после сохранения документа.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly IDocumentHost _docs;
    private List<ChangeItem> _all = new();

    public ChangesViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _docs = docs;
        _docs.DocumentSettled += _ => Refresh();
        _docs.ProjectSettled += _ => Refresh();
    }

    public ObservableCollection<ChangeItem> Items { get; } = new();

    /// <summary>Варианты фильтра: все, новые, изменённые, удалённые.</summary>
    public IReadOnlyList<string> Filters { get; } = new[] { Loc.T("Changes_FilterAll"), Loc.T("Changes_StatusNew"), Loc.T("Changes_StatusChanged"), Loc.T("Changes_StatusDeleted") };

    [ObservableProperty]
    private int filterIndex;

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private ChangeItem? selectedItem;

    partial void OnFilterIndexChanged(int value) => ApplyFilter();

    partial void OnSelectedItemChanged(ChangeItem? value)
    {
        if (value is not null)
        {
            Open(value);
        }
    }

    /// <summary>Собирает правки активного проекта заново.</summary>
    [RelayCommand]
    public void Refresh()
    {
        if (_workspace.Project is not { } project)
        {
            _all = new List<ChangeItem>();
            ApplyFilter();
            return;
        }

        // Несохранённые правки открытых документов тоже считаются: они уже в модели, которую читает поиск.
        foreach (var pane in _docs.Panes.Values.ToList())
        {
            pane.CommitPendingEdits();
        }

        _all = ProjectChanges.Find(project).Select(c => new ChangeItem(c)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var status = FilterIndex switch { 1 => "new", 2 => "changed", 3 => "deleted", _ => null };
        Items.Clear();
        foreach (var item in _all.Where(i => status is null || i.Status == status))
        {
            Items.Add(item);
        }

        Summary = _all.Count == 0
            ? Loc.T("Changes_NoChanges")
            : Loc.T("Changes_Summary0123", _all.Count, _all.Count(i => i.Status == "new"), _all.Count(i => i.Status == "changed"), _all.Count(i => i.Status == "deleted"));
    }

    /// <summary>Открывает топик правки и ставит курсор в её блок.</summary>
    public void Open(ChangeItem item)
    {
        if (_docs.OpenDocument(item.Change.File.FullPath) is not { } pane)
        {
            return;
        }

        if (ProjectChanges.Resolve(pane.Document, item.Change.Path) is { } node && node.GetAttribute("status") == item.Status)
        {
            pane.FocusNode(node);
            _shell.StatusText = Loc.T("Changes_Opened01", item.Change.File.RelativePath, item.StatusLabel);
        }
        else
        {
            // Документ изменился после сбора списка — пересобираем, чтобы строки снова вели к блокам.
            _shell.StatusText = Loc.T("Changes_ListIsOutdated");
            Refresh();
        }
    }
}
