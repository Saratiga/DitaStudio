using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

// Правила CSS по селекторам: разбор, сопоставление, псевдоэлементы, специфичность.
public sealed partial class DocxStyleSheet
{
    /// <summary>Есть правила с настоящими селекторами (потомок, атрибут, псевдокласс, псевдоэлемент).</summary>
    public bool HasSelectorRules => _selectorRules.Count > 0 || _pseudoRules.Count > 0;

    /// <summary>Правила с селекторами, подошедшие элементу (без псевдоэлементов), в порядке каскада.</summary>
    public IReadOnlyList<DocxSelectorRule> MatchSelectorRules(DitaNode node)
    {
        List<DocxSelectorRule>? matched = null;
        foreach (var rule in _selectorRules)
        {
            if (rule.Selector.Matches(node))
            {
                rule.Hits++;
                (matched ??= new List<DocxSelectorRule>()).Add(rule);
            }
        }

        return matched is null
            ? Array.Empty<DocxSelectorRule>()
            : matched.OrderBy(r => r.Specificity).ThenBy(r => r.Order).ToList();
    }

    /// <summary>
    /// Текст <c>content</c> псевдоэлемента <c>::before</c> / <c>::after</c> элемента: строки в кавычках и
    /// <c>attr(имя)</c>; null — правила нет, <c>content: none</c> или значение не разобрано.
    /// </summary>
    public string? PseudoContent(DitaNode node, string which)
    {
        string? content = null;
        foreach (var rule in _pseudoRules.Where(r => r.Selector.PseudoElement == which && r.Selector.Matches(node))
                     .OrderBy(r => r.Specificity).ThenBy(r => r.Order))
        {
            rule.Hits++;
            foreach (var d in rule.Declarations.Where(d => d.Property == "content"))
            {
                content = ParseContent(Substitute(d.Value), node);
            }
        }

        return string.IsNullOrEmpty(content) ? null : content;
    }

