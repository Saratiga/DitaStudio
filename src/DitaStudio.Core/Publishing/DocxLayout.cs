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
/// Внешний вид элементов (шрифты, цвета, отступы, рамки) и размер/поля страницы задаются
/// пользовательским CSS проекта — см. DitaStudio.Docx.Styling.
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
        Subtitle ??= string.Empty;
        Author ??= string.Empty;
        HeaderText ??= string.Empty;
        FooterText ??= string.Empty;
        Language = string.IsNullOrWhiteSpace(Language) ? "ru-RU" : Language.Trim();
    }
}
