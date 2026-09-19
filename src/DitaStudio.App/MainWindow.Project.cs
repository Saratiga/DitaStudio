using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.App.Views;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.App;

// Дерево файлов проекта (императивное построение WPF-дерева) и перенос/
// переименование файла. Открытие/сканирование проекта и список ключей — в
// ViewModels/ProjectViewModel.cs.
public partial class MainWindow
{
    // Открытие/сканирование проекта и список ключей — в
    // ViewModels/ProjectViewModel.cs. Эти два — тонкие пасс-through, нужны
    // не мигрированным местам (RefreshRecentProjectsMenu, Documents.NewDocument),
    // которые зовут их как соседний метод MainWindow.
    private void LoadProject(string path) => ViewModel.ProjectPanel.LoadProject(path);

    private void BuildKeysList() => ViewModel.ProjectPanel.RefreshKeysList();

    private void BuildProjectTree()
    {
        ProjectTree.Items.Clear();
        if (_project is null)
        {
            return;
        }

        var rootItem = new TreeViewItem { Header = _project.Name, IsExpanded = true, Tag = _project.RootPath };
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = rootItem
        };

        foreach (var file in _project.Files)
        {
            var directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            var parent = EnsureFolder(folders, rootItem, directory);
            parent.Items.Add(new TreeViewItem
            {
                Header = BuildFileHeader(file),
                Tag = file,
                ToolTip = file.RelativePath
            });
        }

        ProjectTree.Items.Add(rootItem);
    }

    private TreeViewItem EnsureFolder(Dictionary<string, TreeViewItem> folders, TreeViewItem root, string relativeDirectory)
    {
        if (string.IsNullOrEmpty(relativeDirectory))
        {
            return root;
        }

        if (folders.TryGetValue(relativeDirectory, out var existing))
        {
            return existing;
        }

        var parentPath = Path.GetDirectoryName(relativeDirectory) ?? string.Empty;
        var parent = EnsureFolder(folders, root, parentPath);

        var item = new TreeViewItem
        {
            Header = Path.GetFileName(relativeDirectory),
            IsExpanded = true,
            Tag = _project is null ? relativeDirectory : Path.Combine(_project.RootPath, relativeDirectory)
        };

        parent.Items.Add(item);
        folders[relativeDirectory] = item;
        return item;
    }

    private static object BuildFileHeader(ProjectFile file)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = file.Kind switch
            {
                DitaDocumentKind.Map => "🗺",
                DitaDocumentKind.Topic => "📄",
                _ => "•"
            },
            Margin = new Thickness(0, 0, 6, 0)
        });
        panel.Children.Add(new TextBlock { Text = file.FileName });
        panel.Children.Add(new TextBlock
        {
            Text = "  " + file.Title,
            Foreground = ThemeManager.Brush("TagBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150
        });
        return panel;
    }

    private void OnProjectTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is TreeViewItem { Tag: ProjectFile file })
        {
            OpenDocument(file.FullPath);
        }
    }

    private void OnProjectTreeRightClick(object sender, MouseButtonEventArgs e)
    {
        // TreeView сам по себе не выделяет узел по правому клику — делаем это вручную,
        // чтобы контекстное меню относилось к узлу под курсором, а не к прошлому выделению.
        if (FindTreeViewItem(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void OnProjectFileMove(object sender, RoutedEventArgs e)
    {
        if (_project is null || ProjectTree.SelectedItem is not TreeViewItem { Tag: ProjectFile file })
        {
            return;
        }

        var newRelative = Dialogs.RenameFile(file.RelativePath);
        if (string.IsNullOrWhiteSpace(newRelative))
        {
            return;
        }

        var newFull = Path.GetFullPath(Path.Combine(_project.RootPath, newRelative));
        if (string.Equals(newFull, file.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(newFull))
        {
            if (!Dialogs.Confirm("Перенос файла", $"Файл {newRelative} уже существует. Заменить?"))
            {
                return;
            }

            File.Delete(newFull);
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        RefactorResult result;
        try
        {
            result = RefactorService.MoveFile(_project, file.FullPath, newFull);
        }
        catch (IOException ex)
        {
            Dialogs.Message("Перенос файла", ex.Message);
            return;
        }

        if (_panes.TryGetValue(file.FullPath, out var movedPane))
        {
            _panes.Remove(file.FullPath);
            _panes[newFull] = movedPane;
            foreach (var tab in ViewModel.Documents.Tabs)
            {
                if (ReferenceEquals(tab.Pane, movedPane))
                {
                    tab.FullPath = newFull;
                }
            }
        }

        ApplyRefactorResult(result);
        _project.Scan();
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        UpdateStatus($"Файл перенесён: {file.RelativePath} → {newRelative}. Обновлено ссылок: {result.UpdatedReferences}.");
    }

    private void OnKeyDoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel.ProjectPanel.OpenSelectedKeyCommand.Execute(null);
}
