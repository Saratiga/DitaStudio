using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// DOCX: фразовые элементы, ссылки, сноски, изображения в тексте.
public sealed partial class DocxRenderer
{
    private List<OpenXmlElement> RenderInlineRuns(DitaNode node)
    {
        var runs = new List<OpenXmlElement>();
        foreach (var child in node.Children)
        {
            runs.AddRange(RenderInlineRunsForNode(child));
        }

        return runs;
    }

    private IEnumerable<OpenXmlElement> RenderInlineRunsForNode(DitaNode node) =>
        node.Kind == NodeKind.Element
            ? WithInlineClasses(node, () => RenderInlineRunsForNodeCore(node))
            : RenderInlineRunsForNodeCore(node);

    private IEnumerable<OpenXmlElement> RenderInlineRunsForNodeCore(DitaNode node)
    {
        switch (node.Kind)
        {
            case NodeKind.Text:
                yield return new W.Run(new W.Text(CollapseSpaces(node.Value)) { Space = SpaceProcessingModeValues.Preserve });
                yield break;
            case NodeKind.Comment:
            case NodeKind.ProcessingInstruction:
                yield break;
        }

        if (!Include(node))
        {
            yield break;
        }

        switch (node.Name)
        {
            case "b":
                foreach (var r in Styled(node, bold: true)) yield return r;
                yield break;
            case "i":
                foreach (var r in Styled(node, italic: true)) yield return r;
                yield break;
            case "u":
                foreach (var r in Styled(node, underline: true)) yield return r;
                yield break;
            case "sup":
                foreach (var r in Styled(node, vertical: W.VerticalPositionValues.Superscript)) yield return r;
                yield break;
            case "sub":
                foreach (var r in Styled(node, vertical: W.VerticalPositionValues.Subscript)) yield return r;
                yield break;
            case "line-through":
                foreach (var r in Styled(node, strike: true)) yield return r;
                yield break;
            case "codeph":
            case "apiname":
            case "option":
            case "parmname":
            case "synph":
            case "cmdname":
            case "filepath":
            case "msgnum":
            case "msgph":
            case "systemoutput":
            case "userinput":
            case "varname":
            case "coderef":
            case "shortcut":
                foreach (var r in Styled(node, runStyle: DocxStyleCatalog.CodeChar)) yield return r;
                yield break;
            case "uicontrol":
            case "wintitle":
                foreach (var r in Styled(node, runStyle: DocxStyleCatalog.UiControl)) yield return r;
                yield break;
            case "term" when node.Children.Count > 0:
                foreach (var r in Styled(node, runStyle: DocxStyleCatalog.Term)) yield return r;
                yield break;
            case "menucascade":
            {
                var parts = node.ElementChildren().Where(c => c.Name == "uicontrol").ToList();
                for (var i = 0; i < parts.Count; i++)
                {
                    if (i > 0)
                    {
                        yield return new W.Run(new W.Text(" → ") { Space = SpaceProcessingModeValues.Preserve });
                    }

                    foreach (var r in Styled(parts[i], runStyle: DocxStyleCatalog.UiControl))
                    {
                        yield return r;
                    }
                }

                yield break;
            }
            case "q":
                yield return new W.Run(new W.Text("«"));
                foreach (var r in RenderInlineRunsForChildren(node)) yield return r;
                yield return new W.Run(new W.Text("»"));
                yield break;
            case "image":
            case "glossSymbol":
            case "hazardsymbol":
                foreach (var r in RenderImage(node)) yield return r;
                yield break;
            case "xref":
            case "link":
                foreach (var r in RenderXref(node)) yield return r;
                yield break;
            case "fn":
                yield return RenderFootnote(node);
                yield break;
            case "indexterm":
            case "index-see":
            case "index-see-also":
            case "sort-as":
            case "draft-comment" when !_options.ShowDraftComments:
                yield break;
            // "keyword"/"term"/"text" не перечислены явно — совпадают с default
            // (KeyTextFor + рекурсия по детям), отдельная ветка была байт-в-байт
            // дублем default и ничего не меняла.
            default:
            {
                var keyText = KeyTextFor(node);
                if (keyText is not null && node.Children.Count == 0)
                {
                    yield return new W.Run(new W.Text(keyText) { Space = SpaceProcessingModeValues.Preserve });
                    yield break;
                }

                foreach (var r in RenderInlineRunsForChildren(node)) yield return r;
                yield break;
            }
        }
    }

    private IEnumerable<OpenXmlElement> RenderInlineRunsForChildren(DitaNode node)
    {
        foreach (var child in node.Children)
        {
            foreach (var r in RenderInlineRunsForNode(child))
            {
                yield return r;
            }
        }
    }

