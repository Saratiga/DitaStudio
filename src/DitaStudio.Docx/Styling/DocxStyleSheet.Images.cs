using DitaStudio.Core.Model;

namespace DitaStudio.Docx.Styling;

// Изображения из CSS: размер (width, height, max-width, max-height) и рамка (border) у картинки по классу или селектору.
public sealed partial class DocxStyleSheet
{
    /// <summary>
    /// Размер и рамка картинки из правил CSS для её класса и селекторов (`img.wide`, `.fig image`, `image { border: 1px solid #999 }`).
    /// Размеры, пт; проценты считаются от ширины текста <paramref name="textWidthPt"/>. null — правил для картинки нет.
    /// </summary>
    public DocxImageLook? ImageLookFor(DitaNode image, double textWidthPt)
    {
        if (!HasTableRules)
        {
            return null;
        }

        var declarations = Cascade(image);
        if (declarations.Count == 0)
        {
            return null;
        }

        double? Dimension(string property, double basePt)
        {
            if (declarations.LastOrDefault(d => d.Property == property) is not { } d)
            {
                return null;
            }

            var value = Substitute(d.Value).Trim();
            if (value.EndsWith('%'))
            {
                return double.TryParse(value[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent > 0
                    ? basePt * percent / 100
                    : null;
            }

            return CssApplier.Length(value, DocxDefaults.FontSizePt) is { } length && length > 0 ? length : null;
        }

        var box = new CssBox(new DocxStyleProps());
        CssApplier.Apply(box, declarations.Where(d => d.Property.StartsWith("border", StringComparison.Ordinal)).ToList(), DocxDefaults.FontSizePt, this, silent: true);
        var border = box.Props.BorderTop ?? box.Props.BorderLeft ?? box.Props.BorderBottom ?? box.Props.BorderRight;
        var look = new DocxImageLook(Dimension("width", textWidthPt), Dimension("height", textWidthPt), Dimension("max-width", textWidthPt), Dimension("max-height", textWidthPt), border);
        return look.WidthPt is null && look.HeightPt is null && look.MaxWidthPt is null && look.MaxHeightPt is null && look.Border is null ? null : look;
    }
}

/// <summary>Оформление картинки из CSS. null — «как задано в DITA».</summary>
public sealed record DocxImageLook(double? WidthPt, double? HeightPt, double? MaxWidthPt, double? MaxHeightPt, DocxBorder? Border)
{
    /// <summary>Размер в EMU по пропорциям исходного: заданы ширина и высота — обе; одна из них — вторая по пропорции; затем ограничения max-*.</summary>
    public (long Width, long Height) Apply(long widthEmu, long heightEmu)
    {
        const double emuPerPt = 12700;
        double width = widthEmu, height = heightEmu;
        var ratio = widthEmu > 0 ? (double)heightEmu / widthEmu : 1;
        if (WidthPt is { } w && HeightPt is { } h)
        {
            (width, height) = (w * emuPerPt, h * emuPerPt);
        }
        else if (WidthPt is { } onlyWidth)
        {
            (width, height) = (onlyWidth * emuPerPt, onlyWidth * emuPerPt * ratio);
        }
        else if (HeightPt is { } onlyHeight)
        {
            (width, height) = (ratio > 0 ? onlyHeight * emuPerPt / ratio : width, onlyHeight * emuPerPt);
        }

        if (MaxWidthPt is { } maxWidth && width > maxWidth * emuPerPt)
        {
            (width, height) = (maxWidth * emuPerPt, height * maxWidth * emuPerPt / width);
        }

        if (MaxHeightPt is { } maxHeight && height > maxHeight * emuPerPt)
        {
            (width, height) = (width * maxHeight * emuPerPt / height, maxHeight * emuPerPt);
        }

        return ((long)Math.Max(1, width), (long)Math.Max(1, height));
    }
}
