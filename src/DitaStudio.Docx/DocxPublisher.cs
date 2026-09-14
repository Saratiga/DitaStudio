using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
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
/// </summary>
public sealed class DocxPublisher
{
    private readonly DitaProject _project;

    public DocxPublisher(DitaProject project)
    {
        _project = project;
    }

    public DocxPublishResult Publish(string mapPath, PublishOptions options, string outputFile)
    {
        var tree = MapTree.Build(_project, mapPath);
        var labels = Labels.For(options.Language);
        var topics = tree.PublicationOrder.ToList();
        var bookmarks = AssignBookmarks(topics);

        if (File.Exists(outputFile))
        {
            File.Delete(outputFile);
        }

        using var wordDocument = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            outputFile, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);

        var mainPart = wordDocument.AddMainDocumentPart();
        var body = new W.Body();
        mainPart.Document = new W.Document(body);

        AddStyles(mainPart);
        var numberingPart = AddNumbering(mainPart);
        AddSettings(wordDocument);

        body.Append(new W.Paragraph(
            new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Title" }),
            new W.Run(new W.Text(tree.Root.Title))));
        body.Append(new W.Paragraph());
        body.Append(BuildTocParagraph(labels));
        body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));

        var renderOptions = new DocxRenderOptions
        {
            Labels = labels,
            ShowDraftComments = options.ShowDraftComments,
            Filter = node => IsIncluded(node, options),
            TopicBookmark = (path, id) => bookmarks.TryGetValue(BookmarkKey(path, id), out var name) ? name : null
        };
        var renderer = new DocxRenderer(_project, mainPart, numberingPart, renderOptions);
        var warnings = new List<string>();

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

        body.Append(new W.SectionProperties(
            new W.PageSize { Width = 11906, Height = 16838 },
            new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 709, Footer = 709 }));

        warnings.AddRange(renderOptions.Warnings);
        mainPart.Document.Save();

        return new DocxPublishResult(outputFile, warnings);
    }

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

    /// <summary>Условная фильтрация по props/platform/product/audience/otherprops — как в HtmlPublisher.</summary>
    private static bool IsIncluded(DitaNode node, PublishOptions options)
    {
        if (options.ExcludeConditions.Count == 0)
        {
            return true;
        }

        foreach (var (attribute, excluded) in options.ExcludeConditions)
        {
            var value = node.GetAttribute(attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var tokens = value!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens.All(excluded.Contains))
            {
                return false;
            }
        }

        return true;
    }

    private static W.Paragraph BuildTocParagraph(Labels labels)
    {
        var field = new W.SimpleField(
            new W.Run(new W.Text(labels.Contents + " — " +
                "обновите поле оглавления (F9 или правой кнопкой → «Обновить поле»)")))
        {
            Instruction = "TOC \\o \"1-6\" \\h \\z \\u"
        };

        return new W.Paragraph(field);
    }

    private static NumberingDefinitionsPart AddNumbering(MainDocumentPart mainPart)
    {
        var part = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new W.Numbering();

        numbering.Append(BulletAbstractNum());
        numbering.Append(DecimalAbstractNum());

        part.Numbering = numbering;
        return part;
    }

    private static W.AbstractNum BulletAbstractNum()
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = 1000 };
        string[] chars = { "", "o", "" };
        string[] fonts = { "Symbol", "Courier New", "Wingdings" };
        for (var i = 0; i < 9; i++)
        {
            abstractNum.Append(new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = W.NumberFormatValues.Bullet },
                LevelText = new W.LevelText { Val = chars[i % chars.Length] },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(
                    new W.Indentation { Left = ((i + 1) * 360).ToString(), Hanging = "360" }),
                NumberingSymbolRunProperties = new W.NumberingSymbolRunProperties(new W.RunFonts { Ascii = fonts[i % fonts.Length], HighAnsi = fonts[i % fonts.Length] })
            });
        }

        return abstractNum;
    }

    private static W.AbstractNum DecimalAbstractNum()
    {
        var abstractNum = new W.AbstractNum { AbstractNumberId = 1001 };
        string[] formats = { "%1.", "%1.%2.", "%1.%2.%3." };
        for (var i = 0; i < 9; i++)
        {
            abstractNum.Append(new W.Level
            {
                LevelIndex = i,
                StartNumberingValue = new W.StartNumberingValue { Val = 1 },
                NumberingFormat = new W.NumberingFormat { Val = W.NumberFormatValues.Decimal },
                LevelText = new W.LevelText { Val = formats[Math.Min(i, formats.Length - 1)] },
                LevelJustification = new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                PreviousParagraphProperties = new W.PreviousParagraphProperties(
                    new W.Indentation { Left = ((i + 1) * 360).ToString(), Hanging = "360" })
            });
        }

        return abstractNum;
    }

    private static void AddSettings(DocumentFormat.OpenXml.Packaging.WordprocessingDocument document)
    {
        var settingsPart = document.MainDocumentPart!.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new W.Settings(new W.UpdateFieldsOnOpen { Val = true });
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new W.Styles();

        styles.Append(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                new W.FontSize { Val = "22" }))));

        styles.Append(Style("Normal", "Обычный", null, 22, false));

        styles.Append(Style("Title", "Заголовок", null, 56, true, spacingAfter: "240"));

        var headingSizes = new[] { 36, 32, 28, 24, 22, 22 };
        for (var i = 1; i <= 6; i++)
        {
            styles.Append(HeadingStyle(i, headingSizes[i - 1]));
        }

        styles.Append(Style("FootnoteText", "Текст сноски", "Normal", 18, false));
        styles.Append(RunStyle("FootnoteReference", "Знак сноски", superscript: true));

        stylesPart.Styles = styles;
    }

    private static W.Style Style(string id, string name, string? basedOn, int size, bool bold, string? spacingAfter = null)
    {
        var runProps = new W.RunProperties(new W.FontSize { Val = size.ToString() });
        if (bold)
        {
            runProps.Append(new W.Bold());
        }

        var paragraphProps = new W.StyleParagraphProperties();
        if (spacingAfter is not null)
        {
            paragraphProps.Append(new W.SpacingBetweenLines { After = spacingAfter });
        }

        var style = new W.Style
        {
            Type = W.StyleValues.Paragraph,
            StyleId = id,
            StyleName = new W.StyleName { Val = name },
            PrimaryStyle = new W.PrimaryStyle()
        };

        if (basedOn is not null)
        {
            style.Append(new W.BasedOn { Val = basedOn });
        }

        if (paragraphProps.HasChildren)
        {
            style.Append(paragraphProps);
        }

        style.Append(runProps);
        return style;
    }

    private static W.Style HeadingStyle(int level, int size)
    {
        var style = new W.Style
        {
            Type = W.StyleValues.Paragraph,
            StyleId = "Heading" + level,
            StyleName = new W.StyleName { Val = "heading " + level },
            BasedOn = new W.BasedOn { Val = "Normal" },
            NextParagraphStyle = new W.NextParagraphStyle { Val = "Normal" },
            PrimaryStyle = new W.PrimaryStyle(),
            StyleParagraphProperties = new W.StyleParagraphProperties(
                new W.KeepNext(),
                new W.SpacingBetweenLines { Before = "240", After = "120" },
                new W.OutlineLevel { Val = level - 1 }),
            StyleRunProperties = new W.StyleRunProperties(new W.Bold(), new W.FontSize { Val = size.ToString() })
        };

        return style;
    }

    private static W.Style RunStyle(string id, string name, bool superscript)
    {
        var runProps = new W.StyleRunProperties();
        if (superscript)
        {
            runProps.Append(new W.VerticalTextAlignment { Val = W.VerticalPositionValues.Superscript });
        }

        return new W.Style
        {
            Type = W.StyleValues.Character,
            StyleId = id,
            StyleName = new W.StyleName { Val = name },
            StyleRunProperties = runProps
        };
    }
}
