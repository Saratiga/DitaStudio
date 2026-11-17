using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>Готовая формула меню «Уравнение»: ключ, название и разметка DITA (содержимое <c>equation-inline</c>).</summary>
public sealed record EquationTemplate(string Key, string Name, string Markup);

/// <summary>
/// Формулы кнопки «Уравнение» — как встроенные уравнения Word, но в разметке DITA 1.3 (домен equation-d): формула в строке —
/// <c>equation-inline</c>, внутри которой переменные курсивом (<c>i</c>), степени и индексы — <c>sup</c>/<c>sub</c>, знаки — символами
/// Юникода. Такая формула видна в «Авторе», HTML, PDF и DOCX без MathML.
/// </summary>
public static class Equations
{
    /// <summary>Ключ пункта «Выключная формула» (отдельной строкой, <c>equation-block</c>).</summary>
    public const string BlockKey = "block";

    public static IReadOnlyList<EquationTemplate> BuiltIn { get; } = new[]
    {
        new EquationTemplate("circle", Loc.T("Eq_CircleArea"), "<i>A</i> = π<i>r</i><sup>2</sup>"),
        new EquationTemplate("pythagoras", Loc.T("Eq_Pythagoras"), "<i>a</i><sup>2</sup> + <i>b</i><sup>2</sup> = <i>c</i><sup>2</sup>"),
        new EquationTemplate("quadratic", Loc.T("Eq_Quadratic"),
            "<i>x</i> = (−<i>b</i> ± √(<i>b</i><sup>2</sup> − 4<i>ac</i>)) / 2<i>a</i>"),
        new EquationTemplate("binomial", Loc.T("Eq_Binomial"),
            "(<i>x</i> + <i>a</i>)<sup><i>n</i></sup> = ∑<sub><i>k</i>=0</sub><sup><i>n</i></sup> C<sub><i>n</i></sub><sup><i>k</i></sup> " +
            "<i>x</i><sup><i>k</i></sup><i>a</i><sup><i>n</i>−<i>k</i></sup>"),
        new EquationTemplate("trig", Loc.T("Eq_Trig"), "sin<sup>2</sup><i>α</i> + cos<sup>2</sup><i>α</i> = 1"),
        new EquationTemplate("euler", Loc.T("Eq_Euler"), "<i>e</i><sup><i>iπ</i></sup> + 1 = 0")
    };

    /// <summary>Элемент <c>equation-inline</c> готовой формулы; null — ключ неизвестен.</summary>
    public static DitaNode? Build(string key)
    {
        if (BuiltIn.FirstOrDefault(e => e.Key == key) is not { } template)
        {
            return null;
        }

        var root = DitaDocument.Parse("<equation-inline>" + template.Markup + "</equation-inline>").Root;
        root.RemoveSelf();
        return root;
    }
}

/// <summary>Символ меню «Символ»: знак и его название (подсказка).</summary>
public sealed record SymbolEntry(string Text, string Name);

/// <summary>Группа символов окна «Другие символы».</summary>
public sealed record SymbolGroup(string Name, IReadOnlyList<SymbolEntry> Symbols);

/// <summary>
/// Символы кнопки «Символ» (как в Word): короткий список частых знаков и окно «Другие символы» по группам. Недавно вставленные
/// поднимаются в начало короткого списка (<see cref="Remember"/>), на время работы редактора.
/// </summary>
public static class Symbols
{
    private static readonly List<string> Recent = new();

    public const int QuickCount = 20;

