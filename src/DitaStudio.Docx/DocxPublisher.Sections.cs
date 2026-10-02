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

// Разделы страницы: размещение блоков, свойства раздела.
public sealed partial class DocxPublisher
{
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
        DocxLayout layout, PageMarginBoxes boxes, string title, string date, List<string> warnings)
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

        // Колонтитулы из CSS (@page { @top-left {…} }) перекрывают диалог: верх — при любом верхнем поле, низ — при нижнем.
        var headerImage = boxes.HasTop ? null : Image(layout.HeaderImage);
        var footerImage = boxes.HasBottom ? null : Image(layout.FooterImage);
        var textWidthPt = page.WidthPt - page.LeftPt - page.RightPt - layout.GutterMm * DocxPageSetup.MmToPt;
        var hasHeader = boxes.HasTop || layout.HeaderText.Trim().Length > 0 || headerImage is not null;
        var hasFooter = boxes.HasBottom || layout.FooterText.Trim().Length > 0 || footerImage is not null;
        var distinctFirst = layout.NoHeaderOnFirstPage && (hasHeader || hasFooter);

        // Отступ колонтитула от края листа: из CSS (margin-top / margin-bottom поля) или 12,5 мм, но не больше половины поля.
        var topTwips = DocxPropsWriter.Twips(page.TopPt);
        var bottomTwips = DocxPropsWriter.Twips(page.BottomPt);
        var headerDistance = boxes.Top.FirstOrDefault(b => b.EdgeDistanceMm is not null)?.EdgeDistanceMm is { } topMm
            ? Math.Min((int)Math.Round(topMm * 1440 / 25.4), Math.Max(0, topTwips - 200)) : DocxHeaderFooter.DefaultDistanceTwips(topTwips);
        var footerDistance = boxes.Bottom.FirstOrDefault(b => b.EdgeDistanceMm is not null)?.EdgeDistanceMm is { } bottomMm
            ? Math.Min((int)Math.Round(bottomMm * 1440 / 25.4), Math.Max(0, bottomTwips - 200)) : DocxHeaderFooter.DefaultDistanceTwips(bottomTwips);
        var headerArea = DocxHeaderFooter.AreaHeightMm(topTwips, headerDistance);
        var footerArea = DocxHeaderFooter.AreaHeightMm(bottomTwips, footerDistance);
        var headerImageHeight = layout.FitHeaderFooterImages ? headerArea : layout.HeaderImageHeightMm;
        var footerImageHeight = layout.FitHeaderFooterImages ? footerArea : layout.FooterImageHeightMm;

        // Схема требует: сначала все headerReference, потом footerReference.
        if (hasHeader)
        {
            section.Append(new W.HeaderReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = AddHeader(mainPart, part => boxes.HasTop
                    ? DocxHeaderFooter.FromBoxes(part, DocxStyleCatalog.PageHeader, boxes.Top, _project.RootPath, title, textWidthPt, headerArea, 9001, warnings)
                    : HeaderFooterBlock(part, DocxStyleCatalog.PageHeader, layout.HeaderText, layout.HeaderAlignment,
                        headerImage, layout.HeaderImageAlignment, headerImageHeight, textWidthPt, title, date, 9001))
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
                Id = AddFooter(mainPart, part => boxes.HasBottom
                    ? DocxHeaderFooter.FromBoxes(part, DocxStyleCatalog.PageFooter, boxes.Bottom, _project.RootPath, title, textWidthPt, footerArea, 9002, warnings)
                    : HeaderFooterBlock(part, DocxStyleCatalog.PageFooter, layout.FooterText, layout.FooterAlignment,
                        footerImage, layout.FooterImageAlignment, footerImageHeight, textWidthPt, title, date, 9002))
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
            Header = (uint)headerDistance,
            Footer = (uint)footerDistance,
            Gutter = (uint)DocxPropsWriter.Twips(layout.GutterMm * DocxPageSetup.MmToPt)
        });

        if (distinctFirst)
        {
            section.Append(new W.TitlePage());
        }

        return section;
    }
}
