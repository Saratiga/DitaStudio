using System.Globalization;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Номера заголовков и нумерованных абзацев при публикации HTML/PDF — как у списка заголовков
/// Word в DOCX (ГОСТ 2.105): абзац «пункт» получает номер на уровень ниже текущего заголовка
/// (во 2-й главе, 3-м подразделе — 2.3.1) и того же уровня, что следующий подзаголовок.
/// Один счётчик ведёт всё издание по порядку карты.
/// </summary>
public sealed class HeadingNumbering
{
    /// <summary>Класс нумерованного абзаца.</summary>
    public const string NumberedClass = "numbered";

    private readonly int[] _counts = new int[10];
    private int _level;
    private int _simple;

    public HeadingNumbering(bool numberHeadings, int depth)
    {
        NumberHeadings = numberHeadings;
        Depth = Math.Clamp(depth, 1, 9);
    }

    /// <summary>Заголовки нумеруются (настройка «Нумеровать заголовки»).</summary>
    public bool NumberHeadings { get; }

    /// <summary>Сколько уровней заголовков нумеруется.</summary>
    public int Depth { get; }

    public static bool IsNumbered(DitaNode paragraph) =>
        (paragraph.GetAttribute("outputclass") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(NumberedClass);

    /// <summary>
    /// Заголовок уровня <paramref name="level"/> (1 — глава): номер «2.3» или null (заголовки не
    /// нумеруются, уровень глубже настройки или заголовок «без номера»).
    /// </summary>
    public string? Heading(int level, bool unnumbered)
    {
        _simple = 0; // простая нумерация абзацев — заново в каждом разделе
        if (!NumberHeadings || unnumbered)
        {
            return null;
        }

        level = Math.Clamp(level, 1, 9);
        if (level > Depth)
        {
            return null;
        }

        _counts[level - 1]++;
        Array.Clear(_counts, level, _counts.Length - level);
        _level = level;
        return Join(level);
    }

    /// <summary>Номер нумерованного абзаца: «2.3.1» (заголовки нумеруются) или «1», «2»… в разделе.</summary>
    public string Paragraph()
    {
        if (!NumberHeadings)
        {
            return (++_simple).ToString(CultureInfo.InvariantCulture);
        }

        var index = Math.Min(_level, 8);
        _counts[index]++;
        Array.Clear(_counts, index + 1, _counts.Length - index - 1);
        return Join(index + 1);
    }

    private string Join(int count) =>
        string.Join(".", _counts.Take(count).Select(c => c.ToString(CultureInfo.InvariantCulture)));
}
