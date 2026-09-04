using DitaStudio.Core.Model;

namespace DitaStudio.Core.Validation;

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed class ValidationIssue
{
    public ValidationIssue(IssueSeverity severity, string message, DitaNode? node, string? filePath = null)
    {
        Severity = severity;
        Message = message;
        Node = node;
        FilePath = filePath;
    }

    public IssueSeverity Severity { get; }

    public string Message { get; }

    public DitaNode? Node { get; }

    public string? FilePath { get; }

    public int Line => Node?.Line ?? 0;

    public string Location => Node?.Path ?? string.Empty;

    public string SeverityText => Severity switch
    {
        IssueSeverity.Error => "Ошибка",
        IssueSeverity.Warning => "Предупреждение",
        _ => "Сведения"
    };

    public override string ToString() =>
        $"{SeverityText}: {Message} ({Location}{(Line > 0 ? $", строка {Line}" : string.Empty)})";
}
