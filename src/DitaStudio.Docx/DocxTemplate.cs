using DitaStudio.Core.Localization;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

/// <summary>
/// Шаблон DOCX (.docx или .dotx) как источник стилей: его определения стилей, значения по умолчанию и тема (шрифты и цвета) переходят в
/// публикуемый документ. Порядок слоёв: шаблон → стили, которых в нём нет (из каталога DITA Studio) → CSS проекта поверх (только то, что в CSS
/// задано явно). Колонтитулы, титул и параметры страницы шаблона не переносятся — они из «Оформление DOCX» и CSS; нумерация в стилях шаблона снимается,
/// чтобы не ссылаться на списки, которых в новом документе нет (нумерацию заголовков и абзацев ведёт сама публикация).
/// </summary>
internal sealed class DocxTemplate
{
    // Порядок дочерних элементов pPr и rPr в схеме OOXML — Word считает нарушение повреждением файла.
    private static readonly string[] ParagraphOrder =
    {
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd", "tabs",
        "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc", "textDirection", "textAlignment",
        "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr", "sectPr", "pPrChange"
    };

    private static readonly string[] RunOrder =
    {
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint", "noProof",
        "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd",
        "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath"
    };

    private DocxTemplate(W.Styles styles, byte[]? theme)
    {
        Styles = styles;
        Theme = theme;
    }

    /// <summary>Копия стилей шаблона: изменяется при сборке, сам файл шаблона не трогается.</summary>
    public W.Styles Styles { get; }

    /// <summary>Тема шаблона (<c>theme1.xml</c>) — на неё ссылаются шрифты и цвета стилей; null — в шаблоне темы нет.</summary>
    public byte[]? Theme { get; }

    /// <summary>Читает шаблон; null и предупреждение — файла нет, он не открылся (повреждён, защищён паролем) или в нём нет стилей.</summary>
    public static DocxTemplate? Load(string path, List<string> warnings)
    {
        try
        {
            using var document = WordprocessingDocument.Open(path, false);
            var part = document.MainDocumentPart?.StyleDefinitionsPart;
            if (part?.Styles is not { } source || !source.Elements<W.Style>().Any())
            {
                warnings.Add(Loc.T("Core_TheDOCXTemplate0HasNoStyles", Path.GetFileName(path)));
                return null;
            }

            var styles = (W.Styles)source.CloneNode(true);
            // Нумерация в стилях шаблона ссылается на его списки; в новом документе их нет.
            foreach (var numbering in styles.Descendants<W.NumberingProperties>().ToList())
            {
                numbering.Remove();
            }

            byte[]? theme = null;
            if (document.MainDocumentPart?.ThemePart is { } themePart)
            {
                using var stream = themePart.GetStream();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                theme = buffer.ToArray();
            }

            return new DocxTemplate(styles, theme);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenXmlPackageException or InvalidDataException or System.Xml.XmlException or FileFormatException)
        {
            warnings.Add(Loc.T("Core_TheDOCXTemplate0CouldNotBeOpened", Path.GetFileName(path), ex.Message));
            return null;
        }
    }

    /// <summary>Стили таблиц шаблона: id и название.</summary>
    public IReadOnlyList<(string Id, string Name)> TableStyles =>
        Styles.Elements<W.Style>().Where(s => s.Type?.Value == W.StyleValues.Table && s.StyleId?.Value is { Length: > 0 })
            .Select(s => (s.StyleId!.Value!, s.StyleName?.Val?.Value ?? s.StyleId!.Value!)).ToList();

    /// <summary>
    /// Стиль таблицы шаблона для таблицы с классами <paramref name="classes"/>: класс, совпадающий с id или названием стиля (без учёта регистра, пробелов,
    /// дефисов и подчёркиваний), иначе стиль по умолчанию <paramref name="defaultStyle"/> (id или название); null — стиль не назначается.
    /// </summary>
    public string? TableStyleFor(IEnumerable<string> classes, string? defaultStyle)
    {
        var styles = TableStyles;
        string? Find(string key)
        {
            var wanted = Normalize(key);
            return wanted.Length == 0 ? null : styles.FirstOrDefault(s => Normalize(s.Id) == wanted || Normalize(s.Name) == wanted).Id;
        }

        foreach (var token in classes)
        {
            if (Find(token) is { } byClass)
            {
                return byClass;
            }
        }

        return string.IsNullOrWhiteSpace(defaultStyle) ? null : Find(defaultStyle);
    }

    private static string Normalize(string value) =>
        new string(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '_').ToArray()).ToLowerInvariant();

    /// <summary>Стили таблиц файла шаблона — для списка в «Оформление DOCX»; пусто, если файла нет или он не открылся.</summary>
    public static IReadOnlyList<(string Id, string Name)> ReadTableStyles(string path) =>
        Load(path, new List<string>())?.TableStyles ?? Array.Empty<(string, string)>();

    /// <summary>Стиль шаблона с таким id (и тем же видом — абзац или знак); иначе null.</summary>
    public W.Style? Find(string id, DocxStyleKind kind)
    {
        var style = Styles.Elements<W.Style>().FirstOrDefault(s => s.StyleId?.Value == id);
        var expected = kind == DocxStyleKind.Paragraph ? W.StyleValues.Paragraph : W.StyleValues.Character;
        return style is not null && style.Type?.Value == expected ? style : null;
    }

    /// <summary>
    /// Накладывает на стиль шаблона то, что задал CSS (<paramref name="explicitProps"/>) и что определяет сама публикация (нумерация, уровень структуры):
    /// заданные свойства заменяют прежние, остальные остаются как в шаблоне.
    /// </summary>
    public static void Overlay(W.Style style, DocxStyleKind kind, DocxStyleProps explicitProps, W.NumberingProperties? numbering, int? outlineLevel)
    {
        if (kind == DocxStyleKind.Paragraph)
        {
            var paragraph = DocxPropsWriter.ParagraphElements(explicitProps, numbering, outlineLevel).ToList();
            if (paragraph.Count > 0)
            {
                style.StyleParagraphProperties ??= new W.StyleParagraphProperties();
                Merge(style.StyleParagraphProperties, paragraph, ParagraphOrder);
            }
        }

        var runSource = explicitProps;
        if (kind == DocxStyleKind.Paragraph && explicitProps.Background is not null)
        {
            runSource = explicitProps.Clone();
            runSource.Background = null; // у стиля абзаца заливка — заливка абзаца (она уже в pPr)
        }

        var run = DocxPropsWriter.RunElements(runSource).ToList();
        if (run.Count > 0)
        {
            style.StyleRunProperties ??= new W.StyleRunProperties();
            Merge(style.StyleRunProperties, run, RunOrder);
        }
    }

    private static void Merge(OpenXmlCompositeElement container, IReadOnlyList<OpenXmlElement> elements, string[] order)
    {
        foreach (var name in elements.Select(e => e.LocalName).Distinct().ToList())
        {
            foreach (var old in container.ChildElements.Where(c => c.LocalName == name).ToList())
            {
                old.Remove();
            }
        }

        var all = container.ChildElements.Select(c => c.CloneNode(true)).Concat(elements).ToList();
        container.RemoveAllChildren();
        foreach (var element in all.Select((e, i) => (e, i)).OrderBy(x => Position(x.e.LocalName, order)).ThenBy(x => x.i))
        {
            container.Append(element.e);
        }
    }

    private static int Position(string name, string[] order)
    {
        var index = Array.IndexOf(order, name);
        return index < 0 ? order.Length : index;
    }
}
