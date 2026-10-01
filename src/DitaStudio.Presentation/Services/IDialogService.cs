using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Presentation.Services;

public enum AskButtons
{
    YesNo,
    YesNoCancel
}

public enum AskResult
{
    Yes,
    No,
    Cancel
}

public enum AskIcon
{
    Question,
    Warning
}

/// <summary>
/// Все диалоги редактора. Асинхронные, потому что в Avalonia модальное окно — это
/// <c>Task</c>; WPF-оболочка показывает свои модальные окна синхронно и возвращает
/// уже завершённую задачу. null / false в результате — пользователь отказался.
/// </summary>
public interface IDialogService
{
    Task MessageAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message);

    Task<AskResult> AskAsync(string title, string message, AskButtons buttons, AskIcon icon = AskIcon.Question);

    Task<T?> PickOneAsync<T>(string title, string prompt, IReadOnlyList<T> items, Func<T, string> display) where T : class;

    /// <summary>Запрашивает одну строку текста; null — отмена. Оболочки без такого окна возвращают null.</summary>
    Task<string?> PromptTextAsync(string title, string label, string initial, string hint, string okText = "ОК") => Task.FromResult<string?>(null);

    /// <summary>Окно выбора цвета: палитра и шестнадцатеричный код. Возвращает «#RRGGBB» или null (отмена). Оболочки без такого окна возвращают null.</summary>
    Task<string?> PickColorAsync(string title, string? initialHex) => Task.FromResult<string?>(null);

    Task AboutAsync();

    Task<NewDocumentResult?> NewDocumentAsync(string projectRoot, IEnumerable<string> folders, string? preselectedFolder);

    Task<TableResult?> InsertTableAsync();

    Task<XrefResult?> InsertXrefAsync(DitaProject project, string? currentFile);

    Task<List<List<RelTableCell>>?> EditRelTableAsync(DitaProject project, List<List<RelTableCell>> initialRows);

    Task<string?> RenameIdAsync(string currentId);

    Task<string?> RenameFileAsync(string currentRelativePath);

    Task<ExtractToConrefResult?> ExtractToConrefAsync(DitaProject project, string suggestedId);

    Task<ConditionsResult?> PublishConditionsAsync(DitaProject project, ConditionsResult? current);

    /// <summary>Окно списка продуктов проекта (добавить, удалить, импорт, экспорт); null — отмена. Оболочки без него возвращают null.</summary>
    Task<IReadOnlyList<ProductInfo>?> EditProductsAsync(DitaProject project) => Task.FromResult<IReadOnlyList<ProductInfo>?>(null);

    Task<bool> EditDitavalAsync(DitaProject project);

    Task<PdfHeaderFooterResult?> PdfHeaderFooterAsync(DitaProject project);

    Task CustomCssAsync(DitaProject project);

    Task<DocxLayout?> DocxLayoutSettingsAsync(DitaProject project);

    /// <summary>Размер бумаги, ориентация и поля (DOCX и PDF); без своей реализации — общий диалог оформления.</summary>
    Task<DocxLayout?> PageSetupAsync(DitaProject project) => DocxLayoutSettingsAsync(project);

    /// <summary>Окно построчного сравнения двух файлов со слиянием.</summary>
    Task ShowDiffAsync(string leftPath, string rightPath);
}
