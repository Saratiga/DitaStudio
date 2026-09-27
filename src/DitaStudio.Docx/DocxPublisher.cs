using System.Globalization;
using System.Text.RegularExpressions;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

public sealed class DocxPublishResult
{
    public DocxPublishResult(string outputFile, IReadOnlyList<string> warnings)
    {
        OutputFile = outputFile;
        Warnings = warnings;
    }

    public string OutputFile { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Сборка карты в один родной документ Word (.docx) — параллельно однофайловой сборке HTML
/// (<see cref="HtmlPublisher"/>), но через <see cref="DocxRenderer"/> напрямую в OOXML: настоящее
/// оглавление (поле TOC), настоящие сноски Word (кладутся на свою страницу автоматически), CALS-
/// таблицы с объединением ячеек, разрыв страницы перед заголовком и перенос таблицы между
/// страницами — по тем же токенам outputclass, что и в HTML/PDF.
///
/// Оформление — именованные стили Word, которые строятся из пользовательского CSS проекта
/// (<see cref="DocxStyleSheet"/>); вёрстка, которую CSS не выражает (титул, оглавление, нумерация
/// заголовков, колонтитулы, переплёт), — из <see cref="DocxLayout"/> проекта.
/// </summary>
public sealed class DocxPublisher
{
    private const int HeadingNumId = 9000;
    private const int HeadingAbstractNumId = 1002;

    private readonly DitaProject _project;

    public DocxPublisher(DitaProject project)
    {
        _project = project;
    }

    /// <summary>Сборка с оформлением проекта: его CSS и настройками «Оформление DOCX».</summary>
    public DocxPublishResult Publish(string mapPath, PublishOptions options, string outputFile) =>
        Publish(mapPath, options, outputFile, styles: null, layout: null);

    /// <param name="styles">Оформление; null — из пользовательского CSS проекта.</param>
    /// <param name="layout">Вёрстка; null — из настроек проекта (.ditastudio-docx).</param>
    public DocxPublishResult Publish(string mapPath, PublishOptions options, string outputFile,
        DocxStyleSheet? styles, DocxLayout? layout)
    {
        var warnings = new List<string>();
        if (styles is null)
        {
            var css = _project.ReadCustomCss(out var cssWarning);
            if (cssWarning is not null)
            {
                warnings.Add(cssWarning);
            }

            styles = DocxStyleSheet.FromCss(css);
        }

        warnings.AddRange(styles.Warnings);

        if (layout is null)
        {
            layout = _project.DocxLayout;
            if (_project.DocxLayoutWarning is { } layoutWarning)
            {
                warnings.Add(layoutWarning);
            }
        }

        layout = layout.Clone();
        var culture = CultureFor(layout.Language);
        var date = DateTime.Now.ToString("d MMMM yyyy", culture);

        var tree = MapTree.Build(_project, mapPath);
        var labels = Labels.For(options.Language);
        var topics = tree.PublicationOrder.ToList();
        var bookmarks = AssignBookmarks(topics);
        var title = tree.Root.Title;

        if (File.Exists(outputFile))
        {
            File.Delete(outputFile);
        }

        using var wordDocument = WordprocessingDocument.Create(outputFile, WordprocessingDocumentType.Document);

        var mainPart = wordDocument.AddMainDocumentPart();
        var body = new W.Body();
        mainPart.Document = new W.Document(body);

        AddStyles(mainPart, styles, layout);
        var numberingPart = AddNumbering(mainPart, styles, layout);
        AddSettings(wordDocument, layout);
        SetDocumentProperties(wordDocument, title, layout);

        WriteFrontMatter(body, title, date, labels, layout);

        var renderOptions = new DocxRenderOptions
        {
            Labels = labels,
            ShowDraftComments = options.ShowDraftComments,
            Filter = node => PublishFilter.IsIncluded(node, options),
            TopicBookmark = (path, id) => bookmarks.TryGetValue(BookmarkKey(path, id), out var name) ? name : null,
            Styles = styles,
            NumberFiguresAndTables = layout.NumberFiguresAndTables
        };
        var renderer = new DocxRenderer(_project, mainPart, numberingPart, renderOptions);

        foreach (var item in topics)
        {
            var doc = _project.TryGetDocument(item.TargetPath!);
            if (doc is null)
            {
                warnings.Add($"Не удалось прочитать {item.TargetPath}");
                continue;
            }

            var expanded = RefResolver.ExpandConrefs(_project, doc);
            var topicNode = item.TargetTopicId is null
                ? expanded.Root
                : RefResolver.FindById(expanded.Root, item.TargetTopicId) ?? expanded.Root;

            var bookmarkName = bookmarks[BookmarkKey(item.TargetPath!, item.TargetTopicId)];
            renderOptions.CurrentKeyScope = item.KeyScopeChain;
            renderOptions.RelatedTopics = tree.RelatedLinks.TryGetValue(Path.GetFullPath(item.TargetPath!), out var related)
                ? related
                : null;
            renderer.RenderTopic(expanded, topicNode, body, Math.Clamp(item.Level, 1, 6), bookmarkName);
        }

        body.Append(BuildSectionProperties(mainPart, styles.Page, layout, title, date));

        warnings.AddRange(renderOptions.Warnings);
        mainPart.Document.Save();

        return new DocxPublishResult(outputFile, warnings);
    }

    private static CultureInfo CultureFor(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("ru-RU");
        }
    }

    // ============================================================ титул и оглавление

    private static void WriteFrontMatter(W.Body body, string title, string date, Labels labels, DocxLayout layout)
    {
        var any = false;
        if (layout.TitlePage)
        {
            body.Append(StyledParagraph(DocxStyleCatalog.Title, title));
            if (layout.Subtitle.Length > 0)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.Subtitle, layout.Subtitle));
            }

