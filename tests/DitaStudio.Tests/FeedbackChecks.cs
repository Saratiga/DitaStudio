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
}
