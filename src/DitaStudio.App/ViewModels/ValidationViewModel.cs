using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;

namespace DitaStudio.App.ViewModels;

// Проверка проекта/документа, список найденных замечаний, сравнение файлов.
public partial class ValidationViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private ValidationIssue? selectedIssue;

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    public ValidationViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void ValidateProject()
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

        var issues = project.ValidateAll();
        ShowIssues(issues);
        _main.StatusText = $"Проверка проекта: ошибок {issues.Count(i => i.Severity == IssueSeverity.Error)}, " +
                            $"предупреждений {issues.Count(i => i.Severity == IssueSeverity.Warning)}.";
    }

    [RelayCommand]
    private void ValidateDocument()
    {
        var pane = _main.Current;
        var project = _main.Project;
        if (pane is null || project is null)
        {
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            Dialogs.Message("Проверка", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var issues = new List<ValidationIssue>();
        issues.AddRange(new DitaValidator().Validate(pane.Document));
        issues.AddRange(RefResolver.ValidateReferences(project, pane.Document));
        ShowIssues(issues);
        _main.StatusText = $"Проверка документа: {issues.Count} замечаний.";
    }

    [RelayCommand]
    private void CompareFiles()
    {
        var filter = "Файлы DITA (*.dita;*.ditamap;*.xml)|*.dita;*.ditamap;*.xml|Все файлы (*.*)|*.*";
        var left = new Microsoft.Win32.OpenFileDialog { Title = "Сравнить — первый файл", Filter = filter };
        if (left.ShowDialog() != true)
        {
            return;
        }

        var right = new Microsoft.Win32.OpenFileDialog { Title = "Сравнить — второй файл", Filter = filter };
        if (right.ShowDialog() != true)
        {
            return;
        }

        // Сравнение читает файлы с диска — несохранённые правки в открытых вкладках не видны.
        DiffWindow.Show(left.FileName, right.FileName);
    }

    /// <summary>Сравнивает открытый документ с версией из последнего коммита git — через `git show`,
    /// без библиотеки libgit2. Требует git в PATH и файл внутри репозитория.</summary>
    [RelayCommand]
    private void CompareWithGitHead()
    {
        var path = _main.Current?.FilePath;
        if (path is null)
        {
            Dialogs.Message("Сравнение с git", "Откройте документ.");
            return;
        }

        var headContent = GitHistory.ReadRevision(path);
        if (headContent is null)
        {
            Dialogs.Message("Сравнение с git",
                "Файл не найден в истории git: нет репозитория, файл не отслеживается, или git не установлен.");
            return;
        }

        // Несохранённые правки в текущей вкладке diff не увидит — как и обычное «Сравнить файлы…».
        var tempPath = Path.Combine(Path.GetTempPath(), $"ditastudio-git-head-{Path.GetFileName(path)}");
        File.WriteAllText(tempPath, headContent);

        DiffWindow.Show(tempPath, path);
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues)
    {
        Issues.Clear();
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
        {
            Issues.Add(issue);
        }

        _main.BottomTabIndex = 0;
    }

    [RelayCommand]
    private void OpenSelectedIssue()
    {
        if (SelectedIssue is not { } issue)
        {
            return;
        }

        if (issue.FilePath is not null && File.Exists(issue.FilePath))
        {
            var pane = _main.OpenDocument?.Invoke(issue.FilePath);
            if (pane is not null && issue.Node is not null)
            {
                var editor = pane.Author.EditorFor(issue.Node);
                if (editor is not null)
                {
                    editor.Focus();
                }
                else
                {
                    pane.Mode = EditorMode.Source;
                    if (issue.Line > 0)
                    {
                        pane.Source.GoToLine(issue.Line);
                    }
                }
            }
        }

        _main.StatusText = issue.ToString();
    }
}
