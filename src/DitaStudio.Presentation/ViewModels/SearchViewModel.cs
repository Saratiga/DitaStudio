using DitaStudio.Presentation.Services;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Project;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Поиск по проекту (текст/regex/имена элементов) и замена всех вхождений.
public partial class SearchViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly IDocumentHost _docs;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string replaceText = string.Empty;

    [ObservableProperty]
    private bool searchElements;

    [ObservableProperty]
    private bool searchRegex;

    [ObservableProperty]
    private DitaProject.SearchHit? selectedResult;

    public ObservableCollection<DitaProject.SearchHit> Results { get; } = new();

    public SearchViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _docs = docs;
    }

    /// <summary>Показывает готовый список (например, ссылки на файл) вместо результатов поиска.</summary>
    public void ShowResults(IEnumerable<DitaProject.SearchHit> hits)
    {
        Results.Clear();
        foreach (var hit in hits)
        {
            Results.Add(hit);
        }
    }

    [RelayCommand]
    private void Run()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            return;
        }

        foreach (var pane in _docs.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var hits = project.Search(SearchText, false, SearchElements, SearchRegex);
        Results.Clear();
        foreach (var hit in hits)
        {
            Results.Add(hit);
        }

        _shell.StatusText = Loc.T("Msg_MatchesFound0", hits.Count);
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            return;
        }

        if (SearchElements)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_Replace"), Loc.T("Msg_ReplaceWorksOnlyForTextSearch"));
            return;
        }

        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        foreach (var pane in _docs.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var confirmed = await _ui.Dialogs.ConfirmAsync(Loc.T("Msg_Replace"),
            Loc.T("Msg_ReplaceAllOccurrencesOf0With", SearchText, ReplaceText) +
            Loc.T("Msg_TheChangedDocumentsWillBeMarked"));
        if (!confirmed)
        {
            return;
        }

        var result = project.ReplaceAll(SearchText, ReplaceText, false, SearchRegex);
        foreach (var file in result.ChangedFiles)
        {
            if (_docs.Panes.TryGetValue(file.FullPath, out var pane))
            {
                pane.ReloadViews();
            }
        }

        _docs.RefreshAllTabTitles();
        Run();
        _shell.StatusText = Loc.T("Msg_OccurrencesReplaced0InFiles1", result.ReplacementCount, result.ChangedFiles.Count);
    }

    [RelayCommand]
    private void OpenSelectedResult()
    {
        if (SelectedResult is not { } hit)
        {
            return;
        }

        var pane = _docs.OpenDocument(hit.File.FullPath);
        pane?.FocusNode(hit.Node);
    }
}
