using System.Text.Json;
using System.Text.Json.Serialization;

namespace DitaStudio.Core.Publishing;

/// <summary>Выравнивание текста колонтитула.</summary>
public enum DocxHeaderAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Параметры вёрстки DOCX, которые нельзя выразить через CSS: титульная страница, оглавление,
/// нумерация заголовков, колонтитулы с полями Word, переплёт, язык, переносы, свойства файла.
/// Внешний вид элементов (шрифты, цвета, отступы, рамки) задаётся пользовательским CSS проекта —
/// см. DitaStudio.Docx.Styling; размер бумаги, ориентация и поля — здесь (для DOCX и PDF), а если
/// не заданы — тоже из @page CSS.
/// Хранится вместе с проектом (<c>.ditastudio-docx</c>, JSON).
/// </summary>
public sealed class DocxLayout
{
    /// <summary>Отдельная титульная страница с названием карты.</summary>
    public bool TitlePage { get; set; } = true;

    /// <summary>Подзаголовок на титульной странице.</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Автор или организация: строка на титульной странице и свойство «Автор» файла.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Дата публикации на титульной странице.</summary>
    public bool TitlePageDate { get; set; }

    /// <summary>Оглавление (поле TOC Word).</summary>
    public bool TableOfContents { get; set; } = true;

    /// <summary>Сколько уровней заголовков попадает в оглавление (1–6).</summary>
    public int TocDepth { get; set; } = 3;

    /// <summary>Многоуровневая нумерация заголовков: 1, 1.1, 1.1.1…</summary>
    public bool NumberHeadings { get; set; }

    /// <summary>Сколько уровней заголовков нумеруется (1–6).</summary>
    public int NumberingDepth { get; set; } = 3;

    /// <summary>Каждый топик верхнего уровня карты начинается с новой страницы.</summary>
    public bool PageBreakBeforeTopLevel { get; set; }

    /// <summary>Нумеровать рисунки и таблицы в подписях («Рисунок 3.», «Таблица 2.»).</summary>
    public bool NumberFiguresAndTables { get; set; } = true;

    /// <summary>Текст верхнего колонтитула; поля: {page}, {pages}, {title}, {date}.</summary>
    public string HeaderText { get; set; } = string.Empty;

    public DocxHeaderAlignment HeaderAlignment { get; set; } = DocxHeaderAlignment.Right;

    /// <summary>Текст нижнего колонтитула; поля: {page}, {pages}, {title}, {date}.</summary>
    public string FooterText { get; set; } = string.Empty;

    public DocxHeaderAlignment FooterAlignment { get; set; } = DocxHeaderAlignment.Center;

    /// <summary>Картинка верхнего колонтитула (логотип) — путь от папки проекта; пусто — нет. Действует и в PDF.</summary>
    public string HeaderImage { get; set; } = string.Empty;

    public DocxHeaderAlignment HeaderImageAlignment { get; set; } = DocxHeaderAlignment.Left;

    /// <summary>Высота картинки верхнего колонтитула, мм (ширина — по пропорциям).</summary>
    public double HeaderImageHeightMm { get; set; } = 10;

    /// <summary>Картинка нижнего колонтитула — путь от папки проекта; пусто — нет.</summary>
    public string FooterImage { get; set; } = string.Empty;

    public DocxHeaderAlignment FooterImageAlignment { get; set; } = DocxHeaderAlignment.Left;

    public double FooterImageHeightMm { get; set; } = 10;

