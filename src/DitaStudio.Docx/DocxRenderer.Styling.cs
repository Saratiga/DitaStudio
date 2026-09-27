using DitaStudio.Core.Model;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// Оформление через именованные стили: абзацы получают стиль из DocxStyleCatalog, а если у
// элемента (или его предка) есть класс с правилом в пользовательском CSS — производный стиль
// «базовый + класс», который Word показывает в списке стилей как обычный.
public sealed partial class DocxRenderer
{
    private readonly List<ClassContext> _blockContexts = new();
    private readonly List<ClassContext> _inlineContexts = new();
    private readonly Dictionary<string, string> _derivedStyles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedStyleIds = new(StringComparer.Ordinal);
    private readonly HashSet<W.Run> _classStyledRuns = new(ReferenceEqualityComparer.Instance);

    private DocxStyleSheet Sheet => _options.Styles;

    private sealed record ClassContext(string Key, IReadOnlyList<string> Classes);

    private sealed class Scope : IDisposable
    {
        private readonly List<ClassContext>? _list;

        public Scope(List<ClassContext>? list)
        {
            _list = list;
        }

        public void Dispose() => _list?.RemoveAt(_list.Count - 1);
    }

    /// <summary>Классы элемента, для которых в CSS есть правило: значения outputclass и имя
    /// элемента (HTML-публикация ставит его классом, так что «.keyword {…}» работает и тут).</summary>
    private ClassContext? ContextFor(DitaNode node)
    {
        if (node.Kind != NodeKind.Element || Sheet.CustomClasses.Count == 0)
        {
            return null;
        }

        var classes = new List<string>();
        var tokens = (node.GetAttribute("outputclass") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Append(node.Name);
        foreach (var token in tokens)
        {
            if (Sheet.CustomClasses.Contains(token) && !classes.Contains(token))
            {
                classes.Add(token);
            }
        }

        if (classes.Count == 0 || Sheet.PropsForClasses(classes, Styling.DocxDefaults.FontSizePt) is null)
        {
            return null;
        }

        return new ClassContext(string.Join(".", classes), classes);
    }

    /// <summary>Пока область открыта, все абзацы получают производный стиль с классами узла.</summary>
    private IDisposable BlockScope(DitaNode node)
    {
        var context = ContextFor(node);
        if (context is null)
        {
            return new Scope(null);
        }

        _blockContexts.Add(context);
        return new Scope(_blockContexts);
    }

    private string ParagraphStyle(string baseStyle) =>
        _blockContexts.Count == 0 ? baseStyle : Derived(baseStyle, DocxStyleKind.Paragraph, _blockContexts);

    private string Derived(string? baseStyle, DocxStyleKind kind, IReadOnlyList<ClassContext> contexts)
    {
        var baseKey = baseStyle ?? string.Empty;
        var key = kind + "|" + baseKey + "|" + string.Join("|", contexts.Select(c => c.Key));
        if (_derivedStyles.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var baseSize = Sheet.FontSizeOf(string.IsNullOrEmpty(baseStyle) ? DocxStyleCatalog.Normal : baseStyle!);
        var props = new DocxStyleProps();
        foreach (var context in contexts)
        {
            if (Sheet.PropsForClasses(context.Classes, baseSize) is { } overlay)
            {
                props.Overlay(overlay);
            }
        }

        if (kind == DocxStyleKind.Character)
        {
            props = props.RunOnly();
        }

        if (props.IsEmpty)
        {
            _derivedStyles[key] = baseKey;
            return baseKey;
        }

        var seed = (baseKey.Length == 0 ? "Char" : baseKey) + "_" + string.Join("_", contexts.Select(c => c.Key));
        var id = SafeStyleId(seed);
        var unique = id;
        for (var n = 2; !_usedStyleIds.Add(unique) || DocxStyleCatalog.Get(unique) is not null; n++)
        {
            unique = id + n;
        }

        var baseName = DocxStyleCatalog.Get(baseKey)?.Name ?? (baseKey.Length == 0 ? "Текст" : baseKey);
        var name = baseName + " · " + string.Join(" · ", contexts.Select(c => c.Key));
        _mainPart.StyleDefinitionsPart?.Styles?.Append(
            DocxStyleWriter.Create(unique, name, kind, baseKey.Length == 0 ? null : baseKey, props));

        _derivedStyles[key] = unique;
        return unique;
    }

    private static string SafeStyleId(string seed)
    {
        var chars = seed.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var id = new string(chars);
        return id.Length > 60 ? id[..60] : id;
    }

    /// <summary>Абзац заданного стиля (с учётом открытых областей классов).</summary>
    private W.Paragraph Para(string styleId, IEnumerable<OpenXmlElement> runs)
    {
        var effective = ParagraphStyle(styleId);
        var paragraph = new W.Paragraph();
        if (effective != DocxStyleCatalog.Normal)
        {
            paragraph.Append(new W.ParagraphProperties(new W.ParagraphStyleId { Val = effective }));
        }

        paragraph.Append(runs);
        if (styleId != DocxStyleCatalog.CodeBlock)
        {
            TrimEdges(paragraph);
        }

        return paragraph;
    }

    /// <summary>Пробелы и переносы строк из исходного XML схлопываются в один пробел — как в HTML.</summary>
    private static string CollapseSpaces(string text) => WhitespaceRun.Replace(text, " ");

    private static readonly System.Text.RegularExpressions.Regex WhitespaceRun =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Пробел в начале и в конце абзаца (отступы исходного XML вокруг текста) не нужен.</summary>
    private static void TrimEdges(W.Paragraph paragraph)
    {
        var texts = paragraph.Descendants<W.Text>().ToList();
        var first = texts.FirstOrDefault(t => t.Text.Length > 0);
        if (first is not null)
        {
            first.Text = first.Text.TrimStart();
        }

        var last = texts.LastOrDefault(t => t.Text.Length > 0);
        if (last is not null)
        {
            last.Text = last.Text.TrimEnd();
        }
    }

    private W.Paragraph Para(string styleId, string text) =>
        Para(styleId, new OpenXmlElement[] { new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }) });

