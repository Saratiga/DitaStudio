using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Validation;

namespace DitaStudio.Tests;

// Замечания (3): правки «изменено» с автором и временем, пометка слов внутри блока, уравнения и символы, пустой заголовок,
// путь к элементу для перехода из «Проверки», сноски и вид Word в «живых» страницах.
internal static partial class CoreChecks
{
    internal static void RemarksTests()
    {
        Section("Замечания (3)");

        // --- Track changes: изменено, дата со временем, описание для пометки
        var doc = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><p>Один <ph>два</ph> три</p><p>Второй</p></body></topic>");
        var p = doc.Root.Descendants().First(n => n.Name == "p");
        TrackChanges.Mark(p, TrackChanges.StatusChanged, "Иванов", new DateTime(2026, 11, 17, 14, 5, 0));
        Check(TrackChanges.IsChanged(p) && TrackChanges.IsTracked(p), "status=\"changed\" — отслеживаемая правка");
        Check(p.GetAttribute("tcdate") == "2026-11-17 14:05", "tcdate — дата со временем");
        Check(TrackChanges.DisplayDate(p) == "17.11.2026 14:05", "дата для показа — дд.мм.гггг чч:мм");
        Check(TrackChanges.Describe(p) is { } description && description.Contains("Иванов") && description.Contains("17.11.2026 14:05"),
            "описание правки для оранжевой пометки: кто и когда");
        TrackChanges.Accept(p);
        Check(!TrackChanges.IsTracked(p) && p.GetAttribute("tcauthor") is null && p.Parent is not null, "принять «изменено» — пометка снята, блок на месте");

        var old = DitaDocument.Parse("<p status=\"new\" tcdate=\"2025-01-02\">x</p>").Root;
        Check(TrackChanges.DisplayDate(old) == "02.01.2025", "старая дата без времени читается");

        // Слова внутри блока: ph с пометкой; после «Принять» обёртка без атрибутов разворачивается, текст остаётся.
        var ph = doc.Root.Descendants().First(n => n.Name == "ph");
        TrackChanges.MarkInserted(ph, "Петров");
        var paragraph = ph.Parent!;
        TrackChanges.Accept(ph);
        Check(ph.Parent is null && paragraph.InnerText == "Один два три", "принять вставку слов — ph развёрнут, текст на месте");
        var doc2 = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><p>A <ph status=\"new\">B</ph> C</p></body></topic>");
        var inserted = doc2.Root.Descendants().First(n => n.Name == "ph");
        var host = inserted.Parent!;
        TrackChanges.Reject(inserted);
        Check(host.InnerText == "A  C", "отклонить вставку слов — слова убраны");

        // --- Уравнения: готовые формулы — допустимый equation-inline
        foreach (var equation in Equations.BuiltIn)
        {
            var node = Equations.Build(equation.Key);
            Check(node is { Name: "equation-inline", Parent: null } && node.InnerText.Length > 0, $"формула «{equation.Name}» собирается");
            if (node is not null)
            {
                var test = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><p>Формула: </p></body></topic>");
                test.Root.Descendants().First(n => n.Name == "p").Add(node);
                var errors = new DitaValidator { CheckStyleRules = false }.Validate(test).Where(i => i.Severity == IssueSeverity.Error).ToList();
                Check(errors.Count == 0, $"формула «{equation.Name}» допустима в абзаце: " + string.Join("; ", errors.Select(e => e.Message)));
            }
        }

        Check(Equations.Build("нет такой") is null, "неизвестная формула — null");

        // --- Символы
        Check(Symbols.FromCode("2122") == "™" && Symbols.FromCode("U+00A9") == "©" && Symbols.FromCode("0x3b1") == "α", "символ по коду");
        Check(Symbols.FromCode("zz") is null && Symbols.FromCode("D800") is null && Symbols.FromCode("FFFC") is null, "неверный код и служебные знаки — null");
        Check(Symbols.Groups.All(g => g.Symbols.All(s => s.Text.Length is 1 or 2 && s.Name.Length > 0)), "у каждого символа — один знак и название");
        Symbols.Remember("∑");
        Check(Symbols.Quick.Count == Symbols.QuickCount && Symbols.Quick[0].Text == "∑", "недавний символ — первым в коротком списке");

        // --- Пустой заголовок топика — не ошибка (и с пустым фразовым элементом внутри)
        foreach (var title in new[] { "<title/>", "<title> </title>", "<title><ph/></title>" })
        {
            var empty = DitaDocument.Parse($"<topic id=\"t\">{title}<shortdesc>К</shortdesc><body><p>Текст</p></body></topic>");
            var issues = new DitaValidator().Validate(empty);
            Check(issues.All(i => i.Severity != IssueSeverity.Error), $"пустой заголовок {title} — не ошибка: " +
                string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message)));
        }

        // --- Путь к элементу: переход из «Проверки» в открытый документ (своя копия файла)
        var a = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><p>1</p><section><p>2</p><p>3</p></section></body></topic>");
        var b = DitaDocument.Parse("<topic id=\"t\"><title>T</title><body><p>1</p><section><p>2</p><p>3</p></section></body></topic>");
        var target = a.Root.Descendants().Where(n => n.Name == "p").Last();
        Check(ProjectChanges.Resolve(b, ProjectChanges.PathOf(target)) is { } found && found.InnerText == "3" && !ReferenceEquals(found, target),
            "путь от корня находит тот же элемент в другой копии документа");

        // --- «Живые» страницы: сноски внизу листа и вид Word
        var html = PagedPreview.Build(PageGeometry.A4, "", "T", "", "{page}", "<p>x<a class=\"fn-ref\" href=\"#fn1\">[1]</a></p><div class=\"footnotes\"><h2>Сноски</h2><ol><li id=\"fn1\">Сноска</li></ol></div>");
        Check(html.Contains("fnarea") && html.Contains("extractFootnotes"), "сноски раскладываются по листам, а не списком в конце");
        Check(html.Contains("word-css") && html.Contains("Calibri"), "листы оформлены как документ Word");
        Check(html.Contains("page-break-before"), "разрыв страницы перед блоком начинает новый лист");

        // --- Пометки правок в HTML: изменённое и слова внутри абзаца
        Check(Assets.StyleSheet.Contains(".tc-changed") && Assets.StyleSheet.Contains("span.tc-inserted"), "стили правок: изменённое и слова внутри абзаца");
        Check(DitaCatalog.Default.Get("equation-inline") is { IsInline: true }, "equation-inline — фразовый элемент каталога");
    }
}