    private IEnumerable<OpenXmlElement> Styled(DitaNode node, bool bold = false, bool italic = false,
        bool underline = false, bool strike = false, string? runStyle = null,
        W.VerticalPositionValues? vertical = null)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                // порядок элементов rPr задан схемой OOXML
                var props = new List<OpenXmlElement>();
                if (runStyle is not null) props.Add(new W.RunStyle { Val = runStyle });
                if (bold) props.Add(new W.Bold());
                if (italic) props.Add(new W.Italic());
                if (strike) props.Add(new W.Strike());
                if (underline) props.Add(new W.Underline { Val = W.UnderlineValues.Single });
                if (vertical is not null) props.Add(new W.VerticalTextAlignment { Val = vertical.Value });

                yield return props.Count > 0
                    ? new W.Run(new W.RunProperties(props.ToArray()), new W.Text(CollapseSpaces(child.Value)) { Space = SpaceProcessingModeValues.Preserve })
                    : new W.Run(new W.Text(CollapseSpaces(child.Value)) { Space = SpaceProcessingModeValues.Preserve });
            }
            else if (child.Kind == NodeKind.Element)
            {
                foreach (var r in RenderInlineRunsForNode(child))
                {
                    yield return r;
                }
            }
        }
    }

    private string? KeyTextFor(DitaNode node)
    {
        var keyref = node.GetAttribute("keyref");
        if (string.IsNullOrWhiteSpace(keyref))
        {
            return null;
        }

        return _project.ResolveKey(keyref!.Split('/')[0], _options.CurrentKeyScope)?.KeyText;
    }

    private IEnumerable<OpenXmlElement> RenderXref(DitaNode node)
    {
        var href = node.GetAttribute("href");
        var keyref = node.GetAttribute("keyref");
        string? bookmark = null;
        string? external = null;
        string? label = null;

        if (!string.IsNullOrWhiteSpace(keyref))
        {
            var keyDef = _project.ResolveKey(keyref!.Split('/')[0], _options.CurrentKeyScope);
            if (keyDef is not null)
            {
                label = keyDef.KeyText;
                if (keyDef.ResolvedPath is not null)
                {
                    bookmark = _options.TopicBookmark?.Invoke(keyDef.ResolvedPath, null);
                }
                else if (keyDef.Href is not null)
                {
                    external = keyDef.Href;
                }
            }
        }

        if (bookmark is null && external is null && !string.IsNullOrWhiteSpace(href))
        {
            if (RefResolver.IsExternal(href!) || node.GetAttribute("scope") is "external" or "peer")
            {
                external = href;
            }
            else if (_document.FilePath is not null)
            {
                var reference = RefResolver.Parse(_document.FilePath, href!);
                if (reference.Path is not null)
                {
                    bookmark = _options.TopicBookmark?.Invoke(reference.Path, reference.ElementId ?? reference.TopicId);
                    label ??= TitleOf(reference);
                }
            }
        }

        var innerRuns = RenderInlineRuns(node).ToList();
        var hasText = innerRuns.OfType<W.Run>().Any(r => r.GetFirstChild<W.Text>()?.Text.Length > 0);
        if (!hasText)
        {
            innerRuns = new List<OpenXmlElement> { new W.Run(new W.Text(label ?? external ?? href ?? string.Empty)) };
        }

        if (bookmark is not null)
        {
            yield return new W.Hyperlink(LinkRuns(innerRuns)) { Anchor = bookmark, History = true };
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(external))
        {
            var relId = "hlink" + _nextImageId++;
            _mainPart.AddHyperlinkRelationship(new Uri(external!, UriKind.RelativeOrAbsolute), true, relId);
            yield return new W.Hyperlink(LinkRuns(innerRuns)) { Id = relId, History = true };
            yield break;
        }

        foreach (var r in innerRuns)
        {
            yield return r;
        }
    }

    private string? TitleOf(DitaReference reference)
    {
        if (reference.Path is null || !File.Exists(reference.Path))
        {
            return null;
        }

        var doc = _project.TryGetDocument(reference.Path);
        if (doc is null)
        {
            return null;
        }

        if (reference.TopicId is null)
        {
            return doc.Title;
        }

        var topic = RefResolver.FindById(doc.Root, reference.TopicId);
        return topic?.FirstElement("title")?.InnerText.Trim() ?? doc.Title;
    }

    private IEnumerable<OpenXmlCompositeElement> RenderRelatedLinks(DitaNode node)
    {
        var links = node.DescendantsAndSelf()
            .Where(n => n.Kind == NodeKind.Element && n.Name == "link" && Include(n))
            .ToList();
        if (links.Count == 0)
        {
            yield break;
        }

        yield return Para(DocxStyleCatalog.RelatedLinksTitle, L.RelatedLinks);
        foreach (var link in links)
        {
            yield return Para(DocxStyleCatalog.RelatedLink, RenderXref(link).ToList());
        }
    }

    private W.Run RenderFootnote(DitaNode node)
    {
        var footnotesPart = _mainPart.FootnotesPart ?? _mainPart.AddNewPart<FootnotesPart>();
        EnsureFootnotesRoot(footnotesPart);

        var id = _nextFootnoteId++;
        var runs = RenderInlineRuns(node);
        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(new W.ParagraphStyleId { Val = DocxStyleCatalog.FootnoteText }),
            new W.Run(new W.RunProperties(new W.RunStyle { Val = "FootnoteReference" }), new W.FootnoteReferenceMark()),
            new W.Run(new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
        foreach (var run in runs)
        {
            paragraph.Append(run);
        }

        footnotesPart.Footnotes!.Append(new W.Footnote(paragraph) { Id = id });

        return new W.Run(new W.RunProperties(new W.RunStyle { Val = "FootnoteReference" }), new W.FootnoteReference { Id = id });
    }

    private static void EnsureFootnotesRoot(FootnotesPart part)
    {
        if (part.Footnotes is not null)
        {
            return;
        }

        part.Footnotes = new W.Footnotes(
            new W.Footnote(new W.Paragraph(new W.Run(new W.SeparatorMark()))) { Type = W.FootnoteEndnoteValues.Separator, Id = -1 },
            new W.Footnote(new W.Paragraph(new W.Run(new W.ContinuationSeparatorMark()))) { Type = W.FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
    }

    private IEnumerable<OpenXmlElement> RenderImage(DitaNode node)
    {
        var href = node.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            var keyDef = node.GetAttribute("keyref") is { } k ? _project.ResolveKey(k, _options.CurrentKeyScope) : null;
            href = keyDef?.Href;
        }

        if (string.IsNullOrWhiteSpace(href) || RefResolver.IsExternal(href!) || _document.FilePath is null)
        {
            yield break;
        }

        var absolute = RefResolver.ResolvePath(_document.FilePath, href!);
        if (absolute is null || !File.Exists(absolute))
        {
            _options.Warnings.Add($"Изображение не найдено: {href}");
            yield break;
        }

        var alt = node.GetAttribute("alt") ?? node.FirstElement("alt")?.InnerText ?? string.Empty;
        var extension = Path.GetExtension(absolute).ToLowerInvariant();
        var supported = extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff";

        if (!supported)
        {
            _options.Warnings.Add($"Формат изображения не поддерживается в DOCX (показан как ссылка): {href}");
            yield return new W.Run(new W.RunProperties(new W.Italic(), new W.Color { Val = "808080" }),
                new W.Text($"[изображение: {Path.GetFileName(absolute)}{(string.IsNullOrEmpty(alt) ? string.Empty : " — " + alt)}]"));
            yield break;
        }

        var imagePart = extension switch
        {
            ".png" => _mainPart.AddImagePart(ImagePartType.Png),
            ".jpg" or ".jpeg" => _mainPart.AddImagePart(ImagePartType.Jpeg),
            ".gif" => _mainPart.AddImagePart(ImagePartType.Gif),
            ".bmp" => _mainPart.AddImagePart(ImagePartType.Bmp),
            _ => _mainPart.AddImagePart(ImagePartType.Tiff)
        };
        using (var stream = File.OpenRead(absolute))
        {
            imagePart.FeedData(stream);
        }

        var relId = _mainPart.GetIdOfPart(imagePart);
        var (widthEmu, heightEmu) = ImageSize.ReadEmuSize(absolute, node.GetAttribute("width"), node.GetAttribute("height"));
        var imageId = _nextImageId++;

        // wp:inline обязан лежать внутри w:drawing — без этой обёртки OpenXml SDK молча не
        // записывает элемент в document.xml при сохранении (сам image part при этом создаётся и
        // остаётся в пакете «осиротевшим»): найдено этим же тестом (AdvancedRenderingTests),
        // раньше ни одно изображение в DOCX-экспорте фактически не появлялось.
        yield return new W.Run(new W.Drawing(new Drawing.Wordprocessing.Inline(
            new Drawing.Wordprocessing.Extent { Cx = widthEmu, Cy = heightEmu },
            new Drawing.Wordprocessing.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Drawing.Wordprocessing.DocProperties { Id = (uint)imageId, Name = "image" + imageId, Description = alt },
            new Drawing.Graphic(
                new Drawing.GraphicData(
                    new Pic.Picture(
                        new Pic.NonVisualPictureProperties(
                            new Pic.NonVisualDrawingProperties { Id = (uint)imageId, Name = Path.GetFileName(absolute) },
                            new Pic.NonVisualPictureDrawingProperties()),
                        new Pic.BlipFill(
                            new Drawing.Blip { Embed = relId },
                            new Drawing.Stretch(new Drawing.FillRectangle())),
                        new Pic.ShapeProperties(
                            new Drawing.Transform2D(
                                new Drawing.Offset { X = 0, Y = 0 },
                                new Drawing.Extents { Cx = widthEmu, Cy = heightEmu }),
                            new Drawing.PresetGeometry(new Drawing.AdjustValueList()) { Preset = Drawing.ShapeTypeValues.Rectangle }))
                ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0
        }));
    }
}
