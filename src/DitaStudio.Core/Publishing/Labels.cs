namespace DitaStudio.Core.Publishing;

/// <summary>Подписи, которые генератор добавляет в публикацию (по языку топика).</summary>
public sealed class Labels
{
    public static readonly Labels Russian = new()
    {
        Contents = "Содержание",
        Prerequisites = "Перед началом",
        Context = "Контекст",
        Steps = "Порядок действий",
        Result = "Результат",
        Example = "Пример",
        PostRequisites = "Дальнейшие действия",
        TaskTroubleshooting = "Если что-то пошло не так",
        Condition = "Признак",
        Cause = "Причина",
        Remedy = "Решение",
        RelatedLinks = "Связанные материалы",
        Figure = "Рисунок",
        Table = "Таблица",
        Footnotes = "Сноски",
        Definition = "Определение",
        Options = "Варианты",
        Description = "Описание",
        Property = "Свойство",
        Value = "Значение",
        Type = "Тип",
        Index = "Указатель",
        Notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["note"] = "Примечание",
            ["tip"] = "Совет",
            ["fastpath"] = "Быстрый путь",
            ["restriction"] = "Ограничение",
            ["important"] = "Важно",
            ["remember"] = "Помните",
            ["attention"] = "Внимание",
            ["caution"] = "Осторожно",
            ["notice"] = "Уведомление",
            ["danger"] = "Опасно",
            ["warning"] = "Предупреждение",
            ["trouble"] = "Возможная проблема",
            ["other"] = "Примечание"
        }
    };

    public static readonly Labels English = new()
    {
        Contents = "Contents",
        Prerequisites = "Before you begin",
        Context = "About this task",
        Steps = "Procedure",
        Result = "Results",
        Example = "Example",
        PostRequisites = "What to do next",
        TaskTroubleshooting = "Troubleshooting",
        Condition = "Condition",
        Cause = "Cause",
        Remedy = "Remedy",
        RelatedLinks = "Related information",
        Figure = "Figure",
        Table = "Table",
        Footnotes = "Footnotes",
        Definition = "Definition",
        Options = "Options",
        Description = "Description",
        Property = "Property",
        Value = "Value",
        Type = "Type",
        Index = "Index",
        Notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["note"] = "Note",
            ["tip"] = "Tip",
            ["fastpath"] = "Fastpath",
            ["restriction"] = "Restriction",
            ["important"] = "Important",
            ["remember"] = "Remember",
            ["attention"] = "Attention",
            ["caution"] = "Caution",
            ["notice"] = "Notice",
            ["danger"] = "Danger",
            ["warning"] = "Warning",
            ["trouble"] = "Trouble",
            ["other"] = "Note"
        }
    };

    public string Contents { get; init; } = "Contents";

    public string Prerequisites { get; init; } = string.Empty;

    public string Context { get; init; } = string.Empty;

    public string Steps { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string Example { get; init; } = string.Empty;

    public string PostRequisites { get; init; } = string.Empty;

    public string TaskTroubleshooting { get; init; } = string.Empty;

    public string Condition { get; init; } = string.Empty;

    public string Cause { get; init; } = string.Empty;

    public string Remedy { get; init; } = string.Empty;

    public string RelatedLinks { get; init; } = string.Empty;

    public string Figure { get; init; } = string.Empty;

    public string Table { get; init; } = string.Empty;

    public string Footnotes { get; init; } = string.Empty;

    public string Definition { get; init; } = string.Empty;

    public string Options { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Property { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Index { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Notes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string NoteLabel(string? type) =>
        Notes.TryGetValue(type ?? "note", out var label) ? label : Notes.GetValueOrDefault("note", "Note");

    public static Labels For(string? language) =>
        language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? English
            : Russian;
}
