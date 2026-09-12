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

    private void BuildKeysList()
    {
        KeysList.ItemsSource = _project?.Keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
    }

    private void OnKeyDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KeysList.SelectedItem is KeyDefinition key && key.ResolvedPath is not null && File.Exists(key.ResolvedPath))
        {
            OpenDocument(key.ResolvedPath);
        }
    }
}
