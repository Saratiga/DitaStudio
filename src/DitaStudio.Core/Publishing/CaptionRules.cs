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
    /// В <paramref name="title"/> есть что показать: текст или вложенный элемент (например, ключевое
    /// слово или картинка). Пустой <c>&lt;title/&gt;</c> и заголовок из одних пробелов подписью не
    /// считаются — такая таблица или рисунок выводятся без «Таблица №» и не занимают номер.
    /// </summary>
    public static bool HasContent(DitaNode? title) =>
        title is not null &&
        (!string.IsNullOrWhiteSpace(title.InnerText) || title.Children.Any(child => child.Kind == NodeKind.Element));
}
