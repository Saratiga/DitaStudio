namespace DitaStudio.Core.Publishing;

/// <summary>
/// Растеризация векторных картинок (SVG) в PNG для форматов, которые SVG не умеют (DOCX). Реализация — в оболочке;
/// ядро её не знает, поэтому без растеризатора экспорт ведёт себя по-прежнему (плашка с именем файла и предупреждение).
/// </summary>
public interface IImageRasterizer
{
    /// <summary>PNG-изображение SVG-файла размером <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> px; null — не получилось.</summary>
    byte[]? RasterizeSvg(string svgPath, int pixelWidth, int pixelHeight);
}
