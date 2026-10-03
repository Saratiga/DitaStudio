using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Desktop.Services;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Настоящая растеризация SVG (Svg.Skia) и экспорт в DOCX с ней.</summary>
public sealed class SvgRasterizerTests
{
    private const string RedAndBlue =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 40 20\"><rect width=\"20\" height=\"20\" fill=\"#ff0000\"/><rect x=\"20\" width=\"20\" height=\"20\" fill=\"#0000ff\"/></svg>";

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioSvgTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Rasterize_ScalesToRequestedSize_KeepsColorsAndTransparency()
    {
        var dir = TempDir();
        var svg = Path.Combine(dir, "a.svg");
        File.WriteAllText(svg, RedAndBlue);

        var png = new SkiaSvgRasterizer().RasterizeSvg(svg, 200, 100);
        Assert.NotNull(png);
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal((200, 100), (bitmap.Width, bitmap.Height));
        Assert.Equal(new SKColor(255, 0, 0, 255), bitmap.GetPixel(20, 50));
        Assert.Equal(new SKColor(0, 0, 255, 255), bitmap.GetPixel(180, 50));
    }

    [Fact]
    public void Rasterize_BrokenOrMissingFile_ReturnsNull()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "bad.svg"), "это не svg");
        var rasterizer = new SkiaSvgRasterizer();
        Assert.Null(rasterizer.RasterizeSvg(Path.Combine(dir, "bad.svg"), 100, 100));
        Assert.Null(rasterizer.RasterizeSvg(Path.Combine(dir, "none.svg"), 100, 100));
        Assert.Null(rasterizer.RasterizeSvg(Path.Combine(dir, "bad.svg"), 0, 100));
    }

    [Fact]
    public void DocxExport_WithRealRasterizer_EmbedsPngOfRightSize()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "fig.svg"), RedAndBlue);
        File.WriteAllText(Path.Combine(dir, "t.dita"), "<topic id=\"t\"><title>Схема</title><body><p><image href=\"fig.svg\"/></p></body></topic>");
        File.WriteAllText(Path.Combine(dir, "m.ditamap"), "<map><title>Схема</title><topicref href=\"t.dita\"/></map>");
        var project = new DitaProject(dir);
        project.Scan();

        var output = Path.Combine(dir, "out.docx");
        var result = new DocxPublisher(project).Publish(Path.Combine(dir, "m.ditamap"),
            new PublishOptions { Language = "ru", ImageRasterizer = new SkiaSvgRasterizer() }, output);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("svg", StringComparison.OrdinalIgnoreCase));
        using var doc = WordprocessingDocument.Open(output, false);
        var part = Assert.Single(doc.MainDocumentPart!.ImageParts);
        using var stream = part.GetStream();
        using var bitmap = SKBitmap.Decode(stream);
        Assert.Equal((80, 40), (bitmap.Width, bitmap.Height)); // viewBox 40×20 px, вдвое плотнее
        Assert.Equal(new SKColor(255, 0, 0, 255), bitmap.GetPixel(10, 20));
    }

    /// <summary>Снимок для глаз: настоящий SVG учебного проекта на белом фоне — как он ляжет в DOCX.</summary>
    [Fact]
    public void GuideSample_Svg_Screenshot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "DitaStudio.sln")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        var svg = Path.Combine(root!, "samples", "DitaStudioGuide", "images", "layout-overview.svg");
        var png = new SkiaSvgRasterizer().RasterizeSvg(svg, 1280, 640);
        Assert.NotNull(png);

        using var source = SKBitmap.Decode(png);
        using var flat = new SKBitmap(source.Width, source.Height);
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        using var data = SKImage.FromBitmap(flat).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, "svg-guide-layout.png"), data.ToArray());
    }
}
