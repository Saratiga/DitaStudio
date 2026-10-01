using System.Text.RegularExpressions;
using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

/// <summary>Вид таблиц: рамки, заливка шапки и строк, внутренние поля ячеек.</summary>
public sealed class DocxTableLook
{
    public DocxBorder Outer { get; set; } = new("single", 0.5, "999999");
    public DocxBorder Inner { get; set; } = new("single", 0.5, "CCCCCC");

    /// <summary>Заливка ячеек шапки; "" — без заливки.</summary>
    public string HeaderFill { get; set; } = "E8E8E8";

    /// <summary>Заливка остальных ячеек; "" — без заливки.</summary>
    public string BodyFill { get; set; } = string.Empty;

    /// <summary>Внутренние поля ячеек, пт; null — как в Word по умолчанию.</summary>
    public double? CellPaddingPt { get; set; }

    /// <summary>Ширина таблицы (CSS <c>table { width }</c>) в процентах ширины текста; null — 100 %.</summary>
    public double? WidthPercent { get; set; }

    /// <summary>Ширина таблицы в пунктах (если задана длиной); null — не задана.</summary>
    public double? WidthPt { get; set; }
}

/// <summary>Размер страницы и поля, пт. По умолчанию — A4 с полями 20 мм.</summary>
public sealed record DocxPageSetup(double WidthPt, double HeightPt, double TopPt, double RightPt, double BottomPt, double LeftPt)
{
    public const double MmToPt = 72 / 25.4;

    public static DocxPageSetup A4 => new(210 * MmToPt, 297 * MmToPt, 20 * MmToPt, 20 * MmToPt, 20 * MmToPt, 20 * MmToPt);
}

/// <summary>Правило CSS для произвольного класса — значения outputclass или имени элемента DITA.</summary>
public sealed record DocxClassRule(IReadOnlyList<string> Classes, IReadOnlyList<CssDeclaration> Declarations, int Specificity, int Order);

/// <summary>
/// Правило с настоящим селектором (потомок, ребёнок, атрибут, <c>:first-child</c>, <c>:nth-child()</c>, <c>:not()</c>,
/// <c>::before</c>…): сопоставляется с элементом DITA во время сборки. <see cref="Hits"/> — на сколько элементов
/// оно подошло (для предупреждения о правиле, которое ничего не оформило).
/// </summary>
public sealed class DocxSelectorRule
{
    public DocxSelectorRule(CssSelector selector, IReadOnlyList<CssDeclaration> declarations, int order)
    {
        Selector = selector;
        Declarations = declarations;
        Order = order;
    }

    public CssSelector Selector { get; }

    public IReadOnlyList<CssDeclaration> Declarations { get; }

    public int Order { get; }

    public int Specificity => Selector.Specificity;

    public int Hits { get; set; }
}

/// <summary>
/// Оформление DOCX, полученное из пользовательского CSS проекта: стили Word (оформление по
/// умолчанию + правила CSS по селекторам HTML-публикации), вид таблиц, пометка изменений, размер
/// страницы из @page и правила для собственных классов (outputclass). Применяются правила без
/// @media, @media print и @media docx; @media screen и запросы с условиями пропускаются.
/// </summary>
public sealed class DocxStyleSheet
{
    private readonly Dictionary<string, DocxStyleProps> _styles = new(StringComparer.Ordinal);
    private readonly List<DocxClassRule> _classRules = new();
    private readonly List<DocxSelectorRule> _selectorRules = new();
    private readonly List<DocxSelectorRule> _pseudoRules = new();
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly Diagnostics _diag = new();

    public IReadOnlyDictionary<string, DocxStyleProps> Styles => _styles;
    public DocxTableLook Table { get; } = new();
    public DocxBorder RevBorder { get; private set; } = new("single", 2.25, "D4380D", 4);
    public DocxPageSetup Page { get; private set; } = DocxPageSetup.A4;

