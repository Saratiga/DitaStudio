using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Положение блока на отдельном листе печатного издания (PDF, DOCX): абзац, рисунок, таблица,
/// заметка ставятся на свою страницу в одну из девяти областей — вверху/посередине/внизу ×
/// слева/по центру/справа (гриф «Для служебного пользования» внизу листа, схема по центру).
/// Автор выбирает положение в «Авторе», в файл пишется класс <c>outputclass="place-bottom-right"</c>.
/// На экране (сайт, предпросмотр) блок остаётся в тексте. Работает для блоков прямо в теле
/// топика, разделе или div — не внутри списка, таблицы или заметки: там отдельного листа нет.
/// </summary>
public static class PagePlacement
{
    public const string Prefix = "place-";

    /// <summary>Девять положений: класс → подпись.</summary>
    public static readonly IReadOnlyList<(string Token, string Label)> Positions = new[]
    {
        ("place-top-left", "Вверху слева"),
        ("place-top-center", "Вверху по центру"),
        ("place-top-right", "Вверху справа"),
        ("place-middle-left", "Посередине слева"),
        ("place-middle-center", "По центру листа"),
        ("place-middle-right", "Посередине справа"),
        ("place-bottom-left", "Внизу слева"),
        ("place-bottom-center", "Внизу по центру"),
        ("place-bottom-right", "Внизу справа")
    };

    /// <summary>Блоки, которые можно поставить на отдельный лист.</summary>
    private static readonly HashSet<string> Placeable = new(StringComparer.Ordinal)
    {
        "p", "note", "fig", "table", "simpletable", "div", "lq", "lines", "pre", "codeblock", "hazardstatement"
    };

    /// <summary>Контейнеры, внутри которых блок всё ещё идёт «потоком» по листам.</summary>
    private static readonly HashSet<string> FlowContainers = new(StringComparer.Ordinal)
    {
        "section", "example", "refsyn", "context", "result", "prereq", "postreq", "bodydiv", "sectiondiv", "div"
    };

    private static readonly HashSet<string> Bodies = new(StringComparer.Ordinal)
    {
        "body", "conbody", "refbody", "taskbody", "troublebody", "glossBody"
    };

    /// <summary>Можно ли поставить узел на отдельный лист (вид блока и где он стоит).</summary>
    public static bool CanPlace(DitaNode node)
    {
        if (!Placeable.Contains(node.Name))
        {
            return false;
        }

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (Bodies.Contains(parent.Name))
            {
                return true;
            }

            if (!FlowContainers.Contains(parent.Name))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Ближайший к узлу (он сам или предок) блок, который можно поставить на лист.</summary>
    public static DitaNode? PlaceableFor(DitaNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (CanPlace(current))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>Положение узла (класс place-…) или null — в тексте или поставить нельзя.</summary>
    public static string? Of(DitaNode node) =>
        TextFormatting.Token(node, Prefix) is { } token && Positions.Any(p => p.Token == token) && CanPlace(node)
            ? token
            : null;

    /// <summary>Подпись положения («Внизу справа») или null.</summary>
    public static string? LabelOf(string? token) =>
        Positions.FirstOrDefault(p => p.Token == token).Label;

    /// <summary>По вертикали: top, middle, bottom.</summary>
    public static string Vertical(string token) => token[Prefix.Length..].Split('-')[0];

    /// <summary>По горизонтали: left, center, right.</summary>
    public static string Horizontal(string token) => token[Prefix.Length..].Split('-')[^1];

    /// <summary>Ставит положение (null — вернуть блок в текст). true — атрибут изменился.</summary>
    public static bool Set(DitaNode node, string? token) =>
        TextFormatting.SetToken(node, Prefix, token is not null && Positions.Any(p => p.Token == token) ? token : null);
}
