using DitaStudio.Core.Model;

namespace DitaStudio.Core.Localization;

/// <summary>Язык документа DITA — атрибут <c>xml:lang</c> корня.</summary>
public static class DocumentLanguage
{
    /// <summary>Значение <c>xml:lang</c> корня (например, "ru-RU") или null, если не задан.</summary>
    public static string? Of(DitaDocument? document) =>
        document?.Root.GetAttribute("xml:lang") is { } lang && !string.IsNullOrWhiteSpace(lang) ? lang.Trim() : null;

    /// <summary>"ru-RU" → "ru"; пусто → null.</summary>
    public static string? Primary(string? language) =>
        string.IsNullOrWhiteSpace(language) ? null : language.Trim().Split('-', '_')[0].ToLowerInvariant();

    /// <summary>Язык подписей публикации для документа: его <c>xml:lang</c>, нет — язык интерфейса.</summary>
    public static string ForPublication(DitaDocument? document) => Of(document) ?? Loc.Instance.Language;
}