    private static string? ParseContent(string value, DitaNode node)
    {
        if (value.Trim() is "none" or "normal" or "")
        {
            return null;
        }

        var text = new System.Text.StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '"' or '\'')
            {
                i++;
                while (i < value.Length && value[i] != c)
                {
                    if (value[i] == '\\' && i + 1 < value.Length)
                    {
                        i++;
                        text.Append(value[i] is 'A' or 'a' ? ' ' : value[i]);
                    }
                    else
                    {
                        text.Append(value[i]);
                    }

                    i++;
                }

                i++;
            }
            else if (value.AsSpan(i).StartsWith("attr(", StringComparison.OrdinalIgnoreCase))
            {
                var close = value.IndexOf(')', i);
                if (close < 0)
                {
                    return null;
                }

                text.Append(node.GetAttribute(value[(i + 5)..close].Trim()) ?? string.Empty);
                i = close + 1;
            }
            else
            {
                return null; // counter(), url() и прочее в тексте Word не воспроизводится
            }
        }

        return text.ToString();
    }

    /// <summary>Селекторы, не подошедшие ни к одному элементу публикации, — для предупреждения в журнале сборки.</summary>
    public IReadOnlyList<string> UnmatchedSelectors() =>
        _selectorRules.Concat(_pseudoRules).Where(r => r.Hits == 0).Select(r => r.Selector.Text).Distinct().ToList();

    private static readonly Regex StatePseudo = new(@":(hover|focus|focus-within|focus-visible|active|visited|target|checked|disabled)\b", RegexOptions.Compiled);
    private static readonly Regex SimpleClasses = new(@"^\*?((?:\.[A-Za-z_][\w-]*)+)$", RegexOptions.Compiled);
    private static readonly Regex TypeAndClasses = new(@"^[a-z][a-z0-9]*((?:\.[A-Za-z_][\w-]*)+)$", RegexOptions.Compiled);

    private void ClassifySelector(string selector, int order, IReadOnlyList<CssDeclaration> declarations,
        Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets)
    {
        // Состояния (наведение и т. п.) к документу неприменимы.
        if (StatePseudo.IsMatch(selector))
        {
            return;
        }

        // Метки li::marker разбирают списки; ::before/::after — псевдоэлементы, их разбирает движок селекторов.
        var pseudoElement = selector.Contains("::", StringComparison.Ordinal) && !selector.Contains("::marker", StringComparison.Ordinal) ||
                            selector.Contains(":before", StringComparison.Ordinal) || selector.Contains(":after", StringComparison.Ordinal);
        if (pseudoElement)
        {
            AddSelectorRule(selector, order, declarations);
            return;
        }

        if (MentionsHtmlOnlyClass(selector))
        {
            return;
        }

        var key = LowercaseTypes(selector);
        var specificity = Specificity(selector);

        // ul, ul ul, ol… — не стили, а маркеры списков (CollectListRules); li остаётся и стилем
        // «Элемент списка», поэтому здесь не перехватывается.
        if (key != "li" && ParseListSelector(key) is { Marker: false } chain)
        {
            // «ol > li», «ul li»: маркеры разбирают списки, а всё остальное (цвет, размер, начертание) относится
            // к тексту пункта — это обычное правило селектора. Списки ul/ol сами текст не оформляют.
            var text = declarations.Where(d => !d.Property.StartsWith("list-style", StringComparison.Ordinal)).ToList();
            if (text.Count > 0 && key.EndsWith("li", StringComparison.Ordinal))
            {
                AddSelectorRule(selector, order, text);
            }

            return;
        }

        if (key != "li" && ParseListSelector(key) is not null)
        {
            return;
        }

        if (DocxStyleCatalog.SelectorTargets.TryGetValue(key, out var mapped))
        {
            foreach (var target in mapped)
            {
                var forTarget = declarations;
                if (!target.StartsWith('@') && DocxStyleCatalog.InheritOnlySelectors.Contains(key))
                {
                    forTarget = declarations.Where(d => DocxStyleCatalog.IsInheritedProperty(d.Property)).ToList();
                }

                if (forTarget.Count > 0)
                {
                    AddTarget(targets, target, specificity, order, forTarget);
                }
            }

            return;
        }

        var classMatch = SimpleClasses.Match(key);
        var typedMatch = classMatch.Success ? Match.Empty : TypeAndClasses.Match(key);
        var classPart = classMatch.Success ? classMatch.Groups[1].Value : typedMatch.Success ? typedMatch.Groups[1].Value : null;
        if (classPart is not null)
        {
            // «p.shortdesc» — тот же стиль, что «.shortdesc»: теги HTML и Word не совпадают один в один
            if (DocxStyleCatalog.SelectorTargets.TryGetValue(classPart, out var byClass))
            {
                foreach (var target in byClass)
                {
                    AddTarget(targets, target, specificity, order, declarations);
                }

                return;
            }

            var classes = classPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
            _classRules.Add(new DocxClassRule(classes, declarations, specificity, order));
            return;
        }

        AddSelectorRule(selector, order, declarations);
    }

    // Селектор, которого нет среди стилей каталога и простых классов: разбирается движком CssSelector и
    // сопоставляется с деревом DITA при сборке. Не разобранный — в предупреждение.
    private void AddSelectorRule(string selector, int order, IReadOnlyList<CssDeclaration> declarations)
    {
        if (CssSelector.TryParse(selector, out var error) is not { } parsed)
        {
            _diag.UnsupportedSelector(selector + (error is null ? string.Empty : " (" + error + ")"));
            return;
        }

        var rule = new DocxSelectorRule(parsed, declarations, order);
        (parsed.PseudoElement is null ? _selectorRules : _pseudoRules).Add(rule);
    }

    // Имена тегов — без учёта регистра, классы — как написаны.
    private static string LowercaseTypes(string selector) =>
        Regex.Replace(selector, @"(^|[\s>+~])([A-Za-z][A-Za-z0-9]*)", m => m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant());

    /// <summary>Специфичность как одно число: id·10000 + (классы, атрибуты, псевдоклассы)·100 + теги.</summary>
    public static int Specificity(string selector)
    {
        var ids = Regex.Matches(selector, @"#[\w-]+").Count;
        var classes = Regex.Matches(selector, @"\.[\w-]+|\[[^\]]*\]|(?<!:):[\w-]+").Count;
        var types = Regex.Matches(selector, @"(?:^|[\s>+~])([a-zA-Z][\w-]*)").Count;
        return ids * 10000 + classes * 100 + types;
    }
}
