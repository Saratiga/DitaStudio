using System.Text.RegularExpressions;
using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx.Styling;

/// <summary>
/// Оформление DOCX, полученное из пользовательского CSS проекта: стили Word (оформление по
/// умолчанию + правила CSS по селекторам HTML-публикации), вид таблиц, пометка изменений, размер
/// страницы из @page и правила для собственных классов (outputclass). Применяются правила без
/// @media, @media print и @media docx; @media screen и запросы с условиями пропускаются.
/// </summary>
public sealed partial class DocxStyleSheet
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

    private void Build(CssStyleSheet parsed)
    {
        foreach (var at in parsed.SkippedAtRules.Distinct())
        {
            _diag.Add(at == "@font-face"
                ? Loc.T("Core_FontFaceRulesAreNotCarried")
                : Loc.T("Core_TheRules0AreNotSupported", at));
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
}
