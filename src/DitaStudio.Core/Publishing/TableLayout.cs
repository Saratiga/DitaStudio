using System.Globalization;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Ширины столбцов и высоты строк таблиц — то, что автор задаёт мышью в «Авторе». Ширины —
/// стандартные DITA: доли (<c>colwidth="2*"</c> у CALS, <c>relcolwidth="1* 2*"</c> у simpletable);
/// высота строки — классом <c>outputclass="row-height-12mm"</c> (у DITA 1.3 своего атрибута нет).
/// Публикация HTML/PDF и DOCX переводит их в проценты ширины и минимальную высоту строки.
/// </summary>
public static class TableLayout
{
    public const string RowHeightPrefix = "row-height-";

    /// <summary>
    /// Доли столбцов (сумма 1) по значениям ширины: «N*»/«*» — доли, абсолютные (px, pt, pc, in,
    /// cm, mm) — пропорционально размеру, пустые — как средний столбец. null — ширины не заданы.
    /// </summary>
    public static double[]? Fractions(IReadOnlyList<string?> widths)
    {
        if (widths.Count == 0 || widths.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var stars = widths.Select(Star).ToArray();
        var absolute = widths.Select(Absolute).ToArray();
        double[] weights;
        if (absolute.All(a => a is null))
        {
            weights = stars.Select(s => s ?? 1).ToArray();
        }
        else
        {
            // Смешанные единицы: абсолютные — в px, доля и пустое — как средний абсолютный столбец.
            var mean = absolute.Where(a => a is not null).Average(a => a!.Value);
            weights = absolute.Select((a, i) => a ?? (stars[i] ?? 1) * mean).ToArray();
        }

        var total = weights.Sum();
        return total <= 0 ? null : weights.Select(w => w / total).ToArray();
    }

    /// <summary>Ширины столбцов CALS из colspec (по порядку), null — не заданы.</summary>
    public static double[]? CalsFractions(DitaNode tgroup) =>
        Fractions(tgroup.ElementChildren().Where(e => e.Name == "colspec").Select(c => c.GetAttribute("colwidth")).ToList());

    /// <summary>Ширины столбцов simpletable из relcolwidth, null — не заданы.</summary>
    public static double[]? SimpleFractions(DitaNode table, int columns)
    {
        var tokens = (table.GetAttribute("relcolwidth") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        return Fractions(Enumerable.Range(0, Math.Max(columns, tokens.Length)).Select(i => i < tokens.Length ? tokens[i] : "1*").ToList());
    }

    /// <summary>Записывает доли столбцов CALS в colwidth («N*»), создавая colspec, если их нет.</summary>
    public static void SetCalsWidths(DitaNode tgroup, IReadOnlyList<double> weights)
    {
        EditCommands.EnsureColumnNames(tgroup);
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        var shares = Shares(weights);
        for (var i = 0; i < colspecs.Count && i < shares.Count; i++)
        {
            colspecs[i].SetAttribute("colwidth", shares[i] + "*");
        }
    }

    /// <summary>Записывает доли столбцов simpletable в relcolwidth.</summary>
    public static void SetSimpleWidths(DitaNode table, IReadOnlyList<double> weights) =>
        table.SetAttribute("relcolwidth", string.Join(' ', Shares(weights).Select(s => s + "*")));

    /// <summary>Доли в сотых (сумма ≈ 100), без хвостов после запятой — читаемо в XML.</summary>
    private static List<string> Shares(IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        return weights.Select(w => (total <= 0 ? 1 : Math.Max(1, Math.Round(w / total * 100, 1)))
            .ToString("0.#", CultureInfo.InvariantCulture)).ToList();
    }

    /// <summary>Минимальная высота строки из класса row-height-Nmm, мм; null — не задана.</summary>
    public static double? RowHeightMm(DitaNode row)
    {
        var token = TextFormatting.Token(row, RowHeightPrefix);
        if (token is null || !token.EndsWith("mm", StringComparison.Ordinal))
        {
            return null;
        }

        var number = token[RowHeightPrefix.Length..^2];
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm > 0 ? mm : null;
    }

    /// <summary>Задаёт минимальную высоту строки (мм, целые); null — снять.</summary>
    public static void SetRowHeight(DitaNode row, double? mm) =>
        TextFormatting.SetToken(row, RowHeightPrefix,
            mm is { } value ? RowHeightPrefix + Math.Max(1, Math.Round(value)).ToString(CultureInfo.InvariantCulture) + "mm" : null);

    private static double? Star(string? width)
    {
        var text = width?.Trim();
        if (string.IsNullOrEmpty(text) || !text.EndsWith('*'))
        {
            return null;
        }

        return text.Length == 1 ? 1
            : double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 1;
    }

    private static double? Absolute(string? width)
    {
        var text = width?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text.EndsWith('*'))
        {
            return null;
        }

        var unitStart = text.TakeWhile(c => char.IsDigit(c) || c == '.').Count();
        if (!double.TryParse(text[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return null;
        }

        return text[unitStart..] switch
        {
            "" or "px" => number,
            "pt" => number * 96 / 72,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            _ => null
        };
    }
}
