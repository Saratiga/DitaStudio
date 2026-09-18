using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DitaStudio.App.Views;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Templates;

namespace DitaStudio.App;

// Вкладки документов: открытие, сохранение, закрытие, создание нового документа.
public partial class MainWindow
{
    private DocumentPane? OpenDocument(string path)
    {
        if (_project is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (_panes.TryGetValue(full, out var existing))
        {
            SelectPane(existing);
            return existing;
        }

        DitaDocument document;
        try
        {
            document = _project.GetDocument(full);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Открытие файла", $"Не удалось разобрать {Path.GetFileName(full)}:\n\n{ex.Message}");
            return null;
        }

        var pane = new DocumentPane(_project, document);
        pane.DirtyChanged += (_, _) => UpdateTabHeaders();
        pane.SelectionChanged += (_, _) => OnEditorSelectionChanged();

        var tab = new TabItem { Content = pane, Tag = full };
        tab.Header = BuildTabHeader(pane, tab);
        DocumentTabs.Items.Add(tab);
        DocumentTabs.SelectedItem = tab;
        _panes[full] = pane;

        UpdateStatus($"Открыт {Path.GetFileName(full)}");
        return pane;
    }

    private object BuildTabHeader(DocumentPane pane, TabItem tab)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = (pane.IsDirty ? "• " : string.Empty) +
                   (pane.FilePath is null ? pane.Title : Path.GetFileName(pane.FilePath)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        close.Click += (_, _) => CloseTab(tab);
        panel.Children.Add(close);
        return panel;
    }

    private void UpdateTabHeaders()
    {
        foreach (var item in DocumentTabs.Items.OfType<TabItem>())
        {
            if (item.Content is DocumentPane pane)
            {
                item.Header = BuildTabHeader(pane, item);
            }
        }
    }

    private void SelectPane(DocumentPane pane)
    {
        foreach (var item in DocumentTabs.Items.OfType<TabItem>())
        {
            if (ReferenceEquals(item.Content, pane))
            {
                DocumentTabs.SelectedItem = item;
                return;
            }
        }
    }

    private void OnDocumentTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, DocumentTabs))
        {
            return;
        }

        ViewModel.Current = Current;
        OnEditorSelectionChanged();
        BuildOutline();
    }

    private void OnCloseTab(object sender, RoutedEventArgs e) => CloseCurrentTab();

    private void CloseCurrentTab()
    {
        if (DocumentTabs.SelectedItem is TabItem tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(TabItem tab)
    {
        if (tab.Content is not DocumentPane pane)
        {
            return;
        }

        pane.CommitPendingEdits();
        if (pane.IsDirty)
        {
            var answer = MessageBox.Show(
                $"Сохранить изменения в «{pane.Title}»?",
                "DITA Studio",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.Yes && !pane.Save(out var error))
            {
                Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
                return;
            }
        }

        if (tab.Tag is string path)
        {
            _panes.Remove(path);
        }

        DocumentTabs.Items.Remove(tab);
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveCurrent();

    private bool SaveCurrent()
    {
        var pane = Current;
        if (pane is null)
        {
            return false;
        }

        if (!pane.Save(out var error))
        {
            Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            return false;
        }

        _project?.RebuildKeySpace();
        BuildKeysList();
        UpdateTabHeaders();
        UpdateStatus($"Сохранено: {Path.GetFileName(pane.FilePath ?? pane.Title)}");
        return true;
    }

    private void OnSaveAll(object sender, RoutedEventArgs e) => SaveAll();

    private void SaveAll()
    {
        var saved = 0;
        foreach (var pane in _panes.Values.ToList())
        {
            if (!pane.IsDirty)
            {
                continue;
            }

            if (pane.Save(out var error))
            {
                saved++;
            }
            else
            {
                Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            }
        }

        _project?.RebuildKeySpace();
        BuildKeysList();
        UpdateTabHeaders();
        UpdateStatus($"Сохранено файлов: {saved}");
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var dirty = _panes.Values.Count(p => p.IsDirty);
        if (dirty > 0)
        {
            var answer = MessageBox.Show(
                $"Не сохранено документов: {dirty}. Сохранить перед выходом?",
                "DITA Studio",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SaveAll();
            }
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
