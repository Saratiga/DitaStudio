using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

// Рендер HTML: служебные методы (атрибуты, классы, экранирование).
public sealed partial class HtmlRenderer
{
    private string Attrs(DitaNode node) => IdAttr(node) + BuildClassAttr(null, node);

    private string IdAttr(DitaNode node)
    {
        var id = node.GetAttribute("id");
        return string.IsNullOrEmpty(id) ? string.Empty : $" id=\"{Escape(PrefixedId(id!))}\"";
    }

    /// <summary>Добавляет префикс "имяФайла--" к id элемента в однофайловой сборке — см.
    /// RenderOptions.SingleFileAnchors. В постраничной сборке id остаются как есть.</summary>
    private string PrefixedId(string id) =>
        _options.SingleFileAnchors && _document.FilePath is not null
            ? HtmlPublisher.AnchorFor(_document.FilePath, id)
            : id;

    /// <summary>class="..." (и style="...", если применилось правило подсветки) из outputclass узла,
    /// если он задан — иначе пустая строка.</summary>
    private string OptionalClassAttr(DitaNode node) => BuildClassAttr(null, node);

    /// <summary>class="..." только из outputclass — для ячеек, у которых свой style (выравнивание).</summary>
    private string OutputClassAttr(DitaNode node) =>
        node.GetAttribute("outputclass") is { } value && !string.IsNullOrWhiteSpace(value) ? $" class=\"{Escape(value)}\"" : string.Empty;

    /// <summary>class="baseClass outputclass" одним атрибутом — не дублирует class, если outputclass задан.</summary>
    private string MergedClassAttr(string baseClass, DitaNode? node) => BuildClassAttr(baseClass, node);

    /// <summary>Собирает class="..." из базового класса, outputclass узла и класса rev-changed,
    /// если у элемента задан непустой атрибут rev — полоска на полях при публикации, штатный
    /// DITA-механизм пометки изменений (не полноценный track changes с историей правок). Плюс
    /// class/style от совпавшего правила подсветки .ditaval (action="flag").</summary>
    private string BuildClassAttr(string? baseClass, DitaNode? node)
    {
        var classes = new List<string>();
        if (!string.IsNullOrEmpty(baseClass))
        {
            classes.Add(baseClass!);
        }

        var outputclass = node?.GetAttribute("outputclass");
        if (!string.IsNullOrWhiteSpace(outputclass))
        {
            classes.Add(outputclass!);
        }

        if (!string.IsNullOrWhiteSpace(node?.GetAttribute("rev")))
        {
            classes.Add("rev-changed");
        }

        if (node is not null && TrackChanges.IsInserted(node))
        {
            classes.Add("tc-inserted");
        }
        else if (node is not null && TrackChanges.IsDeleted(node))
        {
            classes.Add("tc-deleted");
        }

        var flag = node is null ? null : ResolveFlagRule(node);
        if (flag is not null)
        {
            classes.Add("ditaval-flag");
        }

        var classAttr = classes.Count == 0 ? string.Empty : $" class=\"{Escape(string.Join(' ', classes))}\"";
        var styleAttr = FlagStyleAttr(flag);

        // Свой размер шрифта (size-13_5): встроенного CSS-класса у него нет, размер ставится стилем элемента.
        var customSize = string.IsNullOrWhiteSpace(outputclass) ? null : outputclass!
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(TextFormatting.CustomSizeCss).FirstOrDefault(css => css is not null);
        if (customSize is not null)
        {
            styleAttr = styleAttr.Length == 0 ? $" style=\"{customSize}\"" : styleAttr.Replace("style=\"", $"style=\"{customSize}; ");
        }

        return classAttr + styleAttr;
    }

    /// <summary>Первое правило подсветки .ditaval, у которого атрибут узла присутствует и (если задан
    /// @val) совпадает с одним из токенов значения. Правила без @val совпадают при любом значении.</summary>
    private DitavalFlagRule? ResolveFlagRule(DitaNode node)
    {
        var rules = _options.FlagRules;
        if (rules is null || rules.Count == 0)
        {
            return null;
        }

        foreach (var rule in rules)
        {
            var value = node.GetAttribute(rule.Attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (rule.Value is null)
            {
                return rule;
            }

            var tokens = value!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains(rule.Value))
            {
                return rule;
            }
        }

        return null;
    }

    private static string FlagStyleAttr(DitavalFlagRule? rule)
    {
        if (rule is null)
        {
            return string.Empty;
        }

        var style = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.Color))
        {
            style.Add($"color:{rule.Color}");
        }

        if (!string.IsNullOrWhiteSpace(rule.BackgroundColor))
        {
            style.Add($"background-color:{rule.BackgroundColor}");
        }

        switch (rule.Style)
        {
            case "bold":
                style.Add("font-weight:bold");
                break;
            case "italics":
                style.Add("font-style:italic");
                break;
            case "underline":
                style.Add("text-decoration:underline");
                break;
        }

        if (!string.IsNullOrWhiteSpace(rule.ChangeBar))
        {
            style.Add($"border-left:3px solid {rule.ChangeBar};padding-left:6px");
        }

        return style.Count == 0 ? string.Empty : $" style=\"{Escape(string.Join(';', style))}\"";
    }

    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string StripTags(string html)
    {
        var sb = new StringBuilder();
        var inside = false;
        foreach (var ch in html)
        {
            if (ch == '<')
            {
                inside = true;
            }
            else if (ch == '>')
            {
                inside = false;
            }
            else if (!inside)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }
}
