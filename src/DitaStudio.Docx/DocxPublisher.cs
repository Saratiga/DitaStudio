using System.Globalization;
using System.Text.RegularExpressions;
using DitaStudio.Core.IO;
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

            // Классы оформления из «Автора» — раньше CSS проекта, чтобы тот мог их переопределить.
            styles = DocxStyleSheet.FromCss(TextFormatting.Css + "\n" + css);
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
        var tocEntries = topics
            .Where(i => i.TargetPath is not null && Math.Clamp(i.Level, 1, 6) <= layout.TocDepth && TocRules.Includes(_project, i))
            .Select(i => new TocEntry(Math.Clamp(i.Level, 1, 6), i.Title, bookmarks[BookmarkKey(i.TargetPath!, i.TargetTopicId)]))
            .ToList();

        // Документ собирается во временный файл рядом и подменяет прежний, только когда готов
        // целиком: сбой посреди сборки не оставит вместо прошлого DOCX обрезанный.
        AtomicFile.WriteVia(outputFile, temp =>
        {
            using var wordDocument = WordprocessingDocument.Create(temp, WordprocessingDocumentType.Document);

            var mainPart = wordDocument.AddMainDocumentPart();
            var body = new W.Body();
            mainPart.Document = new W.Document(body);

            AddStyles(mainPart, styles, layout);
            var numberingPart = AddNumbering(mainPart, styles, layout);
            AddSettings(wordDocument, layout);
            SetDocumentProperties(wordDocument, title, layout);

            WriteFrontMatter(body, title, date, labels, layout, tocEntries);

            var renderOptions = new DocxRenderOptions
            {
                Labels = labels,
                ShowDraftComments = options.ShowDraftComments,
                Filter = node => PublishFilter.IsIncluded(node, options),
                TopicBookmark = (path, id) => bookmarks.TryGetValue(BookmarkKey(path, id), out var name) ? name : null,
                Styles = styles,
                NumberFiguresAndTables = layout.NumberFiguresAndTables,
                CaptionSeparator = layout.CaptionSeparator,
                HeadingNumId = layout.NumberHeadings ? HeadingNumId : null
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
                renderer.RenderTopic(expanded, topicNode, body, Math.Clamp(item.Level, 1, 6), bookmarkName,
                    unnumbered: TocRules.IsHiddenInMap(item.Node));
            }

            var mainSection = BuildSectionProperties(mainPart, WithLayoutPage(styles.Page, layout), layout, title, date, warnings);
            body.Append(mainSection);
            FinishPlacedSections(body, mainSection);

            warnings.AddRange(renderOptions.Warnings);
            mainPart.Document.Save();
        });

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

    /// <summary>Уровень структуры «основной текст» — абзац не попадает в оглавление Word.</summary>
    private const int BodyTextOutlineLevel = 9;

    /// <summary>Строка оглавления: уровень, текст, закладка топика.</summary>
    private sealed record TocEntry(int Level, string Text, string Bookmark);

    private static void WriteFrontMatter(W.Body body, string title, string date, Labels labels, DocxLayout layout,
        IReadOnlyList<TocEntry> tocEntries)
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

            body.Append(StyledParagraph(DocxStyleCatalog.TocHeading, layout.TocTitle.Length > 0 ? layout.TocTitle : labels.Contents));
            foreach (var paragraph in BuildTocParagraphs(layout.TocDepth, tocEntries))
            {
                body.Append(paragraph);
            }
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

    /// <summary>
    /// Поле TOC, заранее заполненное строками-ссылками на топики: оглавление видно сразу — в
    /// LibreOffice, просмотрщиках, в Word до обновления полей; Word при открытии пересобирает его
    /// с номерами страниц (поле помечено устаревшим, в настройках — обновление при открытии).
    /// </summary>
    private static IEnumerable<W.Paragraph> BuildTocParagraphs(int depth, IReadOnlyList<TocEntry> entries)
    {
        var instruction = $" TOC \\o \"1-{Math.Clamp(depth, 1, 6)}\" \\h \\z \\u ";
        if (entries.Count == 0)
        {
            yield return new W.Paragraph(new W.SimpleField(
                new W.Run(new W.Text("Обновите оглавление: F9 или правой кнопкой → «Обновить поле»")))
            {
                Instruction = instruction.Trim()
            });
            yield break;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = DocxStyleCatalog.Toc(entry.Level) }));
            if (i == 0)
            {
                paragraph.Append(
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin, Dirty = true }),
                    new W.Run(new W.FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }));
            }

            paragraph.Append(new W.Hyperlink(new W.Run(new W.Text(entry.Text) { Space = SpaceProcessingModeValues.Preserve }))
            {
                Anchor = entry.Bookmark,
                History = true
            });

            if (i == entries.Count - 1)
            {
                paragraph.Append(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
            }

            yield return paragraph;
        }
    }

    // ================================================================ колонтитулы

    /// <summary>Размер бумаги, ориентация и поля из параметров страницы проекта — поверх @page CSS.</summary>
    private static DocxPageSetup WithLayoutPage(DocxPageSetup page, DocxLayout layout)
    {
        var (width, height) = (page.WidthPt, page.HeightPt);
        if (layout.PaperMm() is { } paper)
        {
            (width, height) = (paper.Width * DocxPageSetup.MmToPt, paper.Height * DocxPageSetup.MmToPt);
        }

        if (layout.Landscape && width < height)
        {
            (width, height) = (height, width);
        }

        static double Pt(double? mm, double fallback) => mm is { } value ? value * DocxPageSetup.MmToPt : fallback;
        return new DocxPageSetup(width, height,
            Pt(layout.MarginTopMm, page.TopPt), Pt(layout.MarginRightMm, page.RightPt),
            Pt(layout.MarginBottomMm, page.BottomPt), Pt(layout.MarginLeftMm, page.LeftPt));
    }

    /// <summary>
    /// Разделы блоков «на отдельном листе» (<see cref="PagePlacement"/>): метки, которые оставил
    /// рендер, получают параметры страницы и колонтитулы основного раздела. Пустые разделы (блок в
    /// начале документа, два таких блока подряд, блок в конце) не создаются — иначе пустой лист.
    /// «Особый первый лист» (titlePg) остаётся только у первого раздела.
    /// </summary>
    internal static void FinishPlacedSections(W.Body body, W.SectionProperties mainSection)
    {
        static W.SectionProperties? Mark(OpenXmlElement? element) =>
            (element as W.Paragraph)?.ParagraphProperties?.SectionProperties;

        var marks = body.Elements<W.Paragraph>().Where(p => Mark(p) is not null).ToList();
        if (marks.Count == 0)
        {
            return;
        }

        foreach (var paragraph in marks)
        {
            var section = Mark(paragraph)!;
            if (section.HasChildren)
            {
                continue;
            }

            // Начало блока: разрыв ставится в конец предыдущего абзаца, а не отдельной строкой.
            var previous = paragraph.PreviousSibling();
            if (previous is null || Mark(previous) is not null)
            {
                paragraph.Remove();
            }
            else if (previous is W.Paragraph before)
            {
                section.Remove();
                (before.ParagraphProperties ??= new W.ParagraphProperties()).SectionProperties = section;
                paragraph.Remove();
            }
        }

        marks = body.Elements<W.Paragraph>().Where(p => Mark(p) is not null).ToList();
        var lastMark = marks.LastOrDefault();
        if (lastMark is not null && lastMark.NextSibling() is W.SectionProperties)
        {
            // Блок в самом конце: его выравнивание переходит к последнему (основному) разделу.
            var vertical = Mark(lastMark)!.GetFirstChild<W.VerticalTextAlignmentOnPage>();
            if (vertical is not null)
            {
                InsertVerticalAlignment(mainSection, (W.VerticalTextAlignmentOnPage)vertical.CloneNode(true));
            }

            if (lastMark.ChildElements.All(c => c is W.ParagraphProperties))
            {
                lastMark.Remove();
            }
            else
            {
                Mark(lastMark)!.Remove();
            }

            marks.RemoveAt(marks.Count - 1);
        }

        for (var i = 0; i < marks.Count; i++)
        {
            var mark = Mark(marks[i])!;
            var filled = (W.SectionProperties)mainSection.CloneNode(true);
            filled.RemoveAllChildren<W.VerticalTextAlignmentOnPage>();
            if (i > 0)
            {
                filled.RemoveAllChildren<W.TitlePage>();
            }

            if (mark.GetFirstChild<W.VerticalTextAlignmentOnPage>() is { } vertical)
            {
                InsertVerticalAlignment(filled, (W.VerticalTextAlignmentOnPage)vertical.CloneNode(true));
            }

            marks[i].ParagraphProperties!.SectionProperties = filled;
        }

        if (marks.Count > 0)
        {
            mainSection.RemoveAllChildren<W.TitlePage>();
        }
    }

    // В sectPr порядок строгий: vAlign — перед titlePg, после pgMar.
    private static void InsertVerticalAlignment(W.SectionProperties section, W.VerticalTextAlignmentOnPage vertical)
    {
        section.RemoveAllChildren<W.VerticalTextAlignmentOnPage>();
        if (section.GetFirstChild<W.TitlePage>() is { } titlePage)
        {
            section.InsertBefore(vertical, titlePage);
        }
        else
        {
            section.Append(vertical);
        }
    }

    private W.SectionProperties BuildSectionProperties(MainDocumentPart mainPart, DocxPageSetup page,
        DocxLayout layout, string title, string date, List<string> warnings)
    {
        var section = new W.SectionProperties();
        string? Image(string relative)
        {
            var full = DocxLayout.ResolveImage(_project.RootPath, relative);
            if (full is null && relative.Trim().Length > 0)
            {
                warnings.Add($"Картинка колонтитула не найдена или не PNG/JPEG/GIF/BMP: {relative}");
            }

            return full;
        }

        var headerImage = Image(layout.HeaderImage);
        var footerImage = Image(layout.FooterImage);
        var textWidthPt = page.WidthPt - page.LeftPt - page.RightPt - layout.GutterMm * DocxPageSetup.MmToPt;
        var hasHeader = layout.HeaderText.Trim().Length > 0 || headerImage is not null;
        var hasFooter = layout.FooterText.Trim().Length > 0 || footerImage is not null;
        var distinctFirst = layout.NoHeaderOnFirstPage && (hasHeader || hasFooter);

        // Схема требует: сначала все headerReference, потом footerReference.
        if (hasHeader)
        {
            section.Append(new W.HeaderReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = AddHeader(mainPart, part => HeaderFooterBlock(part, DocxStyleCatalog.PageHeader, layout.HeaderText, layout.HeaderAlignment,
                    headerImage, layout.HeaderImageAlignment, layout.HeaderImageHeightMm, textWidthPt, title, date, 9001))
            });
        }

        if (distinctFirst)
        {
            section.Append(new W.HeaderReference { Type = W.HeaderFooterValues.First, Id = AddHeader(mainPart, _ => new W.Paragraph()) });
        }

        if (hasFooter)
        {
            section.Append(new W.FooterReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = AddFooter(mainPart, part => HeaderFooterBlock(part, DocxStyleCatalog.PageFooter, layout.FooterText, layout.FooterAlignment,
                    footerImage, layout.FooterImageAlignment, layout.FooterImageHeightMm, textWidthPt, title, date, 9002))
            });
        }

        if (distinctFirst)
        {
            section.Append(new W.FooterReference { Type = W.HeaderFooterValues.First, Id = AddFooter(mainPart, _ => new W.Paragraph()) });
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

    // Абзац строится, когда часть колонтитула уже есть: картинка кладётся в неё самую.
    private static string AddHeader(MainDocumentPart mainPart, Func<OpenXmlPart, W.Paragraph> build)
    {
        var part = mainPart.AddNewPart<HeaderPart>();
        part.Header = new W.Header(build(part));
        return mainPart.GetIdOfPart(part);
    }

    private static string AddFooter(MainDocumentPart mainPart, Func<OpenXmlPart, W.Paragraph> build)
    {
        var part = mainPart.AddNewPart<FooterPart>();
        part.Footer = new W.Footer(build(part));
        return mainPart.GetIdOfPart(part);
    }

    /// <summary>
    /// Колонтитул с картинкой: три места — слева, по центру (табуляция по центру строки) и справа
    /// (табуляция по правому краю). Картинка и текст — каждый на своём месте, а при одинаковом
    /// выравнивании — рядом. Без картинки — прежний абзац с выравниванием текста.
    /// </summary>
    private static W.Paragraph HeaderFooterBlock(OpenXmlPart part, string styleId, string text, DocxHeaderAlignment textAlignment,
        string? image, DocxHeaderAlignment imageAlignment, double imageHeightMm, double textWidthPt, string title, string date, uint imageId)
    {
        var textParagraph = HeaderFooterParagraph(styleId, text, textAlignment, title, date);
        if (image is null || DocxPictures.AddImage(part, image) is not { } relId)
        {
            return textParagraph;
        }

        var (naturalWidth, naturalHeight) = ImageSize.ReadEmuSize(image, null, null);
        var heightEmu = (long)(imageHeightMm * 36000);
        var widthEmu = naturalHeight > 0 ? (long)(naturalWidth * (double)heightEmu / naturalHeight) : heightEmu;
        var picture = DocxPictures.Inline(relId, widthEmu, heightEmu, imageId, Path.GetFileName(image), "Логотип");
        var textRuns = textParagraph.ChildElements.Where(e => e is not W.ParagraphProperties).Select(e => e.CloneNode(true)).ToList();

        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = styleId },
            new W.Tabs(
                new W.TabStop { Val = W.TabStopValues.Center, Position = DocxPropsWriter.Twips(textWidthPt / 2) },
                new W.TabStop { Val = W.TabStopValues.Right, Position = DocxPropsWriter.Twips(textWidthPt) }),
            new W.Justification { Val = W.JustificationValues.Left }));

        foreach (var slot in new[] { DocxHeaderAlignment.Left, DocxHeaderAlignment.Center, DocxHeaderAlignment.Right })
        {
            if (slot != DocxHeaderAlignment.Left)
            {
                paragraph.Append(new W.Run(new W.TabChar()));
            }

            if (slot == imageAlignment)
            {
                paragraph.Append(picture);
                if (slot == textAlignment && textRuns.Count > 0)
                {
                    paragraph.Append(TextRun("  "));
                }
            }

            if (slot == textAlignment)
            {
                paragraph.Append(textRuns);
            }
        }

        return paragraph;
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
            // Все уровни десятичные: глубже настройки номера получают только нумерованные абзацы
            // (стили заголовков к этим уровням не привязаны).
            var numbered = true;
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

            if (i < depth && i < 6)
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

            // Заголовок «без номера»: как обычный, но без нумерации и без уровня структуры —
            // поэтому и поле TOC в Word его не соберёт.
            var plainHeading = def.Id.StartsWith(DocxStyleCatalog.HeadingPlainPrefix, StringComparison.Ordinal);
            if (plainHeading && layout.NumberHeadings)
            {
                numbering = new W.NumberingProperties(new W.NumberingId { Val = 0 });
            }

            var primary = isHeading || plainHeading || def.Id is DocxStyleCatalog.Normal or DocxStyleCatalog.Title or DocxStyleCatalog.Subtitle;
            styles.Append(DocxStyleWriter.Create(def.Id, def.Name, def.Kind, def.BasedOn, props,
                primary, next: isHeading || plainHeading ? DocxStyleCatalog.Normal : null, numbering,
                plainHeading ? BodyTextOutlineLevel : def.OutlineLevel));
        }

        stylesPart.Styles = styles;
    }
}
