using System.Globalization;
using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Оформление текста без знания CSS и XML: выравнивание, размер шрифта и цвет выбираются кнопками
/// в «Авторе», а в файл пишутся стандартным для DITA способом — классами <c>@outputclass</c>
/// (<c>align-center</c>, <c>size-10</c>, <c>color-red</c>). Встроенный CSS этих классов
/// подмешивается к публикации HTML/PDF и к стилям DOCX раньше CSS проекта — тот может их
/// переопределить. В каждой группе у элемента не больше одного класса.
/// </summary>
public static class TextFormatting
{
    public const string AlignPrefix = "align-";
    public const string SizePrefix = "size-";
    public const string ColorPrefix = "color-";
    public const string MarkPrefix = "mark-";

    /// <summary>Выравнивание: класс → (подпись, значение text-align).</summary>
    public static IReadOnlyList<(string Token, string Label, string Css)> Alignments => new[]
    {
        ("align-left", Loc.T("Tip_AlignLeft"), "left"),
        ("align-center", Loc.T("Tip_AlignCenter"), "center"),
        ("align-right", Loc.T("Tip_AlignRight"), "right"),
        ("align-justify", Loc.T("Tip_AlignJustify"), "justify")
    };

    /// <summary>Размеры шрифта, пт.</summary>
    public static readonly IReadOnlyList<int> Sizes = new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28 };

    /// <summary>Цвета текста: класс → (подпись, цвет).</summary>
    public static IReadOnlyList<(string Token, string Label, string Hex)> Colors => new[]
    {
        ("color-red", Loc.T("Core_Red"), "#C00000"),
        ("color-orange", Loc.T("Core_Orange"), "#D86A00"),
        ("color-green", Loc.T("Core_Green"), "#00873C"),
        ("color-blue", Loc.T("Core_Blue"), "#1F5FBF"),
        ("color-purple", Loc.T("Core_Purple"), "#7030A0"),
        ("color-gray", Loc.T("Core_Gray"), "#7F7F7F")
    };

    /// <summary>
    /// Цвета маркера (выделение фона, как «Цвет выделения текста» в Word): класс → (подпись, цвет). Это цвета палитры выделения Word,
    /// поэтому в DOCX они становятся настоящим выделением (<c>w:highlight</c>), а не заливкой.
    /// </summary>
    public static IReadOnlyList<(string Token, string Label, string Hex)> Marks => new[]
    {
        ("mark-red", Loc.T("Core_Red"), "#FF0000"),
        ("mark-yellow", Loc.T("Core_Yellow"), "#FFFF00"),
        ("mark-green", Loc.T("Core_Green"), "#00FF00"),
        ("mark-blue", Loc.T("Core_Blue"), "#0000FF")
    };

    /// <summary>Класс своего цвета маркера по «#RRGGBB» (или «RRGGBB»): «mark-ff8800»; null — не цвет.</summary>
    public static string? MarkToken(string hex)
    {
        var digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6 || !digits.All(Uri.IsHexDigit))
        {
            return null;
        }

        var token = MarkPrefix + digits.ToLowerInvariant();
        // Совпадающий со стандартным — стандартное имя (mark-yellow), а не второе написание того же цвета.
        var upper = "#" + digits.ToUpperInvariant();
        return Marks.FirstOrDefault(m => m.Hex == upper).Token ?? token;
    }

    /// <summary>Цвет класса маркера (#RRGGBB): стандартного (mark-yellow) или своего (mark-ff8800); null — это не класс маркера.</summary>
    public static string? ParseMarkToken(string token)
    {
        if (!token.StartsWith(MarkPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (Marks.FirstOrDefault(m => m.Token == token) is { Token: not null } named)
        {
            return named.Hex;
        }

        var digits = token[MarkPrefix.Length..];
        return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? "#" + digits.ToUpperInvariant() : null;
    }

    /// <summary>Цвет маркера узла (#RRGGBB); null — не задан.</summary>
    public static string? MarkOf(DitaNode node) =>
        Tokens(node).Select(ParseMarkToken).FirstOrDefault(hex => hex is not null);

    /// <summary>Класс маркера своего цвета (не из <see cref="Marks"/>) — встроенного правила CSS у него нет, оно выводится при публикации.</summary>
    public static bool IsCustomMark(string token) =>
        ParseMarkToken(token) is not null && !Marks.Any(m => m.Token == token);

    /// <summary>Тёмный ли фон (#RRGGBB): на нём текст «по умолчанию» делается белым — как автоматический цвет шрифта в Word на тёмном
    /// выделении (синий, тёмно-красный…). Красный и светлее — остаются с чёрным.</summary>
    public static bool IsDarkColor(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length != 6)
        {
            return false;
        }

        double Channel(int index)
        {
            var value = Convert.ToInt32(digits.Substring(index * 2, 2), 16) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2) < 0.1;
    }

    /// <summary>Правило CSS своего цвета маркера («background-color: #FF8800»; на тёмном фоне ещё «color: #FFFFFF»); null — класс не свой цвет маркера.</summary>
    public static string? CustomMarkCss(string token) =>
        IsCustomMark(token) ? $"background-color: {ParseMarkToken(token)}" + (IsDarkColor(ParseMarkToken(token)!) ? "; color: #FFFFFF" : string.Empty) : null;

    /// <summary>Правило CSS класса, которого нет во встроенном списке (свой размер или свой цвет маркера); null — класс обычный.</summary>
    public static string? CustomClassCss(string token) => CustomSizeCss(token) ?? CustomMarkCss(token);

    /// <summary>Наименьший и наибольший размер шрифта, пт, который можно задать своим числом.</summary>
    public const double MinCustomSize = 4;

    public const double MaxCustomSize = 200;

    /// <summary>
    /// Класс размера: «size-12» для целого числа пунктов и «size-13_5» для дробного (точка в имени класса CSS означала бы два
    /// класса, поэтому вместо неё подчёркивание). Числа вне 4–200 пт ограничиваются.
    /// </summary>
    public static string SizeToken(double points)
    {
        points = Math.Clamp(Math.Round(points * 10) / 10, MinCustomSize, MaxCustomSize);
        return SizePrefix + points.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', '_');
    }

    public static string SizeToken(int points) => SizeToken((double)points);

    /// <summary>Размер из класса «size-…» (пт); null — это не класс размера или число вне допустимых границ.</summary>
    public static double? ParseSizeToken(string token) =>
        token.StartsWith(SizePrefix, StringComparison.Ordinal) &&
        double.TryParse(token[SizePrefix.Length..].Replace('_', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) &&
        size is >= MinCustomSize and <= MaxCustomSize
            ? size
            : null;

    /// <summary>Класс размера не из списка <see cref="Sizes"/> — для него нет встроенного правила CSS, оно выводится при публикации.</summary>
    public static bool IsCustomSize(string token) =>
        ParseSizeToken(token) is { } size && !Sizes.Any(s => Math.Abs(s - size) < 0.001);

    /// <summary>Правило CSS своего размера («font-size: 13.5pt») для HTML и DOCX; null — класс не свой размер.</summary>
    public static string? CustomSizeCss(string token) =>
        IsCustomSize(token) ? $"font-size: {ParseSizeToken(token)!.Value.ToString("0.#", CultureInfo.InvariantCulture)}pt" : null;

    /// <summary>CSS встроенных классов оформления — для HTML/PDF и DOCX.</summary>
    public static string Css { get; } = BuildCss();

    private static string BuildCss()
    {
        var css = new StringBuilder("/* Оформление текста из «Автора» (TextFormatting) */\n");
        foreach (var (token, _, value) in Alignments)
        {
            css.Append('.').Append(token).Append(" { text-align: ").Append(value).Append("; }\n");
        }

        foreach (var size in Sizes)
        {
            css.Append('.').Append(SizeToken(size)).Append(" { font-size: ").Append(size).Append("pt; }\n");
        }

        // Маркеры — раньше цветов текста: явный цвет текста главнее белого, который тёмный маркер ставит по умолчанию.
        foreach (var (token, _, hex) in Marks)
        {
            css.Append('.').Append(token).Append(" { background-color: ").Append(hex).Append(IsDarkColor(hex) ? "; color: #FFFFFF" : string.Empty).Append("; }\n");
        }

        foreach (var (token, _, hex) in Colors)
        {
            css.Append('.').Append(token).Append(" { color: ").Append(hex).Append("; }\n");
        }

        return css.ToString();
    }

    /// <summary>Класс группы (<paramref name="prefix"/>) в outputclass узла или null.</summary>
    public static string? Token(DitaNode node, string prefix) =>
        Tokens(node).FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Ставит класс группы вместо прежнего (null — снимает класс группы); остальные классы
    /// outputclass не трогает. true — атрибут изменился.
    /// </summary>
    public static bool SetToken(DitaNode node, string prefix, string? token)
    {
        var before = node.GetAttribute("outputclass");
        var tokens = Tokens(node).Where(t => !t.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (token is not null)
        {
            tokens.Add(token);
        }

        if (tokens.Count == 0)
        {
            node.RemoveAttribute("outputclass");
        }
        else
        {
            node.SetAttribute("outputclass", string.Join(' ', tokens));
        }

        return node.GetAttribute("outputclass") != before;
    }

    /// <summary>Размер шрифта класса size-N (в том числе дробный size-13_5), пт; null — не задан.</summary>
    public static double? SizeOf(DitaNode node) =>
        Token(node, SizePrefix) is { } token ? ParseSizeToken(token) : null;

    /// <summary>Цвет класса color-… (#RRGGBB); null — не задан.</summary>
    public static string? ColorOf(DitaNode node) =>
        Token(node, ColorPrefix) is { } token ? Colors.FirstOrDefault(c => c.Token == token).Hex : null;

    /// <summary>
    /// Выравнивание блока: text-align класса align-… или (у ячейки CALS) стандартного @align;
    /// null — не задано.
    /// </summary>
    public static string? AlignmentOf(DitaNode node)
    {
        if (Token(node, AlignPrefix) is { } token)
        {
            return Alignments.FirstOrDefault(a => a.Token == token).Css;
        }

        return node.Name is "entry" && node.GetAttribute("align") is "left" or "center" or "right" or "justify"
            ? node.GetAttribute("align")
            : null;
    }

    private static IEnumerable<string> Tokens(DitaNode node) =>
        (node.GetAttribute("outputclass") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