    public static IReadOnlyList<SymbolGroup> Groups { get; } = new[]
    {
        new SymbolGroup(Loc.T("Sym_Common"), Entries("€ евро|£ фунт|¥ иена|© авторское право|® зарегистрированный знак|™ товарный знак|± плюс-минус|≠ не равно|≤ меньше или равно|≥ больше или равно|÷ деление|× умножение|∞ бесконечность|µ микро|° градус|§ параграф|¶ абзац|… многоточие|— длинное тире|– короткое тире|« левая кавычка|» правая кавычка|„ нижняя кавычка|“ верхняя кавычка|№ номер|• маркер|· точка по центру|  неразрывный пробел")),
        new SymbolGroup(Loc.T("Sym_Greek"), Entries("α альфа|β бета|γ гамма|δ дельта|ε эпсилон|ζ дзета|η эта|θ тета|ι йота|κ каппа|λ лямбда|μ мю|ν ню|ξ кси|ο омикрон|π пи|ρ ро|σ сигма|τ тау|υ ипсилон|φ фи|χ хи|ψ пси|ω омега|Γ Гамма|Δ Дельта|Θ Тета|Λ Лямбда|Ξ Кси|Π Пи|Σ Сигма|Φ Фи|Ψ Пси|Ω Омега")),
        new SymbolGroup(Loc.T("Sym_Math"), Entries("± плюс-минус|∓ минус-плюс|× умножение|÷ деление|· умножение точкой|− минус|√ корень|∛ кубический корень|∑ сумма|∏ произведение|∫ интеграл|∬ двойной интеграл|∮ контурный интеграл|∂ частная производная|∇ набла|∆ приращение|∞ бесконечность|≈ приблизительно|≠ не равно|≡ тождественно|≤ меньше или равно|≥ больше или равно|≪ много меньше|≫ много больше|∝ пропорционально|∈ принадлежит|∉ не принадлежит|⊂ подмножество|⊃ надмножество|∪ объединение|∩ пересечение|∅ пустое множество|∀ для всех|∃ существует|¬ не|∧ и|∨ или|⇒ следует|⇔ равносильно|∠ угол|⊥ перпендикулярно|∥ параллельно|° градус|′ штрих|″ два штриха|‰ промилле|½ половина|¼ четверть|¾ три четверти|⅓ треть|² квадрат|³ куб")),
        new SymbolGroup(Loc.T("Sym_Arrows"), Entries("← влево|→ вправо|↑ вверх|↓ вниз|↔ влево-вправо|↕ вверх-вниз|⇐ двойная влево|⇒ двойная вправо|⇑ двойная вверх|⇓ двойная вниз|⇔ двойная влево-вправо|↵ ввод|⟵ длинная влево|⟶ длинная вправо|➔ жирная вправо|▲ треугольник вверх|▼ треугольник вниз|◀ треугольник влево|▶ треугольник вправо")),
        new SymbolGroup(Loc.T("Sym_Currency"), Entries("₽ рубль|$ доллар|€ евро|£ фунт|¥ иена|₴ гривна|₸ тенге|₹ рупия|₩ вона|₿ биткоин|¢ цент|₺ лира")),
        new SymbolGroup(Loc.T("Sym_Other"), Entries("✓ галочка|✔ жирная галочка|✗ крестик|✘ жирный крестик|★ звезда|☆ пустая звезда|☐ флажок|☑ отмеченный флажок|☒ перечёркнутый флажок|⚠ внимание|☎ телефон|✉ конверт|⌘ команда|⌥ option|⇧ shift|⌫ удаление|⏎ возврат|♠ пики|♣ трефы|♥ червы|♦ бубны|♪ нота|☺ улыбка|† крест|‡ двойной крест|※ примечание"))
    };

    /// <summary>Короткий список кнопки: недавние символы, затем частые.</summary>
    public static IReadOnlyList<SymbolEntry> Quick
    {
        get
        {
            var all = Groups.SelectMany(g => g.Symbols).GroupBy(s => s.Text).Select(g => g.First()).ToList();
            var recent = Recent.Select(text => all.FirstOrDefault(s => s.Text == text) ?? new SymbolEntry(text, text));
            return recent.Concat(Groups[0].Symbols).GroupBy(s => s.Text).Select(g => g.First()).Take(QuickCount).ToList();
        }
    }

    /// <summary>Запоминает вставленный символ — он встаёт первым в коротком списке.</summary>
    public static void Remember(string text)
    {
        Recent.Remove(text);
        Recent.Insert(0, text);
        if (Recent.Count > QuickCount)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }
    }

    /// <summary>Символ по коду Юникода (как «Код знака» в Word): «2122», «U+2122», «0x2122»; null — код не распознан.</summary>
    public static string? FromCode(string? code)
    {
        var text = (code ?? string.Empty).Trim();
        if (text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length is 0 or > 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value) ||
            value < 0x20 || value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF || value == 0xFFFC)
        {
            return null;
        }

        return char.ConvertFromUtf32(value);
    }

    // Строка «знак название|знак название» — первый «знак» до пробела (неразрывный пробел — сам знак).
    private static IReadOnlyList<SymbolEntry> Entries(string list) =>
        list.Split('|').Select(item =>
        {
            var space = item.IndexOf(' ', 1);
            return new SymbolEntry(item[..space], item[(space + 1)..].Trim());
        }).ToList();
}
