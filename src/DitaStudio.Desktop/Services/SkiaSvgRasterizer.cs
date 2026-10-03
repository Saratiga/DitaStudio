using DitaStudio.Core.Publishing;
using SkiaSharp;
using Svg.Skia;

namespace DitaStudio.Desktop.Services;

/// <summary>
/// Растеризация SVG в PNG для DOCX через Svg.Skia (SkiaSharp, он уже нужен Avalonia): ни встроенный Chromium, ни окно не требуются,
/// поэтому работает в любой момент и на любой ОС. Фон прозрачный; пропорции берутся из самого SVG и вписываются в заданный размер.
/// </summary>
public sealed class SkiaSvgRasterizer : IImageRasterizer
{
    public byte[]? RasterizeSvg(string svgPath, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        try
        {
            using var svg = new SKSvg();
            if (svg.Load(svgPath) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
            {
                return null;
            }

            using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(pixelWidth / picture.CullRect.Width, pixelHeight / picture.CullRect.Height);
            canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
        {
            return null; // битый SVG — экспорт покажет плашку и предупреждение
        }
    }
}
