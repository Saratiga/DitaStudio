using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Presentation.Services;

/// <summary>Режим вкладки документа.</summary>
public enum EditorMode
{
    Author,
    Source,
    Preview
}

/// <summary>
/// Вкладка открытого документа — то, что ViewModel'ям нужно от неё знать и уметь, без
/// привязки к UI-фреймворку. Реализация — контрол вкладки в оболочке (WPF <c>DocumentPane</c>,
/// Avalonia — свой). Через этот же интерфейс с документом работают плагины команд «Автора».
/// </summary>
public interface IDocumentView
{
    DitaDocument Document { get; }

    string Title { get; }

    string? FilePath { get; }

    bool IsDirty { get; }

    EditorMode Mode { get; set; }

    IAuthorSurface Author { get; }

    /// <summary>Изменился признак несохранённости.</summary>
    event EventHandler? DirtyChanged;

    /// <summary>Документ записан на диск.</summary>
    event EventHandler? Saved;

    /// <summary>Сменился текущий элемент (курсор в «Авторе» или исходном коде).</summary>
    event EventHandler? SelectionChanged;

    /// <summary>Переносит в модель несохранённый ввод (текст редактора, исходный XML).
    /// Возвращает текст ошибки разбора или null.</summary>
    string? CommitPendingEdits();

    bool Save(out string? error);

    /// <summary>Текущий XML документа (для копии восстановления) — включая недописанный исходный код.</summary>
    string SnapshotXml();

    void ReloadFromDisk();

    void MarkMissingOnDisk();

    void RestoreFromRecovery(string content);

    /// <summary>Перестраивает все представления из модели (после правки модели извне).</summary>
    void ReloadViews();

    void PerformUndo();

    void PerformRedo();

    void PushUndo(string description);

    /// <summary>Показывает и фокусирует узел в «Авторе»; false — узла там нет (например,
    /// он не отображается) — тогда вызывающий может открыть исходный код.</summary>
    bool FocusNode(DitaNode node);

    /// <summary>Переключает на исходный код и переходит к строке.</summary>
    void GoToSourceLine(int line);
}

/// <summary>Структурные операции режима «Автор» над текущим элементом.</summary>
public interface IAuthorSurface
{
    /// <summary>Элемент под курсором.</summary>
    DitaNode? CurrentNode { get; }

    bool ShowElementTags { get; set; }

    /// <summary>Перестраивает визуальное дерево из модели.</summary>
    void Rebuild();

    bool InsertElement(string name);

    /// <summary>Вставляет фразовый узел в позицию курсора внутри текстового блока.</summary>
    bool InsertInlineNode(DitaNode node);

    /// <summary>Оборачивает выделенный текст в фразовый элемент.</summary>
    bool WrapCurrentInline(string element);

    bool MoveCurrent(bool up);

    bool DeleteCurrent();

    bool MergeCurrentCellRight();

    bool MergeCurrentCellDown();

    /// <summary>Строки и столбцы таблицы под курсором; false — здесь невозможно (или оболочка не умеет).</summary>
    bool EditCurrentTable(TableOperation operation) => false;

    /// <summary>Переключает класс в outputclass текущего элемента; null — элемента нет.</summary>
    bool? ToggleCurrentOutputClass(string className);

    /// <summary>Переключает пометку rev; null — элемента нет.</summary>
    bool? ToggleCurrentRev();

    void MarkCurrentInserted();

    void MarkCurrentDeleted();

    void AcceptCurrentTrackedChange();

    void RejectCurrentTrackedChange();
}
