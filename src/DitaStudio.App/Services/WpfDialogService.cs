using System.Windows;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Services;

namespace DitaStudio.App.Services;

/// <summary>Диалоги WPF-оболочки для общих ViewModel'ей: окна модальные и синхронные, поэтому
/// каждая задача возвращается уже завершённой.</summary>
public sealed class WpfDialogService : IDialogService
{
    public Task MessageAsync(string title, string message)
    {
        Dialogs.Message(title, message);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(Dialogs.Confirm(title, message));

    public Task<AskResult> AskAsync(string title, string message, AskButtons buttons, AskIcon icon = AskIcon.Question)
    {
        var answer = MessageBox.Show(message, title,
            buttons == AskButtons.YesNo ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel,
            icon == AskIcon.Warning ? MessageBoxImage.Warning : MessageBoxImage.Question);

        return Task.FromResult(answer switch
        {
            MessageBoxResult.Yes => AskResult.Yes,
            MessageBoxResult.No => AskResult.No,
            _ => AskResult.Cancel
        });
    }

    public Task<T?> PickOneAsync<T>(string title, string prompt, IReadOnlyList<T> items, Func<T, string> display) where T : class =>
        Task.FromResult(Dialogs.PickOne(title, prompt, items, display));

    public Task AboutAsync()
    {
        Dialogs.About();
        return Task.CompletedTask;
    }

    public Task<NewDocumentResult?> NewDocumentAsync(string projectRoot, IEnumerable<string> folders, string? preselectedFolder) =>
        Task.FromResult(Dialogs.NewDocument(projectRoot, folders, preselectedFolder));

    public Task<TableResult?> InsertTableAsync() => Task.FromResult(Dialogs.InsertTable());

    public Task<XrefResult?> InsertXrefAsync(DitaProject project, string? currentFile) =>
        Task.FromResult(Dialogs.InsertXref(project, currentFile));

    public Task<List<List<RelTableCell>>?> EditRelTableAsync(DitaProject project, List<List<RelTableCell>> initialRows) =>
        Task.FromResult(Dialogs.EditRelTable(project, initialRows));

    public Task<string?> RenameIdAsync(string currentId) => Task.FromResult(Dialogs.RenameId(currentId));

    public Task<string?> RenameFileAsync(string currentRelativePath) => Task.FromResult(Dialogs.RenameFile(currentRelativePath));

    public Task<ExtractToConrefResult?> ExtractToConrefAsync(DitaProject project, string suggestedId) =>
        Task.FromResult(Dialogs.ExtractToConref(project, suggestedId));

    public Task<ConditionsResult?> PublishConditionsAsync(DitaProject project, ConditionsResult? current) =>
        Task.FromResult(Dialogs.PublishConditions(project, current));

    public Task<bool> EditDitavalAsync(DitaProject project) => Task.FromResult(Dialogs.EditDitaval(project));

    public Task<PdfHeaderFooterResult?> PdfHeaderFooterAsync(DitaProject project) =>
        Task.FromResult(Dialogs.PdfHeaderFooter(project));

    public Task CustomCssAsync(DitaProject project)
    {
        Dialogs.CustomCss(project);
        return Task.CompletedTask;
    }

    public Task<DocxLayout?> DocxLayoutSettingsAsync(DitaProject project) =>
        Task.FromResult(Dialogs.DocxLayoutSettings(project));

    public Task ShowDiffAsync(string leftPath, string rightPath)
    {
        DiffWindow.Show(leftPath, rightPath);
        return Task.CompletedTask;
    }
}
