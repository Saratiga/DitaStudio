using System.Windows;
using System.Windows.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;

namespace DitaStudio.App;

// Поиск по проекту (текст/regex/имена элементов) и замена всех вхождений.
public partial class MainWindow
{
    private void FocusSearch()
    {
        BottomTabs.SelectedIndex = 1;
        SearchBox.Focus();
    }

    private void OnFocusSearch(object sender, RoutedEventArgs e) => FocusSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
        }
    }

    private void OnSearch(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var hits = _project.Search(SearchBox.Text, false, SearchElements.IsChecked == true, SearchRegex.IsChecked == true);
        SearchResults.ItemsSource = hits;
        UpdateStatus($"Найдено совпадений: {hits.Count}");
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        if (SearchElements.IsChecked == true)
        {
            Dialogs.Message("Замена", "Замена работает только для текстового поиска, не для поиска по именам элементов.");
            return;
        }

        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var confirmed = Dialogs.Confirm("Замена",
            $"Заменить все вхождения «{SearchBox.Text}» на «{ReplaceBox.Text}» по всему проекту?\n" +
            "Изменённые документы будут помечены как несохранённые.");
        if (!confirmed)
        {
            return;
        }

        var result = _project.ReplaceAll(SearchBox.Text, ReplaceBox.Text, false, SearchRegex.IsChecked == true);
        foreach (var file in result.ChangedFiles)
        {
            if (_panes.TryGetValue(file.FullPath, out var pane))
            {
                pane.ReloadViews();
            }
        }

        UpdateTabHeaders();
        RunSearch();
        UpdateStatus($"Заменено вхождений: {result.ReplacementCount} в файлах: {result.ChangedFiles.Count}. Не забудьте сохранить (Ctrl+Shift+S).");
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResults.SelectedItem is not DitaProject.SearchHit hit)
        {
            return;
        }

        var pane = OpenDocument(hit.File.FullPath);
        var editor = pane?.Author.EditorFor(hit.Node);
        editor?.Focus();
    }
}
