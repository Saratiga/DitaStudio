using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Доработки по замечаниям пользователей (docs/REVIEW_PLAN.md) — по разделу на пункт.
internal static partial class CoreChecks
{
    /// <summary>Временный проект из пар «относительный путь → содержимое»; папка удаляется после действия.</summary>
    private static void WithProject(IReadOnlyDictionary<string, string> files, Action<string, DitaProject> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (relative, content) in files)
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }

            var project = new DitaProject(root);
            project.Scan();
            action(root, project);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // временные файлы удалятся системой
            }
        }
    }

    /// <summary>П. 1: топик без заголовка (например, из одной таблицы).</summary>
    internal static void UntitledTopicTests()
    {
        Section("Топик без заголовка");

        var styled = new DitaValidator();
        var untitled = DitaDocument.Parse("<reference id=\"r\"><title/><refbody><p>Текст</p></refbody></reference>");
        var issues = styled.Validate(untitled);
        Check(!issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Warning && i.Message.Contains("заголов", StringComparison.OrdinalIgnoreCase)),
            "пустой <title/> — не ошибка и не предупреждение: " + string.Join("; ", issues.Select(i => i.Message)));
        Check(issues.Any(i => i.Severity == IssueSeverity.Info && i.Message.Contains("Пустой заголовок")),
            "пустой <title/> — информационное сообщение");
        Check(!issues.Any(i => i.Message == "Пустой элемент <title>."), "про пустой title топика нет общего «Пустой элемент»");

        var emptySectionTitle = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><section><title/><p>x</p></section></conbody></concept>");
        Check(styled.Validate(emptySectionTitle).Any(i => i.Message == "Пустой элемент <title>."),
            "пустой title раздела по-прежнему предупреждение");

        var glossary = DitaDocument.Parse("<glossentry id=\"g\"><glossterm></glossterm><glossdef>Определение.</glossdef></glossentry>");
        Check(styled.Validate(glossary).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("Пустой заголовок")),
            "пустой glossterm — по-прежнему ошибка");

        var template = DocumentTemplates.Create("table", "Параметры сети");
        Check(template.Root.FirstElement("title") is { } t && DitaValidator.IsEmptyTitle(t), "заготовка «Таблица» — с пустым заголовком");
        Check(template.Root.Descendants().Any(n => n.Name == "table"), "заготовка «Таблица» — с таблицей");
        Check(template.Title == "Параметры сети", $"название заготовки «Таблица» — из подписи таблицы: {template.Title}");

        WithProject(new Dictionary<string, string>
        {
            ["intro.dita"] = """
<concept id="intro"><title>Введение</title><conbody><p>См. <xref href="params.dita"/>.</p></conbody></concept>
""",
            ["params.dita"] = """
<reference id="params"><title/><refbody><table><title>Параметры сети</title><tgroup cols="1"><tbody><row><entry>IP</entry></row></tbody></tgroup></table></refbody></reference>
""",
            ["guide.ditamap"] = """
<map><title>Книга</title><topicref href="intro.dita"/><topicref href="params.dita"/></map>
"""
        }, (root, project) =>
        {
            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out-single"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(!System.Text.RegularExpressions.Regex.IsMatch(html, @"<h\d[^>]*>\s*</h\d>"), "HTML: пустой заголовок не печатается");
            var toc = html[html.IndexOf("toc-inline", StringComparison.Ordinal)..html.IndexOf("</nav>", StringComparison.Ordinal)];
            Check(toc.Contains("Введение") && !toc.Contains("Параметры сети"), "HTML: топик без заголовка не попадает в оглавление издания");
            Check(html.Contains(">Параметры сети</a>"), "HTML: ссылка на топик без заголовка подписана названием из таблицы");

            var outFile = Path.Combine(root, "book.docx");
            var docx = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с топиком без заголовка");
            using var package = WordprocessingDocument.Open(outFile, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var headings = body.Elements<W.Paragraph>()
                .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading", StringComparison.Ordinal) == true)
                .ToList();
            Check(headings.Count == 1 && headings[0].InnerText == "Введение",
                "DOCX: заголовок только у топика с заголовком: " + string.Join(" | ", headings.Select(h => h.InnerText)));
            var link = body.Descendants<W.Hyperlink>().FirstOrDefault(h => h.InnerText == "Параметры сети");
            Check(link?.Anchor?.Value is { } anchor && body.Descendants<W.BookmarkStart>().Any(b => b.Name == anchor),
                "DOCX: ссылка на топик без заголовка ведёт на закладку в теле документа");
        });
    }

    /// <summary>П. 10: «Найти ссылки на топик» и удаление файла из проекта (меню карты).</summary>
    internal static void FileReferencesTests()
    {
        Section("Ссылки на файл");

        WithProject(new Dictionary<string, string>
        {
            ["topics/a.dita"] = """
<concept id="a"><title>A</title><conbody><p>См. <xref href="b.dita#b"/> и <xref href="#a"/>, сайт <xref href="https://b.dita" scope="external"/>.</p></conbody></concept>
""",
            ["topics/b.dita"] = """
<concept id="b"><title>B</title><conbody><p conref="a.dita#a/x"/></conbody></concept>
""",
            ["guide.ditamap"] = """
<map><title>Книга</title><topicref href="topics/a.dita"/><topicref href="topics/b.dita"/><keydef keys="kb" href="topics/b.dita"/></map>
"""
        }, (root, project) =>
        {
            var toB = project.FindReferencesTo(Path.Combine(root, "topics", "b.dita"));
            Check(toB.Count == 3, $"на b.dita три ссылки (xref, topicref, keydef): {toB.Count}");
            Check(toB.Any(h => h.Node.Name == "keydef") && toB.Any(h => h.Node.Name == "xref"), "среди ссылок — keydef и xref");

            var toA = project.FindReferencesTo(Path.Combine(root, "topics", "a.dita"));
            Check(toA.Count == 2 && toA.Any(h => h.Context.Contains("conref")),
                $"на a.dita — topicref и conref, ссылка файла на себя не считается: {string.Join("; ", toA.Select(h => h.Context))}");

            project.RemoveFile(Path.Combine(root, "topics", "b.dita"));
            Check(project.FindFile(Path.Combine(root, "topics", "b.dita")) is null, "RemoveFile убирает файл из проекта");
            Check(project.FindReferencesTo(Path.Combine(root, "topics", "a.dita")).Count == 1,
                "ссылки из убранного файла больше не находятся");
        });
    }

    /// <summary>П. 8а: вставка и удаление строк и столбцов, разделение ячеек.</summary>
    internal static void TableEditingTests()
    {
        Section("Строки и столбцы таблиц");
        var validator = new DitaValidator { CheckStyleRules = false };

        DitaDocument Doc(string body) => DitaDocument.Parse($"<concept id=\"c\"><title>T</title><conbody>{body}</conbody></concept>");
        DitaNode Cell(DitaDocument doc, string text) =>
            doc.Root.Descendants().First(n => n.Kind == NodeKind.Element && n.Name is "entry" or "stentry" or "propvalue" && n.InnerText == text);
        List<List<string>> Grid(DitaDocument doc) => doc.Root.Descendants()
            .Where(n => n.Name is "row" or "strow" or "sthead")
            .Select(r => r.ElementChildren().Select(e =>
                e.InnerText + (e.GetAttribute("namest") is { } ns ? $"[{ns}-{e.GetAttribute("nameend")}]" : string.Empty) +
                (e.GetAttribute("morerows") is { } mr ? $"v{mr}" : string.Empty)).ToList())
            .ToList();
        string Show(DitaDocument doc) => string.Join(" | ", Grid(doc).Select(r => string.Join(",", r)));
        void Valid(DitaDocument doc, string what)
        {
            var issues = validator.Validate(doc);
            Check(issues.Count == 0, $"{what}: таблица валидна" + (issues.Count == 0 ? string.Empty : ": " + issues[0].Message));
        }

        const string cals = """
<table><tgroup cols="2"><colspec colname="c1" colnum="1" colwidth="1*"/><colspec colname="c2" colnum="2" colwidth="2*"/>
<thead><row><entry>H1</entry><entry>H2</entry></row></thead>
<tbody>
<row><entry morerows="1">A</entry><entry>B</entry></row>
<row><entry>C</entry></row>
<row><entry namest="c1" nameend="c2">D</entry></row>
</tbody></tgroup></table>
""";

        var doc = Doc(cals);
        var focus = TableCommands.Apply(Cell(doc, "B"), TableOperation.InsertRowBelow);
        Check(Show(doc) == "H1,H2 | Av2,B |  | C | D[c1-c2]", "строка ниже B: A растягивается ещё на строку, в новой — одна ячейка: " + Show(doc));
        Check(focus?.GetAttribute("colname") == "c2", "новая ячейка привязана ко второму столбцу");
        Valid(doc, "вставка строки через объединение");

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "A"), TableOperation.InsertRowAbove);
        Check(Show(doc) == "H1,H2 | , | Av1,B | C | D[c1-c2]", "строка выше A — полная: " + Show(doc));

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "B"), TableOperation.DeleteRow);
        Check(Show(doc) == "H1,H2 | A,C | D[c1-c2]", "удаление строки A/B: A переезжает в следующую строку: " + Show(doc));
        Check(Cell(doc, "A").GetAttribute("colname") == "c1", "переехавшая ячейка привязана к своему столбцу");
        Valid(doc, "удаление строки с объединением");

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "C"), TableOperation.DeleteRow);
        Check(Show(doc) == "H1,H2 | A,B | D[c1-c2]", "удаление строки, в которую растянута A: A короче: " + Show(doc));

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "A"), TableOperation.InsertColumnRight);
        var tgroup = doc.Root.Descendants().First(n => n.Name == "tgroup");
        Check(tgroup.GetAttribute("cols") == "3", "cols = 3");
        Check(string.Join(",", tgroup.ElementChildren().Where(e => e.Name == "colspec").Select(e => e.GetAttribute("colname") + "#" + e.GetAttribute("colnum"))) == "c1#1,c3#2,c2#3",
            "новый colspec между первым и вторым, colnum перенумерованы");
        Check(Show(doc) == "H1,,H2 | Av1,,B | ,C | D[c1-c2]", "новый столбец справа от A; объединение D расширилось само: " + Show(doc));
        Valid(doc, "вставка столбца");

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "H1"), TableOperation.DeleteColumn);
        Check(Show(doc) == "H2 | B | C | D", "удаление первого столбца: объединение D стало обычной ячейкой: " + Show(doc));
        Check(Cell(doc, "D").GetAttribute("colname") == "c2" && doc.Root.Descendants().First(n => n.Name == "tgroup").GetAttribute("cols") == "1",
            "D привязана к оставшемуся столбцу, cols = 1");
        Valid(doc, "удаление столбца");
        Check(TableCommands.Apply(Cell(doc, "H2"), TableOperation.DeleteColumn) is null, "последний столбец не удаляется");

        doc = Doc(cals);
        TableCommands.Apply(Cell(doc, "D"), TableOperation.SplitCell);
        Check(Show(doc) == "H1,H2 | Av1,B | C | D,", "разделение D по горизонтали: " + Show(doc));
        TableCommands.Apply(Cell(doc, "A"), TableOperation.SplitCell);
        Check(Show(doc) == "H1,H2 | A,B | ,C | D,", "разделение A по вертикали — пустая ячейка в следующей строке: " + Show(doc));
        Valid(doc, "разделение ячеек");
        Check(TableCommands.Apply(Cell(doc, "B"), TableOperation.SplitCell) is null, "необъединённую ячейку делить нечего");

        doc = Doc("<table><tgroup cols=\"1\"><tbody><row><entry>X</entry></row></tbody></tgroup></table>");
        Check(TableCommands.Apply(Cell(doc, "X"), TableOperation.DeleteRow) is null, "единственная строка тела не удаляется");

        // simpletable и специализации
        doc = Doc("<simpletable relcolwidth=\"1* 2*\" keycol=\"2\"><sthead><stentry>H1</stentry><stentry>H2</stentry></sthead><strow><stentry>a</stentry><stentry>b</stentry></strow></simpletable>");
        TableCommands.Apply(Cell(doc, "a"), TableOperation.InsertColumnLeft);
        var simple = doc.Root.Descendants().First(n => n.Name == "simpletable");
        Check(Show(doc) == ",H1,H2 | ,a,b", "simpletable: столбец слева во всех строках: " + Show(doc));
        Check(simple.GetAttribute("relcolwidth") == "1* 1* 2*" && simple.GetAttribute("keycol") == "3", "relcolwidth и keycol сдвинуты");
        TableCommands.Apply(Cell(doc, "H1"), TableOperation.InsertRowAbove);
        Check(Grid(doc).Count == 2, "над шапкой строку не вставить");
        TableCommands.Apply(Cell(doc, "H1"), TableOperation.InsertRowBelow);
        Check(Show(doc) == ",H1,H2 | ,, | ,a,b", "строка под шапкой — первая строка данных: " + Show(doc));
        TableCommands.Apply(Cell(doc, "b"), TableOperation.DeleteColumn);
        Check(Show(doc) == ",H1 | , | ,a" && simple.GetAttribute("relcolwidth") == "1* 1*" && !simple.HasAttribute("keycol"),
            "удаление ключевого столбца: " + Show(doc));
        Valid(doc, "simpletable");

        doc = DitaDocument.Parse("<reference id=\"r\"><title>T</title><refbody><properties><property><proptype>t</proptype><propvalue>v</propvalue><propdesc>d</propdesc></property></properties></refbody></reference>");
        TableCommands.Apply(Cell(doc, "v"), TableOperation.InsertRowBelow);
        var props = doc.Root.Descendants().Where(n => n.Name == "property").ToList();
        Check(props.Count == 2 && string.Join(",", props[1].ElementChildren().Select(c => c.Name)) == "proptype,propvalue,propdesc",
            "properties: новая строка с теми же ячейками");
        Check(TableCommands.Apply(Cell(doc, "v"), TableOperation.InsertColumnRight) is null, "properties: столбцы заданы моделью");
        Valid(doc, "properties");
    }
}
