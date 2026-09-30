using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Разрыв страницы перед топиком — свойство строки карты (<c>topicref</c>), записанное классом <c>outputclass</c>:
/// <c>page-break-before</c> — топик начинается с новой страницы, <c>page-break-none</c> — не начинается, даже если
/// в «Оформление DOCX» включено «каждый топик верхнего уровня — с новой страницы». Публикации DOCX, PDF и единый
/// HTML учитывают класс; в постраничном HTML (сайте) страниц нет.
/// </summary>
public static class TopicPageBreak
{
    public const string Before = "page-break-before";
    public const string None = "page-break-none";

    /// <summary>true — с новой страницы, false — не с новой, null — как по умолчанию (настройка оформления).</summary>
    public static bool? Of(DitaNode? mapRow)
    {
        var tokens = Tokens(mapRow);
        return tokens.Contains(Before) ? true : tokens.Contains(None) ? false : null;
    }

    /// <summary>Ставит выбор (null — снимает оба класса); остальные классы outputclass не трогает.</summary>
    public static void Set(DitaNode mapRow, bool? value)
    {
        var tokens = Tokens(mapRow).Where(t => t is not (Before or None)).ToList();
        if (value == true)
        {
            tokens.Add(Before);
        }
        else if (value == false)
        {
            tokens.Add(None);
        }

        mapRow.SetAttribute("outputclass", tokens.Count == 0 ? null : string.Join(' ', tokens));
    }

    private static List<string> Tokens(DitaNode? node) =>
        (node?.GetAttribute("outputclass") ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
}
