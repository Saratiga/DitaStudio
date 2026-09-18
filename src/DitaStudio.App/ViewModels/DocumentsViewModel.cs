using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Model;

namespace DitaStudio.App.ViewModels;

// Вкладки документов: открытие, сохранение, закрытие. NewDocument оставлен
// в MainWindow.Documents.cs — завязан на дерево проекта (ещё не мигрировано).
public partial class DocumentsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? selectedTab;

    public DocumentsViewModel(MainViewModel main)
    {
        _main = main;
    }

    partial void OnSelectedTabChanged(TabViewModel? value) => _main.RefreshEditorContext?.Invoke();

    public DocumentPane? OpenDocument(string path)
    {
        var project = _main.Project;
        if (project is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (_main.Panes.TryGetValue(full, out var existing))
        {
            SelectedTab = Tabs.FirstOrDefault(t => ReferenceEquals(t.Pane, existing));
            return existing;
        }

        DitaDocument document;
        try
        {
            document = project.GetDocument(full);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Открытие файла", $"Не удалось разобрать {Path.GetFileName(full)}:\n\n{ex.Message}");
            return null;
        }

        var pane = new DocumentPane(project, document);
        var tab = new TabViewModel(this, pane, full);
        pane.DirtyChanged += (_, _) => tab.RefreshTitle();
        pane.SelectionChanged += (_, _) => _main.RefreshEditorContext?.Invoke();

        Tabs.Add(tab);
        SelectedTab = tab;
        _main.Panes[full] = pane;

        _main.StatusText = $"Открыт {Path.GetFileName(full)}";
        return pane;
    }

    public void RefreshAllTabTitles()
    {
        foreach (var tab in Tabs)
        {
            tab.RefreshTitle();
        }
    }

    public void CloseTab(TabViewModel tab)
    {
        var pane = tab.Pane;
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

        _main.Panes.Remove(tab.FullPath);
        Tabs.Remove(tab);
    }

    [RelayCommand]
    private void CloseCurrentTab()
    {
        if (SelectedTab is { } tab)
        {
            CloseTab(tab);
        }
    }

    [RelayCommand]
    private void SaveCurrent()
    {
        var tab = SelectedTab;
        var pane = tab?.Pane;
        if (pane is null)
        {
            return;
        }

        if (!pane.Save(out var error))
        {
            Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            return;
        }

        _main.Project?.RebuildKeySpace();
        _main.RefreshProjectKeys?.Invoke();
        tab!.RefreshTitle();
        _main.StatusText = $"Сохранено: {Path.GetFileName(pane.FilePath ?? pane.Title)}";
    }

    [RelayCommand]
    private void SaveAll()
    {
        var saved = 0;
        foreach (var tab in Tabs.ToList())
        {
            if (!tab.Pane.IsDirty)
            {
                continue;
            }

            if (tab.Pane.Save(out var error))
            {
                saved++;
                tab.RefreshTitle();
            }
            else
            {
                Dialogs.Message("Сохранение", error ?? "Не удалось сохранить файл.");
            }
        }

        _main.Project?.RebuildKeySpace();
        _main.RefreshProjectKeys?.Invoke();
        _main.StatusText = $"Сохранено файлов: {saved}";
    }

    // Вызывается из MainWindow.OnClosing — там же живой Window.OnClosing,
    // которого у VM быть не может.
    public bool ConfirmClose()
    {
        foreach (var tab in Tabs)
        {
            tab.Pane.CommitPendingEdits();
        }

        var dirty = Tabs.Count(t => t.Pane.IsDirty);
        if (dirty == 0)
        {
            return true;
        }

        var answer = MessageBox.Show(
            $"Не сохранено документов: {dirty}. Сохранить перед выходом?",
            "DITA Studio",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (answer == MessageBoxResult.Yes)
        {
            SaveAll();
        }

        return true;
    }
}
