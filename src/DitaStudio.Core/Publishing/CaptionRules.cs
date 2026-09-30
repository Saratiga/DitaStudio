using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>Разделитель между номером и названием в подписи.</summary>
public enum CaptionSeparator
{
    /// <summary>«Рисунок 1. Название».</summary>
    Period,

    /// <summary>«Рисунок 1 — Название».</summary>
    Dash
}

/// <summary>Подписи таблиц и рисунков: когда подпись выводится и получает номер.</summary>
public static class CaptionRules
{
    /// <summary>Текст между номером и названием: «. » или « — ».</summary>
    public static string Separator(CaptionSeparator separator) => separator == CaptionSeparator.Dash ? " — " : ". ";

    /// <summary>
    /// У рисунка или таблицы есть подпись: элемент <c>title</c> есть — пусть даже пустой (тогда подпись «Рисунок N» /
    /// «Таблица N» без точки и названия, номер расходуется). Элемента нет вообще — подписи нет и номер не тратится.
    /// </summary>
    public static bool HasCaption(DitaNode? title) => title is not null;

    /// <summary>
    /// В <paramref name="title"/> есть что показать: текст или вложенный элемент (например, ключевое
    /// слово или картинка). Разделитель и название печатаются только у такого заголовка.
    /// </summary>
    public static bool HasContent(DitaNode? title) =>
        title is not null &&
        (!string.IsNullOrWhiteSpace(title.InnerText) || title.Children.Any(child => child.Kind == NodeKind.Element));
}
