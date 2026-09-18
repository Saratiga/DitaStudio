using System.Windows;
using System.Windows.Input;

namespace DitaStudio.App;

// Фокус на поле поиска. Логика поиска/замены — в ViewModels/SearchViewModel.cs.
public partial class MainWindow
{
    private void FocusSearch()
    {
        BottomTabs.SelectedIndex = 1;
        SearchBox.Focus();
    }

    private void OnFocusSearch(object sender, RoutedEventArgs e) => FocusSearch();

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel.Search.OpenSelectedResultCommand.Execute(null);
}