    /// <summary>Колонтитулы из <c>@page { @top-left { content: … } }</c> — перекрывают заданные в диалоге.</summary>
    public DitaStudio.Core.Publishing.PageMarginBoxes MarginBoxes { get; private set; } = DitaStudio.Core.Publishing.PageMarginBoxes.Empty;
    public IReadOnlyList<DocxClassRule> ClassRules => _classRules;

    /// <summary>Все классы, упомянутые в правилах для собственных классов.</summary>
    public IReadOnlySet<string> CustomClasses { get; private set; } = new HashSet<string>();

    /// <summary>
    /// Свой размер шрифта (класс size-13_5) или свой цвет маркера (mark-ff8800): правила для них в CSS нет, поэтому оно создаётся при
    /// первой встрече — как встроенные правила других размеров и цветов. false — класс не «свой».
    /// </summary>
    public bool RegisterSizeClass(string token)
    {
        if (CustomClasses.Contains(token))
        {
            return true;
        }

        if (DitaStudio.Core.Publishing.TextFormatting.CustomClassCss(token) is not { } css)
        {
            return false;
        }

        var declarations = css.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(':', 2)).Select(parts => new CssDeclaration(parts[0].Trim(), parts[1].Trim())).ToArray();
        _classRules.Add(new DocxClassRule(new[] { token }, declarations, 100, -1));
        CustomClasses = new HashSet<string>(CustomClasses) { token };
        return true;
    }

    public IReadOnlyList<string> Warnings => _diag.Messages;

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

    public static DocxStyleSheet Default => FromCss(null);

    public static DocxStyleSheet FromCss(string? css)
    {
        var sheet = new DocxStyleSheet();
        var parsed = CssParser.Parse(css ?? string.Empty, CssParser.DocxMedia);
        sheet.Build(parsed);
        sheet.MarginBoxes = DitaStudio.Core.Publishing.PageMarginBoxes.Parse(css);
        foreach (var warning in sheet.MarginBoxes.Warnings)
        {
            sheet._diag.Add(warning);
        }

        return sheet;
    }

    /// <summary>Итоговый размер шрифта стиля (с учётом цепочки базовых), пт.</summary>
    public double FontSizeOf(string styleId)
    {
        var def = DocxStyleCatalog.Get(styleId);
        if (_styles.TryGetValue(styleId, out var props) && props.FontSizePt is { } size)
        {
            return size;
        }

        if (def?.BasedOn is { } parent)
        {
            return FontSizeOf(parent);
        }

        return styleId == DocxStyleCatalog.Normal ? DocxDefaults.FontSizePt : FontSizeOf(DocxStyleCatalog.Normal);
    }

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

    /// <summary>Оформление для элемента с данными классами поверх базового стиля (null — ни одно
    /// правило не подошло). Правила применяются в порядке специфичности и следования в файле.</summary>
    public DocxStyleProps? PropsForClasses(IReadOnlyCollection<string> classes, double baseFontPt) =>
        PropsFor(classes, Array.Empty<DocxSelectorRule>(), baseFontPt);

    /// <summary>То же, что <see cref="PropsForClasses"/>, но вместе с правилами селекторов: классовые и селекторные правила
    /// складываются в один каскад по специфичности и порядку.</summary>
    public DocxStyleProps? PropsFor(IReadOnlyCollection<string> classes, IReadOnlyList<DocxSelectorRule> selectorRules, double baseFontPt)
    {
        if (classes.Count == 0 && selectorRules.Count == 0)
        {
            return null;
        }

        var declarations = _classRules
            .Where(r => classes.Count > 0 && r.Classes.All(classes.Contains))
            .Select(r => (r.Specificity, r.Order, r.Declarations))
            .Concat(selectorRules.Select(r => (r.Specificity, r.Order, r.Declarations)))
            .OrderBy(r => r.Specificity).ThenBy(r => r.Order)
            .SelectMany(r => r.Declarations)
            .ToList();
        if (declarations.Count == 0)
        {
            return null;
        }

        var box = new CssBox(new DocxStyleProps());
        CssApplier.Apply(box, declarations, baseFontPt, this, silent: true);
        box.ResolvePadding();
        return box.Props.IsEmpty ? null : box.Props;
    }

    internal string Substitute(string value)
    {
        for (var depth = 0; depth < 5 && value.Contains("var(", StringComparison.Ordinal); depth++)
        {
            value = VarRegex.Replace(value, m =>
            {
                var name = m.Groups[1].Value;
                if (_variables.TryGetValue(name, out var v))
                {
                    return v;
                }

                return m.Groups[2].Success ? m.Groups[2].Value.Trim() : m.Value;
            });
        }

        return value;
    }

    private static readonly Regex VarRegex = new(@"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*(?:\([^()]*\))?[^()]*))?\)", RegexOptions.Compiled);

    internal Diagnostics Diag => _diag;

    // =============================================================== построение

    private void Build(CssStyleSheet parsed)
    {
        foreach (var at in parsed.SkippedAtRules.Distinct())
        {
            _diag.Add(at == "@font-face"
                ? "Правила @font-face в DOCX не переносятся: шрифт должен быть установлен на компьютере, где открывают документ."
                : $"Правила {at} в DOCX не поддерживаются и пропущены.");
        }

        CollectVariables(parsed);

        var targets = new Dictionary<string, List<(int Spec, int Order, IReadOnlyList<CssDeclaration> Decls)>>(StringComparer.Ordinal);
        foreach (var rule in parsed.Rules)
        {
            var declarations = rule.Declarations.Where(d => !d.Property.StartsWith("--", StringComparison.Ordinal)).ToList();
            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var selector in rule.Selectors)
            {
                ClassifySelector(selector, rule.Order, declarations, targets);
            }
        }

        CollectListRules(parsed);
        ResolveListMarkers();

        // Стили — в порядке каталога: базовый стиль всегда разрешается раньше производных.
        foreach (var def in DocxStyleCatalog.Styles)
        {
            var box = new CssBox(def.Defaults.Clone());
            if (targets.TryGetValue(def.Id, out var matched))
            {
                var parentSize = def.BasedOn is { } parent ? FontSizeOf(parent)
                    : def.Id == DocxStyleCatalog.Normal ? DocxDefaults.FontSizePt : FontSizeOf(DocxStyleCatalog.Normal);
                CssApplier.Apply(box, Ordered(matched), parentSize, this, silent: false);
                box.ResolvePadding();
            }

            var props = box.Props;
            if (def.Kind == DocxStyleKind.Character)
            {
                props = props.RunOnly();
            }

            _styles[def.Id] = props;
        }

        BuildTableLook(targets);
        BuildRevBorder(targets);
        BuildPage(parsed);
        CustomClasses = new HashSet<string>(_classRules.SelectMany(r => r.Classes), StringComparer.Ordinal);
    }

    private static List<CssDeclaration> Ordered(List<(int Spec, int Order, IReadOnlyList<CssDeclaration> Decls)> matched) =>
        matched.OrderBy(m => m.Spec).ThenBy(m => m.Order).SelectMany(m => m.Decls).ToList();

    private void CollectVariables(CssStyleSheet parsed)
    {
        foreach (var rule in parsed.Rules)
        {
            if (!rule.Selectors.Any(s => s is ":root" or "html" or "body" or "*"))
            {
                continue;
            }

            foreach (var d in rule.Declarations.Where(d => d.Property.StartsWith("--", StringComparison.Ordinal)))
            {
                _variables[d.Property] = d.Value;
            }
        }
    }

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

    // =============================================================== списки

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

    private void BuildTableLook(Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets)
    {
        CssBox? Resolve(string target)
        {
            if (!targets.TryGetValue(target, out var matched))
            {
                return null;
            }

            var box = new CssBox(new DocxStyleProps());
            CssApplier.Apply(box, Ordered(matched), FontSizeOf(DocxStyleCatalog.TableText), this, silent: true);
            return box;
        }

        static DocxBorder? AnySide(DocxStyleProps p) => p.BorderTop ?? p.BorderLeft ?? p.BorderBottom ?? p.BorderRight;

        if (targets.TryGetValue(DocxStyleCatalog.TableTarget, out var tableRules) &&
            Ordered(tableRules).LastOrDefault(d => d.Property == "width") is { } widthRule)
        {
            var width = Substitute(widthRule.Value).Trim();
            if (width.EndsWith('%') && double.TryParse(width[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                Table.WidthPercent = Math.Clamp(percent, 5, 100);
            }
            else if (CssApplier.Length(width, DocxDefaults.FontSizePt) is { } widthPt && widthPt > 0)
            {
                Table.WidthPt = widthPt;
            }
        }

        var table = Resolve(DocxStyleCatalog.TableTarget);
        var th = Resolve(DocxStyleCatalog.HeaderCellTarget);
        var td = Resolve(DocxStyleCatalog.BodyCellTarget);

        // Рамки ячеек (th, td) задают и внутренние линии, и внешние — так таблица выглядит в
        // HTML со схлопнутыми рамками; явная рамка у самой таблицы перекрывает внешнюю.
        var cellBorder = (td is null ? null : AnySide(td.Props)) ?? (th is null ? null : AnySide(th.Props));
        if (cellBorder is not null)
        {
            Table.Inner = cellBorder;
            Table.Outer = cellBorder;
        }

        if (table is not null && AnySide(table.Props) is { } tableBorder)
        {
            Table.Outer = tableBorder;
        }

        if (th?.Props.Background is { } headerFill)
        {
            Table.HeaderFill = headerFill;
        }

        if (td?.Props.Background is { } bodyFill)
        {
            Table.BodyFill = bodyFill;
        }
        else if (table?.Props.Background is { } tableFill)
        {
            Table.BodyFill = tableFill;
        }

        var padding = td?.PaddingLeft ?? td?.PaddingTop ?? th?.PaddingLeft ?? th?.PaddingTop;
        if (padding is not null)
        {
            Table.CellPaddingPt = Math.Clamp(padding.Value, 0, 72);
        }
    }

    private void BuildRevBorder(Dictionary<string, List<(int, int, IReadOnlyList<CssDeclaration>)>> targets)
    {
        if (!targets.TryGetValue(DocxStyleCatalog.RevTarget, out var matched))
        {
            return;
        }

        var box = new CssBox(new DocxStyleProps { BorderLeft = RevBorder });
        CssApplier.Apply(box, Ordered(matched), DocxDefaults.FontSizePt, this, silent: true);
        box.ResolvePadding();
        RevBorder = box.Props.BorderLeft ?? RevBorder;
    }

    private void BuildPage(CssStyleSheet parsed)
    {
        if (parsed.PageRules.Any(r => r.Pseudo is not null))
        {
            _diag.Add("Правила @page :first/:left/:right в DOCX не поддерживаются — первая страница без колонтитулов " +
                      "и зеркальные поля задаются в «Публикация → Оформление DOCX…».");
        }

        var width = Page.WidthPt;
        var height = Page.HeightPt;
        double top = Page.TopPt, right = Page.RightPt, bottom = Page.BottomPt, left = Page.LeftPt;

        foreach (var rule in parsed.PageRules.Where(r => r.Pseudo is null).OrderBy(r => r.Order))
        {
            foreach (var d in rule.Declarations)
            {
                var value = Substitute(d.Value);
                switch (d.Property)
                {
                    case "size":
                        if (!TryParsePageSize(value, ref width, ref height))
                        {
                            _diag.UnsupportedValue(d.Property, value);
                        }

                        break;
                    case "margin":
                        var sides = CssApplier.Sides(value, DocxDefaults.FontSizePt);
                        if (sides is null)
                        {
                            _diag.UnsupportedValue(d.Property, value);
                            break;
                        }

                        (top, right, bottom, left) = sides.Value;
                        break;
                    case "margin-top" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } t:
                        top = t;
                        break;
                    case "margin-right" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } r:
                        right = r;
                        break;
                    case "margin-bottom" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } b:
                        bottom = b;
                        break;
                    case "margin-left" when CssApplier.Length(value, DocxDefaults.FontSizePt) is { } l:
                        left = l;
                        break;
                    default:
                        _diag.UnsupportedProperty("@page " + d.Property);
                        break;
                }
            }
        }

        static double Clamp(double v) => Math.Clamp(v, 0, 300);
        Page = new DocxPageSetup(width, height, Clamp(top), Clamp(right), Clamp(bottom), Clamp(left));
    }

    private static readonly Dictionary<string, (double W, double H)> PaperSizes = new(StringComparer.Ordinal)
    {
        ["a3"] = (297, 420), ["a4"] = (210, 297), ["a5"] = (148, 210), ["a6"] = (105, 148),
        ["b4"] = (250, 353), ["b5"] = (176, 250), ["letter"] = (215.9, 279.4), ["legal"] = (215.9, 355.6),
        ["ledger"] = (279.4, 431.8)
    };

    private static bool TryParsePageSize(string value, ref double width, ref double height)
    {
        var tokens = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double? w = null, h = null;
        var landscape = false;
        var lengths = new List<double>();
        foreach (var token in tokens)
        {
            if (PaperSizes.TryGetValue(token, out var paper))
            {
                (w, h) = (paper.W * DocxPageSetup.MmToPt, paper.H * DocxPageSetup.MmToPt);
            }
            else if (token == "landscape")
            {
                landscape = true;
            }
            else if (token is "portrait" or "auto")
            {
            }
            else if (CssValues.TryParseLength(token, out var length) && length.Unit is not ("em" or "%" or "ex" or "ch"))
            {
                lengths.Add(length.ToPoints(DocxDefaults.FontSizePt));
            }
            else
            {
                return false;
            }
        }

        if (lengths.Count == 1)
        {
            (w, h) = (lengths[0], lengths[0]);
        }
        else if (lengths.Count >= 2)
        {
            (w, h) = (lengths[0], lengths[1]);
        }

        w ??= width;
        h ??= height;
        if (landscape && w < h || !landscape && tokens.Contains("portrait") && w > h)
        {
            (w, h) = (h, w);
        }

        if (w < 72 || h < 72)
        {
            return false;
        }

        (width, height) = (w.Value, h.Value);
        return true;
    }

    // ================================================================ диагностика

    internal sealed class Diagnostics
    {
        private readonly List<string> _messages = new();
        private readonly SortedSet<string> _selectors = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _properties = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _values = new(StringComparer.Ordinal);
        private bool _finalized;

        public void Add(string message) => _messages.Add(message);

        public void UnsupportedSelector(string selector) => _selectors.Add(selector);

        public void UnsupportedProperty(string property) => _properties.Add(property);

        public void UnsupportedValue(string property, string value) => _values.Add($"{property}: {value}");

        public IReadOnlyList<string> Messages
        {
            get
            {
                if (!_finalized)
                {
                    _finalized = true;
                    if (_selectors.Count > 0)
                    {
                        _messages.Add("CSS → DOCX: селекторы не поддерживаются, правила пропущены: " + Sample(_selectors) +
                                      ". В DOCX работают теги и классы HTML-публикации (p, h1, .note, .shortdesc…), классы outputclass и " +
                                      "селекторы по элементам DITA: имя, .класс, #id, [атрибут], потомок, ребёнок, соседи, :first-child, " +
                                      ":last-child, :nth-child(), :nth-of-type(), :not(), :empty, ::before, ::after.");
                    }

                    if (_properties.Count > 0)
                    {
                        _messages.Add("CSS → DOCX: свойства не переносятся в Word: " + Sample(_properties) + ".");
                    }

                    if (_values.Count > 0)
                    {
                        _messages.Add("CSS → DOCX: значения не распознаны: " + Sample(_values) + ".");
                    }
                }

                return _messages;
            }
        }

        private static string Sample(IReadOnlyCollection<string> items)
        {
            const int limit = 8;
            var shown = string.Join(", ", items.Take(limit));
            return items.Count > limit ? $"{shown} и ещё {items.Count - limit}" : shown;
        }
    }
}

/// <summary>Значения по умолчанию документа (docDefaults).</summary>
public static class DocxDefaults
{
    public const string FontFamily = "Calibri";
    public const double FontSizePt = 11;
}