    /// <summary>Форматы картинок колонтитулов (их понимают и Word, и Chromium).</summary>
    public static readonly IReadOnlyList<string> ImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    /// <summary>Полный путь картинки колонтитула или null — не задана, нет файла или формат не тот.</summary>
    public static string? ResolveImage(string projectRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, relative));
        return File.Exists(full) && ImageExtensions.Contains(System.IO.Path.GetExtension(full).ToLowerInvariant()) ? full : null;
    }

    /// <summary>Картинка как data-URI — для шаблонов колонтитулов PDF (внешние файлы Chromium туда не грузит).</summary>
    public static string ImageDataUri(string fullPath)
    {
        var mime = System.IO.Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(fullPath))}";
    }

    /// <summary>Не показывать колонтитулы на первой странице (обычно — на титульной).</summary>
    public bool NoHeaderOnFirstPage { get; set; } = true;

    /// <summary>Зеркальные поля — для двусторонней печати.</summary>
    public bool MirrorMargins { get; set; }

    /// <summary>Поле переплёта, мм.</summary>
    public double GutterMm { get; set; }

    /// <summary>Язык текста (для проверки правописания и переносов в Word), например ru-RU.</summary>
    public string Language { get; set; } = "ru-RU";

    /// <summary>Автоматическая расстановка переносов.</summary>
    public bool AutoHyphenation { get; set; }

    /// <summary>Заголовок оглавления (DOCX, единый HTML и PDF); пусто — «Содержание» (по языку публикации).</summary>
    public string TocTitle { get; set; } = string.Empty;

    // ---- страница: общая для DOCX и PDF, перекрывает @page пользовательского CSS

    /// <summary>Размер бумаги (A4, A3, A5, B5, Letter, Legal); пусто — как в CSS проекта (по умолчанию A4).</summary>
    public string PaperSize { get; set; } = string.Empty;

    /// <summary>
    /// Свой размер бумаги, мм (ширина × высота в книжной ориентации): заданы оба или ни один. Если
    /// заданы, главнее <see cref="PaperSize"/>. Границы — <see cref="MinPaperMm"/>…<see cref="MaxPaperMm"/>.
    /// </summary>
    public double? PaperWidthMm { get; set; }

    public double? PaperHeightMm { get; set; }

    public const double MinPaperMm = 50;

    public const double MaxPaperMm = 2000;

    /// <summary>Альбомная ориентация.</summary>
    public bool Landscape { get; set; }

    /// <summary>Поля страницы, мм; null — как в CSS проекта (по умолчанию 20 мм).</summary>
    public double? MarginTopMm { get; set; }

    public double? MarginBottomMm { get; set; }

    public double? MarginLeftMm { get; set; }

    public double? MarginRightMm { get; set; }

    /// <summary>Размеры бумаги в миллиметрах (ширина × высота в книжной ориентации).</summary>
    public static readonly IReadOnlyDictionary<string, (double Width, double Height)> PaperSizesMm =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["A3"] = (297, 420),
            ["A4"] = (210, 297),
            ["A5"] = (148, 210),
            ["B5"] = (176, 250),
            ["Letter"] = (215.9, 279.4),
            ["Legal"] = (215.9, 355.6)
        };

    /// <summary>Свой размер бумаги, если задан.</summary>
    [JsonIgnore]
    public bool HasCustomPaper => PaperWidthMm is not null && PaperHeightMm is not null;

    /// <summary>Размер бумаги в мм (книжная ориентация): свой, иначе выбранный из списка, иначе null —
    /// «как в CSS проекта».</summary>
    public (double Width, double Height)? PaperMm() =>
        HasCustomPaper ? (PaperWidthMm!.Value, PaperHeightMm!.Value)
        : PaperSizesMm.TryGetValue(PaperSize, out var named) ? named
        : null;

    /// <summary>Задано ли что-то о странице (иначе всё берётся из CSS проекта).</summary>
    [JsonIgnore]
    public bool HasPageSetup =>
        PaperSize.Length > 0 || HasCustomPaper || Landscape || MarginTopMm is not null || MarginBottomMm is not null ||
        MarginLeftMm is not null || MarginRightMm is not null;

    /// <summary>Правило @page для печати HTML в PDF; пусто — ничего не задано.</summary>
    public string PageCss()
    {
        if (!HasPageSetup)
        {
            return string.Empty;
        }

        static string Mm(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "mm";

        var rules = new List<string>();
        var size = PaperSize.Length > 0 ? PaperSize : string.Empty;
        if (HasCustomPaper)
        {
            // Ключевое слово landscape в CSS сочетается только с именем формата, поэтому у своего
            // размера альбомная ориентация — это размеры, поставленные в порядке «шире, чем выше».
            var (width, height) = (PaperWidthMm!.Value, PaperHeightMm!.Value);
            if (Landscape && width < height)
            {
                (width, height) = (height, width);
            }

            size = $"{Mm(width)} {Mm(height)}";
        }
        else if (Landscape)
        {
            size = (size + " landscape").Trim();
        }

        if (size.Length > 0)
        {
            rules.Add($"size: {size};");
        }

        foreach (var (name, value) in new[] { ("top", MarginTopMm), ("right", MarginRightMm), ("bottom", MarginBottomMm), ("left", MarginLeftMm) })
        {
            if (value is { } mm)
            {
                rules.Add($"margin-{name}: {Mm(mm)};");
            }
        }

        return "/* Параметры страницы проекта (DocxLayout) */\n@page { " + string.Join(" ", rules) + " }";
    }

    /// <summary>Поля, которые можно вставлять в текст колонтитулов.</summary>
    public static readonly IReadOnlyList<string> Fields = new[] { "{page}", "{pages}", "{title}", "{date}" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Разбирает JSON; неизвестные поля пропускаются, недостающие берутся по умолчанию,
    /// числа приводятся к допустимым границам.</summary>
    public static DocxLayout FromJson(string json)
    {
        var layout = JsonSerializer.Deserialize<DocxLayout>(json, JsonOptions) ?? new DocxLayout();
        layout.Normalize();
        return layout;
    }

    public DocxLayout Clone() => FromJson(ToJson());

    public void Normalize()
    {
        TocDepth = Math.Clamp(TocDepth, 1, 6);
        NumberingDepth = Math.Clamp(NumberingDepth, 1, 6);
        GutterMm = double.IsFinite(GutterMm) ? Math.Clamp(GutterMm, 0, 100) : 0;
        PaperSize = PaperSizesMm.Keys.FirstOrDefault(k => string.Equals(k, PaperSize?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (PaperWidthMm is { } paperWidth && PaperHeightMm is { } paperHeight &&
            double.IsFinite(paperWidth) && double.IsFinite(paperHeight))
        {
            PaperWidthMm = Math.Clamp(paperWidth, MinPaperMm, MaxPaperMm);
            PaperHeightMm = Math.Clamp(paperHeight, MinPaperMm, MaxPaperMm);
            PaperSize = string.Empty; // свой размер главнее
        }
        else
        {
            PaperWidthMm = null;
            PaperHeightMm = null;
        }

        MarginTopMm = Margin(MarginTopMm);
        MarginBottomMm = Margin(MarginBottomMm);
        MarginLeftMm = Margin(MarginLeftMm);
        MarginRightMm = Margin(MarginRightMm);
        Subtitle ??= string.Empty;
        TocTitle = TocTitle?.Trim() ?? string.Empty;
        Author ??= string.Empty;
        HeaderText ??= string.Empty;
        HeaderImage = HeaderImage?.Trim() ?? string.Empty;
        FooterImage = FooterImage?.Trim() ?? string.Empty;
        HeaderImageHeightMm = double.IsFinite(HeaderImageHeightMm) ? Math.Clamp(HeaderImageHeightMm, 3, 60) : 10;
        FooterImageHeightMm = double.IsFinite(FooterImageHeightMm) ? Math.Clamp(FooterImageHeightMm, 3, 60) : 10;
        FooterText ??= string.Empty;
        Language = string.IsNullOrWhiteSpace(Language) ? "ru-RU" : Language.Trim();

        static double? Margin(double? value) => value is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, 100) : null;
    }
}
