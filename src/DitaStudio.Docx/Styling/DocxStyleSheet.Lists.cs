using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

// Маркеры и нумерация списков: правила ul/ol/li, abstractNum для классов.
public sealed partial class DocxStyleSheet
{
    // Маркеры списков: 9 уровней Word = вложенность ul, из правил ul, ul ul…, li и li::marker.
    private readonly DocxListMarker[] _bulletMarkers = Enumerable.Range(0, 9).Select(DocxListMarker.DefaultForLevel).ToArray();
    private readonly string?[] _bulletColors = new string?[9];
    private readonly List<ListRule> _listRules = new();

    /// <summary>Маркер маркированного списка на уровне вложенности ilvl (0 — верхний).</summary>
    public DocxListMarker BulletMarker(int ilvl) => _bulletMarkers[Math.Clamp(ilvl, 0, 8)];

    /// <summary>Цвет маркера на уровне ilvl (li::marker { color }); null — как у текста.</summary>
    public string? BulletColor(int ilvl) => _bulletColors[Math.Clamp(ilvl, 0, 8)];

    /// <summary>Цвет номеров нумерованных списков (ol li::marker или li::marker).</summary>
    public string? OrderedColor { get; private set; }

    /// <summary>Маркер списка с данными классами (outputclass или имя элемента: «.dash» или
    /// «ul.dash» с list-style-type); null — правил для этих классов нет.</summary>
    public DocxListMarker? MarkerForClasses(IReadOnlyCollection<string> classes)
    {
        DocxListMarker? result = null;
        foreach (var rule in _classRules.Where(r => r.Classes.All(classes.Contains))
                     .OrderBy(r => r.Specificity).ThenBy(r => r.Order))
        {
            foreach (var d in rule.Declarations)
            {
                if (d.Property is "list-style-type" or "list-style" &&
                    DocxListMarker.Parse(Substitute(d.Value)) is { } marker)
                {
                    result = marker;
                }
            }
        }

        return result;
    }

    /// <summary>Правило для списков: Kind — ul, ol или any (просто li); Depth — сколько ul в
    /// цепочке селектора (правило действует с этого уровня вглубь); Marker — псевдоэлемент ::marker.</summary>
    private sealed record ListRule(string Kind, int Depth, bool Marker, int Specificity, int Order, IReadOnlyList<CssDeclaration> Declarations);

    /// <summary>Цепочка из ul/ol/li (через пробел или &gt;), возможно с li::marker в конце.
    /// null — это не селектор списка.</summary>
    private static (string Kind, int Depth, bool Marker)? ParseListSelector(string key)
    {
        var tokens = key.Split(new[] { ' ', '>' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
        {
            return null;
        }

        var marker = false;
        if (tokens[^1] == "li::marker")
        {
            marker = true;
            tokens[^1] = "li";
        }

        if (tokens.Any(t => t is not ("ul" or "ol" or "li")))
        {
            return null;
        }

        var kind = tokens.LastOrDefault(t => t is "ul" or "ol") ?? "any";
        return (kind, tokens.Count(t => t == "ul"), marker);
    }

    private void CollectListRules(CssStyleSheet parsed)
    {
        foreach (var rule in parsed.Rules)
        {
            foreach (var selector in rule.Selectors)
            {
                if (ParseListSelector(LowercaseTypes(selector)) is { } list)
                {
                    _listRules.Add(new ListRule(list.Kind, list.Depth, list.Marker, Specificity(selector), rule.Order, rule.Declarations));
                }
            }
        }
    }

    private void ResolveListMarkers()
    {
        var ordered = _listRules.OrderBy(r => r.Specificity).ThenBy(r => r.Order).ToList();

        for (var ilvl = 0; ilvl < 9; ilvl++)
        {
            // Правило «ul ul» (глубина 2) действует на второй уровень и глубже — как в CSS.
            foreach (var rule in ordered.Where(r => r.Kind is "ul" or "any" && r.Depth <= ilvl + 1))
            {
                foreach (var d in rule.Declarations)
                {
                    var value = Substitute(d.Value);
                    var isMarkerText = rule.Marker ? d.Property == "content" : d.Property is "list-style-type" or "list-style";
                    if (isMarkerText)
                    {
                        if (DocxListMarker.Parse(value) is { } marker)
                        {
                            _bulletMarkers[ilvl] = marker;
                        }
                        else
                        {
                            _diag.UnsupportedValue(d.Property, value);
                        }
                    }
                    else if (rule.Marker && d.Property == "color" && CssValues.TryParseColor(value, out var color))
                    {
                        _bulletColors[ilvl] = color.Length == 0 ? null : color;
                    }
                }
            }
        }

        foreach (var rule in ordered.Where(r => r.Kind is "ol" or "any"))
        {
            foreach (var d in rule.Declarations)
            {
                var value = Substitute(d.Value);
                if (rule.Marker && d.Property == "color" && CssValues.TryParseColor(value, out var color))
                {
                    OrderedColor = color.Length == 0 ? null : color;
                }
                else if (rule.Kind == "ol" && d.Property is "list-style-type" or "list-style")
                {
                    // вид нумерации (буквы, римские цифры) в Word пока не переносится
                    _diag.UnsupportedValue("ol " + d.Property, value);
                }
            }
        }
    }

    private static void AddTarget(Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets,
        string target, int specificity, int order, IReadOnlyList<CssDeclaration> declarations)
    {
        if (!targets.TryGetValue(target, out var list))
        {
            list = new List<(int, int, IReadOnlyList<CssDeclaration>)>();
            targets[target] = list;
        }

        list.Add((specificity, order, declarations));
    }

    private static bool MentionsHtmlOnlyClass(string selector) =>
        Regex.Matches(selector, @"\.([A-Za-z_][\w-]*)").Any(m => DocxStyleCatalog.HtmlOnlyClasses.Contains(m.Groups[1].Value)) ||
        Regex.IsMatch(selector, @"(^|[\s>+~])(nav|header|footer|aside|img|figure|svg|button|input|hr|iframe|video)\b", RegexOptions.IgnoreCase);
}
