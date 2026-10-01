using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

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

    /// <summary>Просьба открыть файл (двойной щелчок по топику в списке карты). Оболочки без такого списка событие не поднимают.</summary>
    event EventHandler<string>? OpenFileRequested
    {
        add { }
        remove { }
    }

    /// <summary>Фокус ушёл из изменённого заголовка топика: имя файла можно привести в соответствие с ним.
    /// Оболочки без такого редактирования событие не поднимают.</summary>
    event EventHandler? RootTitleCommitted
    {
        add { }
        remove { }
    }

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

    /// <summary>Вставляет рисунок (блок <c>fig</c> с названием и этим изображением) после текущего блока.</summary>
    bool InsertFigure(DitaNode image) => false;

    /// <summary>Выносит изображение из абзаца под курсором в рисунок <c>fig</c> с названием.</summary>
    bool WrapImageAsFigure() => false;

    /// <summary>
    /// Подпись рисунка или таблицы под курсором: есть <c>title</c> — убирает (подписи не будет), нет — добавляет пустой
    /// (подпись «Рисунок N» / «Таблица N»). true — добавлена, false — убрана, null — здесь нет ни рисунка, ни таблицы.
    /// </summary>
    bool? ToggleCaption() => null;

    /// <summary>Границы таблицы CALS под курсором одним выбором (все, внешняя рамка, горизонтальные, без границ). false — курсор не в таблице.</summary>
    bool SetTableBorders(TableBorderMode mode) => false;

    /// <summary>Линия под строкой таблицы, в которой стоит курсор: показать или убрать. false — курсор не в строке таблицы.</summary>
    bool SetRowBorder(bool visible) => false;

    /// <summary>Линия справа от столбца, в котором стоит курсор: показать или убрать. false — курсор не в ячейке таблицы.</summary>
    bool SetColumnBorder(bool visible) => false;

    /// <summary>Выделяет целиком таблицу, в которой стоит курсор (контур; Delete удаляет). false — курсор не в таблице.</summary>
    bool SelectCurrentTable() => false;

    /// <summary>Удаляет таблицу, в которой стоит курсор, целиком. false — курсор не в таблице.</summary>
    bool DeleteCurrentTable() => false;

    /// <summary>Оборачивает выделенный текст в фразовый элемент.</summary>
    bool WrapCurrentInline(string element);

    bool MoveCurrent(bool up);

    bool DeleteCurrent();

    bool MergeCurrentCellRight();

    bool MergeCurrentCellDown();

    /// <summary>Оформление выделенного текста (или текста, который будет набран у курсора).</summary>
    bool ApplyTextFormat(string prefix, string? token) => false;

    /// <summary>Есть ли выделенный текст (в «Авторе» — внутри одного блока).</summary>
    bool HasTextSelection => false;

    /// <summary>Включён ли режим «кисти» маркера: выделение текста мышью сразу закрашивается выбранным цветом (как в Word).</summary>
    bool MarkerPenActive => false;

    /// <summary>Цвет кисти маркера — класс <c>mark-…</c>; null при включённой кисти — «ластик» (выделение снимает маркер).</summary>
    string? MarkerPenToken => null;

    /// <summary>Включает кисть маркера выбранным цветом (<paramref name="token"/> = null — ластик). false — оболочка так не умеет.</summary>
    bool StartMarkerPen(string? token) => false;

    /// <summary>Выключает кисть маркера.</summary>
    void StopMarkerPen() { }

    /// <summary>Оформление текущего блока — класс группы вместо прежнего (null — снять).</summary>
    bool SetCurrentBlockFormat(string prefix, string? token) => false;

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
