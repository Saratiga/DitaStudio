using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

// Оглавление из CSS. В Word оно — поле TOC со стилями «Заголовок оглавления» и «toc 1…6», а в HTML — навигация такой разметки:
//   <div class="toc"><h2 class="toc-title">Содержание</h2><ul><li class="lvl1"><span class="num">1</span><a class="text">…</a></li>…</ul></div>
// Правила CSS для этих классов (`.toc`, `.toc-title`, `.toc .lvl2`, `.toc .lvl1 > .num`, `.toc-title::before`…) сопоставляются с такой же
// «воображаемой» разметкой общим движком селекторов и ложатся в стили Word: любой селектор, который понимает движок, работает и здесь.
public sealed partial class DocxStyleSheet
{
    /// <summary>Глубина оглавления, до которой уровни не скрыты правилами CSS (<c>.toc .lvl4 { display: none }</c> убирает 4-й и глубже); 6 — скрытых нет.</summary>
    public int TocMaxLevel { get; private set; } = 6;

    /// <summary>Оглавление скрыто правилом <c>.toc { display: none }</c>.</summary>
    public bool TocHidden { get; private set; }

    /// <summary>Заголовок оглавления скрыт правилом <c>.toc-title { display: none }</c>.</summary>
    public bool TocTitleHidden { get; private set; }

    /// <summary>Название оглавления из CSS (<c>content</c> у <c>.toc-title</c> или <c>.toc-title::before/::after</c>); null — название не задано в CSS.</summary>
    public string? TocTitleText { get; private set; }

    private static DitaNode TocElement(string name, string? cssClass = null)
    {
        var node = DitaNode.Element(name);
        if (cssClass is not null)
        {
            node.SetAttribute("outputclass", cssClass);
        }

        return node;
    }

    /// <summary>Объявления, подходящие воображаемому элементу, в порядке каскада (специфичность, порядок в файле).</summary>
    internal List<CssDeclaration> Cascade(DitaNode node)
    {
        var tokens = CssSelector.ClassTokens(node);
        var matched = _classRules.Where(r => r.Classes.All(tokens.Contains)).Select(r => (r.Specificity, r.Order, r.Declarations))
            .Concat(MatchSelectorRules(node).Select(r => (r.Specificity, r.Order, r.Declarations)))
            .OrderBy(r => r.Specificity).ThenBy(r => r.Order)
            .SelectMany(r => r.Declarations)
            .ToList();
        return matched;
    }

    private void BuildToc()
    {
        if (_classRules.Count == 0 && _selectorRules.Count == 0 && _pseudoRules.Count == 0)
        {
            return;
        }

        var container = TocElement("div", "toc");
        var title = TocElement("h2", "toc-title");
        var list = TocElement("ul");
        container.Add(title);
        container.Add(list);
        var levels = new List<(int Level, DitaNode Item, DitaNode Number, DitaNode Text)>();
        for (var level = 1; level <= 6; level++)
        {
            var item = TocElement("li", "lvl" + level);
            var number = TocElement("span", "num");
            var text = TocElement("a", "text");
            item.Add(number);
            item.Add(text);
            list.Add(item);
            levels.Add((level, item, number, text));
        }

        var containerRules = Cascade(container);
        TocHidden = IsDisplayNone(containerRules);

        // Заголовок: наследуемые свойства контейнера (шрифт, цвет), затем собственные правила.
        var titleRules = containerRules.Where(d => DocxStyleCatalog.IsInheritedProperty(d.Property)).Concat(Cascade(title)).ToList();
        TocTitleHidden = IsDisplayNone(titleRules);
        var titleStyle = DocxStyleCatalog.TocHeading;
        _styles[titleStyle] = ApplyTocRules(titleStyle, titleRules);
        TocTitleText = TocContent(title, titleRules);

        var hiddenFrom = 7;
        foreach (var (level, item, number, text) in levels)
        {
            var itemRules = Cascade(item);
            var rules = containerRules.Where(d => DocxStyleCatalog.IsInheritedProperty(d.Property) || d.Property is "margin-left" or "padding-left")
                .Concat(itemRules).ToList();
            if (IsDisplayNone(itemRules) && hiddenFrom == 7)
            {
                hiddenFrom = level;
            }

            var style = DocxStyleCatalog.Toc(level);
            var props = ApplyTocRules(style, rules);

            // Номер раздела: «имитация таба» — ширина и отступ справа у .num; в Word это висячий отступ строки оглавления: номер
            // стоит у левого края, заголовок начинается на нужном расстоянии от него, а перенесённая строка выровнена по заголовку.
            var numberRules = Cascade(number);
            var em = props.FontSizePt ?? FontSizeOf(style); // em у .num — от размера шрифта строки оглавления
            var width = Dimension(numberRules, "min-width", em) ?? Dimension(numberRules, "width", em);
            var gap = (Dimension(numberRules, "margin-right", em) ?? 0) + (Dimension(numberRules, "padding-right", em) ?? 0);
            if (width is { } w && w + gap > 0 && !IsDisplayNone(numberRules))
            {
                var hanging = w + gap;
                props.IndentLeftPt = (props.IndentLeftPt ?? 0) + hanging;
                props.FirstLinePt = -hanging;
            }

            _styles[style] = props;
        }

        TocMaxLevel = hiddenFrom - 1;
    }

    private DocxStyleProps ApplyTocRules(string styleId, List<CssDeclaration> declarations)
    {
        var props = _styles.TryGetValue(styleId, out var current) ? current.Clone() : new DocxStyleProps();
        if (declarations.Count == 0)
        {
            return props;
        }

        var box = new CssBox(props);
        var def = DocxStyleCatalog.Get(styleId);
        var baseSize = def?.BasedOn is { } parent ? FontSizeOf(parent) : FontSizeOf(DocxStyleCatalog.Normal);
        CssApplier.Apply(box, declarations.Where(d => d.Property != "content").ToList(), baseSize, this, silent: true);
        box.ResolvePadding();
        return box.Props;
    }

    private static bool IsDisplayNone(IEnumerable<CssDeclaration> declarations) =>
        declarations.LastOrDefault(d => d.Property == "display")?.Value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) == true;

    private double? Dimension(IEnumerable<CssDeclaration> declarations, string property, double emPt) =>
        declarations.LastOrDefault(d => d.Property == property) is { } d ? CssApplier.Length(Substitute(d.Value), emPt) : null;

    /// <summary>Название оглавления из CSS: <c>content</c> у самого элемента или у его <c>::before</c>/<c>::after</c> (они дописываются вокруг своего текста — название по умолчанию).</summary>
    private string? TocContent(DitaNode title, IReadOnlyList<CssDeclaration> declarations)
    {
        string? own = null;
        if (declarations.LastOrDefault(d => d.Property == "content") is { } content)
        {
            own = ParseContent(Substitute(content.Value), title);
        }

        var before = PseudoContent(title, "before");
        var after = PseudoContent(title, "after");
        if (own is null && before is null && after is null)
        {
            return null;
        }

        // Без собственного content название заполняется позже подписью по умолчанию (маркер «{0}»).
        return (before ?? string.Empty) + (own ?? TocTitlePlaceholder) + (after ?? string.Empty);
    }

    /// <summary>Место названия по умолчанию в <see cref="TocTitleText"/>, если CSS дописывает текст до или после него.</summary>
    public const string TocTitlePlaceholder = "\u0001";
}
