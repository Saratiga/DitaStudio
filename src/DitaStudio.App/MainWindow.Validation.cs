using System.Windows.Input;

namespace DitaStudio.App;

// Логика — в ViewModels/ValidationViewModel.cs. Здесь только пасс-through
// для MouseDoubleClick, у которого нет прямого командного биндинга в WPF.
public partial class MainWindow
{
    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel.Validation.OpenSelectedIssueCommand.Execute(null);
}
