using DitaStudio.Core.Publishing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using DitaStudio.Core.Localization;

namespace DitaStudio.Docx;

/// <summary>
/// Колонтитулы DOCX: размер картинки под область колонтитула и колонтитул из полей страницы CSS
/// (<c>@page { @top-left { content: … } }</c>, см. <see cref="PageMarginBoxes"/>).
/// </summary>
internal static class DocxHeaderFooter
{
    private const double TwipsPerMm = 1440 / 25.4;
    private const double EmuPerMm = 36000;
    private const double EmuPerPt = 12700;

    /// <summary>Расстояние от края листа до колонтитула по умолчанию, twips: 12,5 мм, но не больше половины поля.</summary>
    public static int DefaultDistanceTwips(int marginTwips) => Math.Min(709, Math.Max(0, marginTwips / 2));

    /// <summary>
    /// Высота области колонтитула, мм: поле страницы минус расстояние от края и зазор до текста 1 мм; не меньше 3 мм.
    /// </summary>
    public static double AreaHeightMm(int marginTwips, int distanceTwips) =>
        Math.Max(3, (marginTwips - distanceTwips) / TwipsPerMm - 1);

    /// <summary>
    /// Размер картинки в EMU. <paramref name="heightMm"/> — нужная высота; ширина берётся по пропорциям, а если
    /// она шире текста (<paramref name="maxWidthPt"/>), картинка уменьшается вместе с высотой.
    /// </summary>
    public static (long Width, long Height) Fit(long naturalWidth, long naturalHeight, double heightMm, double maxWidthPt)
    {
        var height = heightMm * EmuPerMm;
        var width = naturalHeight > 0 && naturalWidth > 0 ? naturalWidth * height / naturalHeight : height;
        var maxWidth = maxWidthPt * EmuPerPt;
        if (maxWidth > 0 && width > maxWidth)
        {
            height *= maxWidth / width;
            width = maxWidth;
        }

        return ((long)Math.Max(1, width), (long)Math.Max(1, height));
    }

    /// <summary>
    /// Колонтитул из полей страницы CSS одним абзацем: три места (слева, по центру — табуляция по центру строки,
    /// справа — табуляция по правому краю). Номер страницы и число страниц — поля Word PAGE/NUMPAGES.
    /// </summary>
    public static W.Paragraph FromBoxes(OpenXmlPart part, string styleId, IEnumerable<MarginBox> boxes, string projectRoot,
        string title, double textWidthPt, double areaMm, uint firstImageId, List<string> warnings)
    {
        var list = boxes.ToList();
        var properties = new W.ParagraphProperties { ParagraphStyleId = new W.ParagraphStyleId { Val = styleId } };
        var rule = list.FirstOrDefault(b => b.Rule is not null)?.Rule;
        if (rule is { } line)
        {
            var border = new W.ParagraphBorders();
            var size = (uint)Math.Clamp(Math.Round(line.WidthPt * 8), 2, 96);
            if (list[0].IsTop)
            {
                border.BottomBorder = new W.BottomBorder { Val = W.BorderValues.Single, Size = size, Space = 4, Color = line.Color };
            }
            else
            {
                border.TopBorder = new W.TopBorder { Val = W.BorderValues.Single, Size = size, Space = 4, Color = line.Color };
            }

            properties.ParagraphBorders = border;
        }

        properties.Tabs = new W.Tabs(
            new W.TabStop { Val = W.TabStopValues.Center, Position = (int)Math.Round(textWidthPt * 10) },
            new W.TabStop { Val = W.TabStopValues.Right, Position = (int)Math.Round(textWidthPt * 20) });
        properties.Justification = new W.Justification { Val = W.JustificationValues.Left };
        var paragraph = new W.Paragraph(properties);

        var imageId = firstImageId;
        for (var slot = 0; slot < 3; slot++)
        {
            if (slot > 0)
            {
                paragraph.Append(new W.Run(new W.TabChar()));
            }

            foreach (var box in list.Where(b => (int)b.Position % 3 == slot))
            {
                foreach (var item in box.Content)
                {
                    switch (item)
                    {
                        case MarginContent.Text text:
                            paragraph.Append(Run(box, text.Value));
                            break;
                        case MarginContent.PageNumber:
                            paragraph.Append(new W.SimpleField(Run(box, "1")) { Instruction = " PAGE " });
                            break;
                        case MarginContent.PageCount:
                            paragraph.Append(new W.SimpleField(Run(box, "1")) { Instruction = " NUMPAGES " });
                            break;
                        case MarginContent.Title:
                            paragraph.Append(Run(box, title));
                            break;
                        case MarginContent.Image image:
                        {
                            var full = DocxLayout.ResolveImage(projectRoot, image.Path);
                            if (full is null || DocxPictures.AddImage(part, full) is not { } relId)
                            {
                                warnings.Add(Loc.T("Core_TheHeaderOrFooterImageWas", image.Path));
                                break;
                            }

                            var (naturalWidth, naturalHeight) = ImageSize.ReadEmuSize(full, null, null);
                            var (width, height) = Fit(naturalWidth, naturalHeight, box.HeightMm ?? areaMm, textWidthPt);
                            paragraph.Append(DocxPictures.Inline(relId, width, height, imageId++, Path.GetFileName(full), "Логотип"));
                            break;
                        }
                    }
                }
            }
        }

        return paragraph;
    }

    private static W.Run Run(MarginBox box, string text)
    {
        var run = new W.Run();
        var props = new W.RunProperties();
        if (box.FontFamily is { Length: > 0 } family)
        {
            props.RunFonts = new W.RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family, EastAsia = family };
        }

        if (box.Bold)
        {
            props.Bold = new W.Bold();
        }

        if (box.Italic)
        {
            props.Italic = new W.Italic();
        }

        if (box.Color is { } color)
        {
            props.Color = new W.Color { Val = color };
        }

        if (box.FontSizePt is { } size)
        {
            props.FontSize = new W.FontSize { Val = ((int)Math.Round(size * 2)).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        if (props.HasChildren)
        {
            run.Append(props);
        }

        run.Append(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }
}
