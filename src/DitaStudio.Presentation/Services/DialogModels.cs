using DitaStudio.Core.Project;
using DitaStudio.Core.Templates;

namespace DitaStudio.Presentation.Services;

// Результаты диалогов — общие для обеих оболочек (раньше вложенные типы Dialogs в WPF-версии).

public sealed record NewDocumentResult(DocumentTemplate Template, string Title, string FileName, string Folder);

public sealed record TableResult(int Rows, int Columns, bool Header, string Title);

public sealed record XrefResult(ProjectFile File, string? TopicId, string Text);

/// <summary>Одна ячейка таблицы соответствий (reltable) — не более одного topicref на
/// ячейку; DITA допускает несколько, но для редактора этого достаточно (частый случай на
/// практике). Пустая ячейка (File == null) — допустимо, relcell может быть пустой.</summary>
public sealed class RelTableCell
{
    public ProjectFile? File { get; set; }

    public string? TopicId { get; set; }
}

public sealed class ExtractToConrefResult
{
    public ProjectFile? TargetFile { get; set; }

    public string? NewFileName { get; set; }

    public string ElementId { get; set; } = string.Empty;
}

public sealed record ConditionsResult(Dictionary<string, HashSet<string>> Exclude, bool ShowDraftComments);

public sealed record PdfHeaderFooterResult(bool Show, string HeaderText, string FooterText);
