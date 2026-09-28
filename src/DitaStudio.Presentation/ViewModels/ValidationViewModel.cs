using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

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

        var issues = project.ValidateAll(PluginRegistry.ValidationRules);
        ShowIssues(issues);
        _main.StatusText = $"Проверка проекта: ошибок {issues.Count(i => i.Severity == IssueSeverity.Error)}, " +
                            $"предупреждений {issues.Count(i => i.Severity == IssueSeverity.Warning)}.";
    }

    [RelayCommand]
    private async Task ValidateDocument()
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
            await _main.Dialogs.MessageAsync("Проверка", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var issues = new List<ValidationIssue>();
        issues.AddRange(new DitaValidator(project.Catalog).Validate(pane.Document));
        issues.AddRange(RefResolver.ValidateReferences(project, pane.Document));

        foreach (var plugin in PluginRegistry.ValidationRules)
        {
            try
            {
                issues.AddRange(plugin.Check(pane.Document));
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, $"Плагин \"{plugin.Name}\" упал при проверке: {ex.Message}", null, pane.FilePath));
            }
        }

        ShowIssues(issues);
        _main.StatusText = $"Проверка документа: {issues.Count} замечаний.";
    }

    [RelayCommand]
    private async Task CompareFiles()
    {
        var filters = new[] { new FileFilter("Файлы DITA", "*.dita", "*.ditamap", "*.xml"), FileFilter.All };
        var left = await _main.Files.OpenFileAsync("Сравнить — первый файл", filters);
        if (left is null)
        {
            return;
        }

        var right = await _main.Files.OpenFileAsync("Сравнить — второй файл", filters, Path.GetDirectoryName(left));
        if (right is null)
        {
            return;
        }

        // Сравнение читает файлы с диска — несохранённые правки в открытых вкладках не видны.
        await _main.Dialogs.ShowDiffAsync(left, right);
    }

    /// <summary>Сравнивает открытый документ с версией из последнего коммита git — через `git show`,
    /// без библиотеки libgit2. Требует git в PATH и файл внутри репозитория.</summary>
    [RelayCommand]
    private Task CompareWithGitHeadAsync() =>
        CompareWithHistoryAsync("git", "Сравнение с git", "git-head", path => GitHistory.ReadRevision(path),
            "Файл не найден в истории git: нет репозитория, файл не отслеживается, или git не установлен.");

    /// <summary>Сравнивает открытый документ с версией BASE из SVN — через `svn cat`, без
    /// клиентской библиотеки. Требует svn в PATH и файл под версионным контролем.</summary>
    [RelayCommand]
    private Task CompareWithSvnBaseAsync() =>
        CompareWithHistoryAsync("svn", "Сравнение с SVN", "svn-base", path => SvnHistory.ReadRevision(path),
            "Файл не найден в истории SVN: не под версионным контролем, или svn не установлен.");

    /// <summary>Общая часть сравнения с историей VCS. Клиент (git/svn) запускается в фоне: на
    /// большом репозитории или сетевом диске он отвечает секундами, окно при этом не замирает.
    /// Пока чтение идёт, команда недоступна (AsyncRelayCommand) — повторный щелчок не запустит
    /// второй процесс.</summary>
    private async Task CompareWithHistoryAsync(string client, string title, string tempPrefix,
        Func<string, string?> readRevision, string notFoundMessage)
    {
        var path = _main.Current?.FilePath;
        if (path is null)
        {
            await _main.Dialogs.MessageAsync(title, "Откройте документ.");
            return;
        }

        _main.StatusText = $"Чтение версии из {client}…";
        var content = await Task.Run(() => readRevision(path));
        if (content is null)
        {
            _main.StatusText = string.Empty;
            await _main.Dialogs.MessageAsync(title, notFoundMessage);
            return;
        }

        // Несохранённые правки в текущей вкладке diff не увидит — как и обычное «Сравнить файлы…».
        var tempPath = Path.Combine(Path.GetTempPath(), $"ditastudio-{tempPrefix}-{Path.GetFileName(path)}");
        try
        {
            File.WriteAllText(tempPath, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusText = string.Empty;
            await _main.Dialogs.MessageAsync(title, $"Не удалось записать временный файл: {ex.Message}");
            return;
        }

        _main.StatusText = string.Empty;
        await _main.Dialogs.ShowDiffAsync(tempPath, path);
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
            if (pane is not null && issue.Node is not null && !pane.FocusNode(issue.Node))
            {
                // Узла нет среди блоков «Автора» (атрибут, служебный элемент) — показываем исходник.
                pane.Mode = EditorMode.Source;
                if (issue.Line > 0)
                {
                    pane.GoToSourceLine(issue.Line);
                }
            }
        }

        _main.StatusText = issue.ToString();
    }
}
