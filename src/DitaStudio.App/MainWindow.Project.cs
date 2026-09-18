using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.App.Views;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using Microsoft.Win32;

namespace DitaStudio.App;

// Открытие/сканирование папки проекта, дерево файлов, список ключей.
public partial class MainWindow
{
    private void OnOpenProject(object sender, RoutedEventArgs e) => OpenProject();

    private void OpenProject()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку с проектом DITA" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadProject(dialog.FolderName);
    }

    private void LoadProject(string path)
    {
        _project = new DitaProject(path);
        ViewModel.Project = _project;
        _panes.Clear();
        DocumentTabs.Items.Clear();

        try
        {
            _project.Scan();
        }
        catch (Exception ex)
        {
            Dialogs.Message("Проект", $"Не удалось прочитать папку: {ex.Message}");
            return;
        }

        Title = $"DITA Studio — {_project.Name}";
        _conditions = new Dialogs.ConditionsResult(
            _project.ExcludedConditionValues.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
            _project.ShowDraftComments);
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        RecentProjects.Add(path);
        RefreshRecentProjectsMenu();
        UpdateStatus($"Проект открыт: {_project.Files.Count} файлов, {_project.Keys.Count} ключей.");
    }

    private void OnRescanProject(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        _project.Scan();
        BuildProjectTree();
        BuildMapSelector();
        BuildKeysList();
        UpdateStatus($"Проект обновлён: {_project.Files.Count} файлов.");
    }

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
            foreach (var tab in DocumentTabs.Items.OfType<TabItem>())
            {
                if (ReferenceEquals(tab.Content, movedPane))
                {
                    tab.Tag = newFull;
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

    private void BuildKeysList()
    {
        KeysList.ItemsSource = _project?.Keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();

        var hidden = (_project?.TotalKeyCount ?? 0) - (_project?.Keys.Count ?? 0);
        ScopedKeysHint.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScopedKeysHint.Text = hidden > 0
            ? $"Показаны только ключи корневой области. Ещё {hidden} — внутри keyscope-областей карты."
            : string.Empty;
    }

    private void OnKeyDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KeysList.SelectedItem is KeyDefinition key && key.ResolvedPath is not null && File.Exists(key.ResolvedPath))
        {
            OpenDocument(key.ResolvedPath);
        }
    }
}
