using System.Windows;
using System.Windows.Input;
using DitaStudio.App.Authoring;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;

namespace DitaStudio.App;

// Проверка проекта/документа и список найденных замечаний.
public partial class MainWindow
{
    private void OnValidateProject(object sender, RoutedEventArgs e) => ValidateProject();

    private void ValidateProject()
    {
        if (_project is null)
        {
            return;
        }

        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var issues = _project.ValidateAll();
        ShowIssues(issues);
        UpdateStatus($"Проверка проекта: ошибок {issues.Count(i => i.Severity == IssueSeverity.Error)}, " +
                     $"предупреждений {issues.Count(i => i.Severity == IssueSeverity.Warning)}.");
    }

    private void OnValidateDocument(object sender, RoutedEventArgs e)
    {
        var pane = Current;
        if (pane is null || _project is null)
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
        issues.AddRange(RefResolver.ValidateReferences(_project, pane.Document));
        ShowIssues(issues);
        UpdateStatus($"Проверка документа: {issues.Count} замечаний.");
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues)
    {
        IssuesList.ItemsSource = issues
            .OrderByDescending(i => i.Severity)
            .ToList();
        BottomTabs.SelectedIndex = 0;
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssuesList.SelectedItem is not ValidationIssue issue)
        {
            return;
        }

        if (issue.FilePath is not null && File.Exists(issue.FilePath))
        {
            var pane = OpenDocument(issue.FilePath);
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

        UpdateStatus(issue.ToString());
    }
}