    /// <summary>Инлайн-элемент с классом: его знаки получают производный символьный стиль.
    /// Самый вложенный элемент назначает стиль первым (со всеми классами предков), внешние
    /// элементы оформляют только оставшийся собственный текст — так вложенный класс важнее.</summary>
    private IEnumerable<OpenXmlElement> WithInlineClasses(DitaNode node, Func<IEnumerable<OpenXmlElement>> render)
    {
        var context = ContextFor(node);
        if (context is null)
        {
            return render();
        }

        _inlineContexts.Add(context);
        List<OpenXmlElement> produced;
        List<ClassContext> active;
        try
        {
            produced = render().ToList();
            active = _inlineContexts.ToList();
        }
        finally
        {
            _inlineContexts.RemoveAt(_inlineContexts.Count - 1);
        }

        foreach (var run in produced.SelectMany(e => e is W.Run r ? new[] { r } : e.Descendants<W.Run>()))
        {
            if (!_classStyledRuns.Add(run))
            {
                continue;
            }

            var baseStyle = run.RunProperties?.RunStyle?.Val?.Value;
            var derived = Derived(baseStyle, DocxStyleKind.Character, active);
            if (derived.Length > 0 && derived != baseStyle)
            {
                SetRunStyle(run, derived);
            }
        }

        return produced;
    }

    private static void SetRunStyle(W.Run run, string styleId)
    {
        var properties = run.RunProperties ??= new W.RunProperties();
        properties.RunStyle = new W.RunStyle { Val = styleId };
    }

    /// <summary>Свойства таблицы: ширина, рамки и поля ячеек из CSS (table, th, td).</summary>
    private W.TableProperties TableProperties()
    {
        var look = Sheet.Table;
        var properties = new W.TableProperties(
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
            new W.TableBorders(
                DocxPropsWriter.Border(new W.TopBorder(), look.Outer),
                DocxPropsWriter.Border(new W.LeftBorder(), look.Outer),
                DocxPropsWriter.Border(new W.BottomBorder(), look.Outer),
                DocxPropsWriter.Border(new W.RightBorder(), look.Outer),
                DocxPropsWriter.Border(new W.InsideHorizontalBorder(), look.Inner),
                DocxPropsWriter.Border(new W.InsideVerticalBorder(), look.Inner)));

        if (look.CellPaddingPt is { } padding)
        {
            var twips = DocxPropsWriter.Twips(padding).ToString();
            properties.Append(new W.TableCellMarginDefault(
                new W.TopMargin { Width = twips, Type = W.TableWidthUnitValues.Dxa },
                new W.TableCellLeftMargin { Width = (short)DocxPropsWriter.Twips(padding), Type = W.TableWidthValues.Dxa },
                new W.BottomMargin { Width = twips, Type = W.TableWidthUnitValues.Dxa },
                new W.TableCellRightMargin { Width = (short)DocxPropsWriter.Twips(padding), Type = W.TableWidthValues.Dxa }));
        }

        return properties;
    }

    /// <summary>Ячейка простой таблицы (simpletable, properties, choicetable).</summary>
    private W.TableCell Cell(IEnumerable<OpenXmlElement> runs, bool isHeader)
    {
        var paragraph = Para(isHeader ? DocxStyleCatalog.TableHeading : DocxStyleCatalog.TableText, runs);
        return CellShading(isHeader) is { } shading
            ? new W.TableCell(new W.TableCellProperties(shading), paragraph)
            : new W.TableCell(paragraph);
    }

    /// <summary>Копии знаков для гиперссылки: знакам без своего стиля — стиль «Hyperlink».</summary>
    private static OpenXmlElement[] LinkRuns(IEnumerable<OpenXmlElement> runs)
    {
        var copies = runs.Select(r => r.CloneNode(true)).ToArray();
        foreach (var run in copies.SelectMany(e => e is W.Run r ? new[] { r } : e.Descendants<W.Run>()))
        {
            if (run.RunProperties?.RunStyle is null)
            {
                SetRunStyle(run, DocxStyleCatalog.Hyperlink);
            }
        }

        return copies;
    }

    private static W.Run HyperlinkRun(string text) =>
        new(new W.RunProperties(new W.RunStyle { Val = DocxStyleCatalog.Hyperlink }), new W.Text(text));

    /// <summary>Заливка ячейки: шапка или тело таблицы; null — без заливки.</summary>
    private W.Shading? CellShading(bool isHeader)
    {
        var fill = isHeader ? Sheet.Table.HeaderFill : Sheet.Table.BodyFill;
        return string.IsNullOrEmpty(fill) ? null : DocxPropsWriter.Shading(fill);
    }
}
