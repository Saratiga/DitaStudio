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
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx;

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
public sealed partial class DocxPublisher
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

        var tree = MapTree.Build(_project, mapPath, node => PublishFilter.IsIncluded(node, options));
        var labels = Labels.For(options.Language ?? DocumentLanguage.ForPublication(_project.TryGetDocument(mapPath)));
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

            var frontPage = WithLayoutPage(styles.Page, layout);
            WriteFrontMatter(body, mainPart, title, date, labels, layout, tocEntries,
                frontPage.WidthPt - frontPage.LeftPt - frontPage.RightPt - layout.GutterMm * DocxPageSetup.MmToPt, warnings);

            var renderOptions = new DocxRenderOptions
            {
                Labels = labels,
                ShowDraftComments = options.ShowDraftComments,
                ImageRasterizer = options.ImageRasterizer,
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
                    warnings.Add(Loc.T("Core_CouldNotRead0", item.TargetPath));
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
                    unnumbered: TocRules.IsHiddenInMap(item.Node), pageBreakBefore: TopicPageBreak.Of(item.Node));
            }

            WriteIndex(body, labels, renderer.IndexTerms);

            var mainSection = BuildSectionProperties(mainPart, WithLayoutPage(styles.Page, layout), layout, styles.MarginBoxes, title, date, warnings);
            body.Append(mainSection);
            FinishPlacedSections(body, mainSection);

            warnings.AddRange(renderOptions.Warnings);
            if (styles.UnmatchedSelectors() is { Count: > 0 } unmatched)
            {
                warnings.Add(Loc.T("Core_CSSDOCXTheseSelectorsMatchedNo") +
                             string.Join("; ", unmatched.Take(5)) + (unmatched.Count > 5 ? Loc.T("Core_And0More", unmatched.Count - 5) : string.Empty) +
                             Loc.T("Core_SelectorsAreMatchedAgainstDITAElements"));
            }
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
}
