using System.Windows;
using System.Windows.Controls;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;
using DitaStudio.Core.Templates;

namespace DitaStudio.App;

// Открытие/сохранение/закрытие вкладок теперь в ViewModels/DocumentsViewModel.cs
// и ViewModels/TabViewModel.cs. Здесь остались только пасс-through к VM (нужны
// не мигрированным Map/Project/SidePanels, которые зовут эти имена как соседний
// метод MainWindow) и NewDocument — завязан на дерево проекта, не мигрирован.
public partial class MainWindow
{
    private DocumentPane? OpenDocument(string path) => ViewModel.Documents.OpenDocument(path);

    private void UpdateTabHeaders() => ViewModel.Documents.RefreshAllTabTitles();

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ViewModel.Documents.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void OnNewDocument(object sender, RoutedEventArgs e) => NewDocument();

    private void NewDocument()
    {
        if (_project is null)
        {
            Dialogs.Message("Создание документа", "Сначала откройте папку проекта.");
            return;
        }

        var folders = new List<string> { _project.RootPath };
        folders.AddRange(Directory.EnumerateDirectories(_project.RootPath, "*", SearchOption.AllDirectories)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));

        var selected = ProjectTree.SelectedItem is TreeViewItem { Tag: string folder } && Directory.Exists(folder)
            ? folder
            : ProjectTree.SelectedItem is TreeViewItem { Tag: ProjectFile file }
                ? Path.GetDirectoryName(file.FullPath)
                : null;

        var result = Dialogs.NewDocument(_project.RootPath, folders, selected);
        if (result is null)
        {
            return;
        }

        var path = Path.Combine(result.Folder, result.FileName);
        if (File.Exists(path) && !Dialogs.Confirm("Создание документа", $"Файл {result.FileName} уже существует. Перезаписать?"))
        {
            return;
        }

        var document = DocumentTemplates.Create(result.Template.Key, result.Title);
        document.FilePath = path;

        try
        {
            document.Save(path);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Создание документа", ex.Message);
            return;
        }

        _project.Register(document);
        _project.AddFile(path);
        _project.RebuildKeySpace();
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        OpenDocument(path);
    }
}