            if (layout.Author.Length > 0)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.TitleAuthor, layout.Author));
            }

            if (layout.TitlePageDate)
            {
                body.Append(StyledParagraph(DocxStyleCatalog.TitleAuthor, date));
            }

            any = true;
        }

        if (layout.TableOfContents)
        {
            if (any)
            {
                body.Append(PageBreakParagraph());
            }

            body.Append(StyledParagraph(DocxStyleCatalog.TocHeading, labels.Contents));
            body.Append(BuildTocParagraph(layout.TocDepth));
            any = true;
        }

        // Если каждый топик верхнего уровня и так начинается с новой страницы, отдельный разрыв
        // дал бы пустую страницу.
        if (any && !layout.PageBreakBeforeTopLevel)
        {
            body.Append(PageBreakParagraph());
        }
    }

    private static W.Paragraph StyledParagraph(string styleId, string text) =>
        new(new W.ParagraphProperties(new W.ParagraphStyleId { Val = styleId }),
            new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static W.Paragraph PageBreakParagraph() =>
        new(new W.Run(new W.Break { Type = W.BreakValues.Page }));

    private static W.Paragraph BuildTocParagraph(int depth)
    {
        var field = new W.SimpleField(
            new W.Run(new W.Text("Обновите оглавление: F9 или правой кнопкой → «Обновить поле»")))
        {
            Instruction = $"TOC \\o \"1-{Math.Clamp(depth, 1, 6)}\" \\h \\z \\u"
        };

        return new W.Paragraph(field);
    }

    // ================================================================ колонтитулы

    private static W.SectionProperties BuildSectionProperties(MainDocumentPart mainPart, DocxPageSetup page,
        DocxLayout layout, string title, string date)
    {
        var section = new W.SectionProperties();
        var hasHeader = layout.HeaderText.Trim().Length > 0;
        var hasFooter = layout.FooterText.Trim().Length > 0;
        var distinctFirst = layout.NoHeaderOnFirstPage && (hasHeader || hasFooter);

        // Схема требует: сначала все headerReference, потом footerReference.
        if (hasHeader)
        {
            section.Append(new W.HeaderReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = AddHeader(mainPart, HeaderFooterParagraph(DocxStyleCatalog.PageHeader, layout.HeaderText, layout.HeaderAlignment, title, date))
            });
        }

        if (distinctFirst)
        {
            section.Append(new W.HeaderReference { Type = W.HeaderFooterValues.First, Id = AddHeader(mainPart, new W.Paragraph()) });
        }

        if (hasFooter)
        {
            section.Append(new W.FooterReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = AddFooter(mainPart, HeaderFooterParagraph(DocxStyleCatalog.PageFooter, layout.FooterText, layout.FooterAlignment, title, date))
            });
        }

        if (distinctFirst)
        {
            section.Append(new W.FooterReference { Type = W.HeaderFooterValues.First, Id = AddFooter(mainPart, new W.Paragraph()) });
        }

        var width = (uint)DocxPropsWriter.Twips(page.WidthPt);
        var height = (uint)DocxPropsWriter.Twips(page.HeightPt);
        var size = new W.PageSize { Width = width, Height = height };
        if (width > height)
        {
            size.Orient = W.PageOrientationValues.Landscape;
        }

        section.Append(size);
        section.Append(new W.PageMargin
        {
            Top = DocxPropsWriter.Twips(page.TopPt),
            Right = (uint)DocxPropsWriter.Twips(page.RightPt),
            Bottom = DocxPropsWriter.Twips(page.BottomPt),
            Left = (uint)DocxPropsWriter.Twips(page.LeftPt),
            Header = 709,
            Footer = 709,
            Gutter = (uint)DocxPropsWriter.Twips(layout.GutterMm * DocxPageSetup.MmToPt)
        });

        if (distinctFirst)
        {
            section.Append(new W.TitlePage());
        }

        return section;
    }

    private static string AddHeader(MainDocumentPart mainPart, W.Paragraph paragraph)
    {
        var part = mainPart.AddNewPart<HeaderPart>();
        part.Header = new W.Header(paragraph);
        return mainPart.GetIdOfPart(part);
    }

    private static string AddFooter(MainDocumentPart mainPart, W.Paragraph paragraph)
    {
        var part = mainPart.AddNewPart<FooterPart>();
        part.Footer = new W.Footer(paragraph);
        return mainPart.GetIdOfPart(part);
    }

    private static readonly Regex FieldPattern = new(@"\{(page|pages|title|date)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Абзац колонтитула: текст с полями {page}, {pages} (поля Word PAGE/NUMPAGES —
    /// Word сам подставит номера), {title} и {date} (подставляются при сборке).</summary>
    public static W.Paragraph HeaderFooterParagraph(string styleId, string text, DocxHeaderAlignment alignment, string title, string date)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = styleId },
            new W.Justification
            {
                Val = alignment switch
                {
                    DocxHeaderAlignment.Left => W.JustificationValues.Left,
                    DocxHeaderAlignment.Center => W.JustificationValues.Center,
                    _ => W.JustificationValues.Right
                }
            }));

        var position = 0;
        foreach (Match match in FieldPattern.Matches(text))
        {
            if (match.Index > position)
            {
                paragraph.Append(TextRun(text[position..match.Index]));
            }

            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "page":
                    paragraph.Append(new W.SimpleField(TextRun("1")) { Instruction = " PAGE " });
                    break;
                case "pages":
                    paragraph.Append(new W.SimpleField(TextRun("1")) { Instruction = " NUMPAGES " });
                    break;
                case "title":
                    paragraph.Append(TextRun(title));
                    break;
                default:
                    paragraph.Append(TextRun(date));
                    break;
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            paragraph.Append(TextRun(text[position..]));
        }

        return paragraph;
    }

    private static W.Run TextRun(string text) => new(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });

    // ================================================================ закладки

    private static string BookmarkKey(string path, string? topicId) => Path.GetFullPath(path) + "#" + (topicId ?? string.Empty);

    /// <summary>Строит закладку на каждый топик публикации. Ссылки внутри проекта нередко
    /// указывают на топик через его СОБСТВЕННЫЙ id (например, "install.dita#install") даже когда
    /// файл однотопиковый и MapItem.TargetTopicId для него не задан (не нужен для разрешения
    /// неоднозначности) — поэтому каждый топик регистрируется под обоими ключами: с
    /// TargetTopicId из карты и (если отличается) с id корневого элемента документа.</summary>
    private Dictionary<string, string> AssignBookmarks(IEnumerable<MapItem> topics)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in topics)
        {
            if (item.TargetPath is null)
            {
                continue;
            }

            var key = BookmarkKey(item.TargetPath, item.TargetTopicId);
            if (result.ContainsKey(key))
            {
                continue;
            }

            var seed = DocxRenderer.SafeBookmarkName(
                Path.GetFileNameWithoutExtension(item.TargetPath) + (item.TargetTopicId is null ? string.Empty : "_" + item.TargetTopicId));
            var name = seed;
            var counter = 2;
            while (!used.Add(name))
            {
                name = seed + "_" + counter++;
            }

            result[key] = name;

            var rootId = _project.TryGetDocument(item.TargetPath)?.Root.GetAttribute("id");
            if (!string.IsNullOrEmpty(rootId))
            {
                var rootKey = BookmarkKey(item.TargetPath, rootId);
                result.TryAdd(rootKey, name);
            }
        }

        return result;
    }

    // ================================================================ нумерация

    private static NumberingDefinitionsPart AddNumbering(MainDocumentPart mainPart, DocxStyleSheet styles, DocxLayout layout)
    {
        var part = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new W.Numbering();

        // Маркеры и цвет маркеров — из CSS (ul, ul ul, li::marker…), по умолчанию disc/circle/square.
        numbering.Append(DocxNumbering.Bullet(DocxRenderer.BulletAbstractNumId, styles.BulletMarker, styles.BulletColor));
        numbering.Append(DocxNumbering.Decimal(DocxRenderer.DecimalAbstractNumId, styles.OrderedColor));
        if (layout.NumberHeadings)
        {
            // все abstractNum должны идти раньше любого num
            numbering.Append(HeadingAbstractNum(layout.NumberingDepth));
            numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = HeadingAbstractNumId }) { NumberID = HeadingNumId });
        }

        part.Numbering = numbering;
        return part;
    }

    /// <summary>Многоуровневая нумерация заголовков «1», «1.1», «1.1.1» (как в ГОСТ 2.105),
    /// привязанная к стилям Heading1…HeadingN — номера видны и в оглавлении.</summary>
    private static W.AbstractNum HeadingAbstractNum(int depth)
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = HeadingAbstractNumId };
        abstractNum.Append(new W.MultiLevelType { Val = W.MultiLevelValues.Multilevel });
        for (var i = 0; i < 9; i++)
        {
            var numbered = i < depth;
            var text = string.Join(".", Enumerable.Range(1, i + 1).Select(n => "%" + n));
            var level = new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = numbered ? W.NumberFormatValues.Decimal : W.NumberFormatValues.None },
                LevelSuffix = new W.LevelSuffix { Val = W.LevelSuffixValues.Space },
                LevelText = new W.LevelText { Val = numbered ? text : string.Empty },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(new W.Indentation { Left = "0", FirstLine = "0" })
            };

            if (numbered && i < 6)
            {
                level.ParagraphStyleIdInLevel = new W.ParagraphStyleIdInLevel { Val = DocxStyleCatalog.Heading(i + 1) };
            }

            abstractNum.Append(level);
        }

        return abstractNum;
    }

    // ============================================================ стили и настройки

    private static void AddSettings(WordprocessingDocument document, DocxLayout layout)
    {
        var settingsPart = document.MainDocumentPart!.AddNewPart<DocumentSettingsPart>();
        var settings = new W.Settings();

        // Порядок элементов settings задан схемой: mirrorMargins … autoHyphenation … updateFields.
        if (layout.MirrorMargins)
        {
            settings.Append(new W.MirrorMargins());
        }

        if (layout.AutoHyphenation)
        {
            settings.Append(new W.AutoHyphenation());
        }

        settings.Append(new W.UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings = settings;
    }

    private static void SetDocumentProperties(WordprocessingDocument document, string title, DocxLayout layout)
    {
        var properties = document.PackageProperties;
        properties.Title = title;
        properties.Language = layout.Language;
        if (layout.Author.Length > 0)
        {
            properties.Creator = layout.Author;
        }
    }

    private static void AddStyles(MainDocumentPart mainPart, DocxStyleSheet sheet, DocxLayout layout)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles();

        styles.Append(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts
                {
                    Ascii = DocxDefaults.FontFamily, HighAnsi = DocxDefaults.FontFamily,
                    ComplexScript = DocxDefaults.FontFamily, EastAsia = DocxDefaults.FontFamily
                },
                new W.FontSize { Val = ((int)(DocxDefaults.FontSizePt * 2)).ToString() },
                new W.FontSizeComplexScript { Val = ((int)(DocxDefaults.FontSizePt * 2)).ToString() },
                new W.Languages { Val = layout.Language }))));

        foreach (var def in DocxStyleCatalog.Styles)
        {
            var props = sheet.Styles.TryGetValue(def.Id, out var resolved) ? resolved.Clone() : def.Defaults.Clone();
            W.NumberingProperties? numbering = null;
            var isHeading = def.OutlineLevel is not null;

            if (isHeading)
            {
                var level = def.OutlineLevel!.Value + 1;
                if (level == 1 && layout.PageBreakBeforeTopLevel)
                {
                    props.PageBreakBefore = true;
                }

                if (layout.NumberHeadings && level <= layout.NumberingDepth)
                {
                    numbering = new W.NumberingProperties(
                        new W.NumberingLevelReference { Val = level - 1 },
                        new W.NumberingId { Val = HeadingNumId });
                }
            }

            var primary = isHeading || def.Id is DocxStyleCatalog.Normal or DocxStyleCatalog.Title or DocxStyleCatalog.Subtitle;
            styles.Append(DocxStyleWriter.Create(def.Id, def.Name, def.Kind, def.BasedOn, props,
                primary, next: isHeading ? DocxStyleCatalog.Normal : null, numbering, def.OutlineLevel));
        }

        stylesPart.Styles = styles;
    }
}
