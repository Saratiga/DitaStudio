using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;

namespace DitaStudio.App.ViewModels;

// Поиск по проекту (текст/regex/имена элементов) и замена всех вхождений.
public partial class SearchViewModel : ObservableObject
{
    private readonly MainViewModel _main;

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

    public SearchViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void Run()
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

        var hits = project.Search(SearchText, false, SearchElements, SearchRegex);
        Results.Clear();
        foreach (var hit in hits)
        {
            Results.Add(hit);
        }

        _main.StatusText = $"Найдено совпадений: {hits.Count}";
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        if (SearchElements)
        {
            Dialogs.Message("Замена", "Замена работает только для текстового поиска, не для поиска по именам элементов.");
            return;
        }

        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        foreach (var pane in _main.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var confirmed = Dialogs.Confirm("Замена",
            $"Заменить все вхождения «{SearchText}» на «{ReplaceText}» по всему проекту?\n" +
            "Изменённые документы будут помечены как несохранённые.");
        if (!confirmed)
        {
            return;
        }

        var result = project.ReplaceAll(SearchText, ReplaceText, false, SearchRegex);
        foreach (var file in result.ChangedFiles)
        {
            if (_main.Panes.TryGetValue(file.FullPath, out var pane))
            {
                pane.ReloadViews();
            }
        }

        _main.UpdateTabHeaders?.Invoke();
        Run();
        _main.StatusText = $"Заменено вхождений: {result.ReplacementCount} в файлах: {result.ChangedFiles.Count}. Не забудьте сохранить (Ctrl+Shift+S).";
    }

    [RelayCommand]
    private void OpenSelectedResult()
    {
        if (SelectedResult is not { } hit)
        {
            return;
        }

        var pane = _main.OpenDocument?.Invoke(hit.File.FullPath);
        var editor = pane?.Author.EditorFor(hit.Node);
        editor?.Focus();
    }
}
