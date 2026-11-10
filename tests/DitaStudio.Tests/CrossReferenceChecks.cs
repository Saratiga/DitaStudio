using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Замечание: перекрёстные ссылки на рисунки, таблицы, нумерованные пункты и абзацы, сноски, разделы — как в Word
// («Вставить ссылку на»: подпись целиком, постоянная часть и номер, номер, текст). Номер — сквозной по изданию.
internal static partial class CoreChecks
{
    private const string XrefFirst = """
        <topic id="a"><title>Первый</title><body>
        <fig id="f1"><title>Схема</title></fig>
        <p>Сноска<fn id="n1">Пояснение</fn>.</p>
        </body></topic>
        """;

    // Ссылки стоят раньше целей (номер известен только после отрисовки цели) и ведут в другой топик.
    private const string XrefSecond = """
        <topic id="b"><title>Второй</title><body>
        <p>ПО_УМОЛЧАНИЮ: <xref href="#b/f2"/>; ЦЕЛИКОМ: <xref href="#b/f2" outputclass="xref-full"/>;
        НОМЕР: <xref href="#b/t1" outputclass="xref-number"/>; ТЕКСТ: <xref href="#b/t1" outputclass="xref-text"/>;
        ПУНКТ: <xref href="#b/s2"/>; АБЗАЦ: <xref href="#b/np"/>; СНОСКА: <xref href="a.dita#a/n1"/>;
        ДРУГОЙ: <xref href="a.dita#a/f1"/>; РАЗДЕЛ: <xref href="#b/sec"/>; СВОЙ: <xref href="#b/f2">своя подпись</xref>;
        ИСКЛЮЧЁН: <xref href="#b/gone"/>.</p>
        <fig id="f2"><title>Вид сбоку</title></fig>
        <table id="t1"><title>Параметры</title><tgroup cols="1"><tbody><row><entry>x</entry></row></tbody></tgroup></table>
        <ol><li>раз</li><li id="s2">два</li></ol>
        <p outputclass="numbered">Первый пункт</p><p outputclass="numbered" id="np">Второй пункт</p>
        <section id="sec"><title>Подробности</title><p>т</p></section>
        <fig id="gone" audience="internal"><title>Скрытый</title></fig>
        </body></topic>
        """;

    internal static void CrossReferenceTests()
    {
        Section("Перекрёстные ссылки на рисунки, таблицы, пункты, сноски, разделы");

        // Подписи по виду ссылки.
        var labels = new CrossReferenceLabels();
        labels.Record("x.dita", "f", XrefTargetKind.Figure, "Рисунок 4", "4", "Схема", " — ");
        Check(labels.TextFor("x.dita", "f", XrefForm.Default) == "Рисунок 4", "по умолчанию ссылка на рисунок — «Рисунок 4»");
        Check(labels.TextFor("x.dita", "f", XrefForm.Full) == "Рисунок 4 — Схема", "подпись целиком — с разделителем издания");
        Check(labels.TextFor("x.dita", "f", XrefForm.Number) == "4" && labels.TextFor("x.dita", "f", XrefForm.Text) == "Схема", "номер и текст отдельно");
        var token = labels.Token("X.DITA", "f", XrefForm.Label, "запас");
        Check(labels.Resolve("см. " + token + ".") == "см. Рисунок 4.", "метка заменяется подписью (путь без учёта регистра)");
        Check(labels.Resolve(labels.Token("x.dita", "нет", XrefForm.Default, "запас<>"), HtmlRenderer.Escape) == "запас&lt;&gt;",
            "цели нет в издании — запасной текст, экранированный");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = XrefFirst,
            ["b.dita"] = XrefSecond,
            ["m.ditamap"] = "<map><title>М</title><topicref href=\"a.dita\"/><topicref href=\"b.dita\"/></map>"
        }, (root, project) =>
        {
            var map = Path.Combine(root, "m.ditamap");
            var options = new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true, Language = "ru" };
            options.ExcludeConditions["audience"] = new HashSet<string> { "internal" };
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(map, options).EntryFile);
            string After(string marker) => html[(html.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Split(';')[0];
            string Link(string marker) => System.Text.RegularExpressions.Regex.Replace(After(marker), "<[^>]+>", string.Empty).Trim();

            Check(Link("ПО_УМОЛЧАНИЮ:") == "Рисунок 2", $"HTML: пустая ссылка на рисунок — сквозной номер издания ({Link("ПО_УМОЛЧАНИЮ:")})");
            Check(Link("ЦЕЛИКОМ:") == "Рисунок 2. Вид сбоку", $"HTML: подпись целиком ({Link("ЦЕЛИКОМ:")})");
            Check(Link("НОМЕР:") == "1" && Link("ТЕКСТ:") == "Параметры", $"HTML: номер и текст таблицы ({Link("НОМЕР:")}, {Link("ТЕКСТ:")})");
            Check(Link("ПУНКТ:") == "2", $"HTML: пункт нумерованного списка — его номер ({Link("ПУНКТ:")})");
            Check(Link("АБЗАЦ:") == "2", $"HTML: нумерованный абзац — его номер ({Link("АБЗАЦ:")})");
            Check(Link("СНОСКА:") == "1", $"HTML: сноска в другом топике — её номер ({Link("СНОСКА:")})");
            Check(Link("ДРУГОЙ:") == "Рисунок 1", $"HTML: рисунок другого топика ({Link("ДРУГОЙ:")})");
            Check(Link("РАЗДЕЛ:") == "Подробности", $"HTML: раздел — его название ({Link("РАЗДЕЛ:")})");
            Check(Link("СВОЙ:") == "своя подпись", "HTML: свой текст ссылки остаётся");
            Check(Link("ИСКЛЮЧЁН:").Length > 0 && !Link("ИСКЛЮЧЁН:").Contains("Рисунок 3"), $"HTML: цель исключена условием — запасной текст ({Link("ИСКЛЮЧЁН:")})");
            Check(!CrossReferenceLabels.HasTokens(html), "HTML: меток не осталось");
            Check(html.Contains("Рисунок 2. Вид сбоку</figcaption>"), "HTML: подпись рисунка как прежде");

            // Постраничный HTML: ссылка на рисунок другой страницы — тоже сквозной номер.
            var pages = new PublishOptions { OutputDirectory = Path.Combine(root, "pages"), Language = "ru" };
            new HtmlPublisher(project).Publish(map, pages);
            var pageB = Directory.GetFiles(pages.OutputDirectory, "*.html").Select(File.ReadAllText).First(t => t.Contains("ДРУГОЙ:"));
            Check(pageB.Contains("Рисунок 1</a>") && !CrossReferenceLabels.HasTokens(pageB), "HTML по страницам: номер рисунка из другой страницы, меток нет");

            // Предпросмотр одного топика: свои номера, метки заменяются сразу.
            var preview = new HtmlPublisher(project).RenderPreview(project.TryGetDocument(Path.Combine(root, "b.dita"))!);
            Check(!CrossReferenceLabels.HasTokens(preview) && preview.Contains("Рисунок 1</a>"), "предпросмотр топика: номер по топику, меток нет");

            // DOCX: тот же текст, ссылки — на закладки целей; ссылка на исключённую цель — просто текст.
            var docx = Path.Combine(root, "d.docx");
            var docxOptions = new PublishOptions { Language = "ru" };
            docxOptions.ExcludeConditions["audience"] = new HashSet<string> { "internal" };
            new DocxPublisher(project).Publish(map, docxOptions, docx);
            CheckValidDocx(docx, "перекрёстные ссылки");
            using var document = WordprocessingDocument.Open(docx, false);
            var body = document.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;
            string DocxLink(string marker) => text[(text.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Split(';')[0].Trim();
            Check(DocxLink("ПО_УМОЛЧАНИЮ:") == "Рисунок 2" && DocxLink("ЦЕЛИКОМ:") == "Рисунок 2. Вид сбоку",
                $"DOCX: рисунок — номер и подпись ({DocxLink("ПО_УМОЛЧАНИЮ:")}, {DocxLink("ЦЕЛИКОМ:")})");
            Check(DocxLink("НОМЕР:") == "1" && DocxLink("ПУНКТ:") == "2" && DocxLink("СНОСКА:") == "1" && DocxLink("ДРУГОЙ:") == "Рисунок 1",
                $"DOCX: таблица, пункт, сноска, рисунок другого топика ({DocxLink("НОМЕР:")}, {DocxLink("ПУНКТ:")}, {DocxLink("СНОСКА:")}, {DocxLink("ДРУГОЙ:")})");
            Check(!CrossReferenceLabels.HasTokens(text), "DOCX: меток не осталось");
            var bookmarks = body.Descendants<W.BookmarkStart>().Select(b => b.Name!.Value).ToHashSet();
            var links = body.Descendants<W.Hyperlink>().Where(h => h.Anchor?.Value?.StartsWith("_Ref") == true).ToList();
            Check(links.Count >= 6 && links.All(h => bookmarks.Contains(h.Anchor!.Value!)), $"DOCX: ссылки ведут на закладки целей ({links.Count})");
            Check(bookmarks.Contains(CrossReferenceLabels.BookmarkName(Path.Combine(root, "b.dita"), "f2")), "DOCX: у рисунка — закладка");
            Check(!bookmarks.Contains(CrossReferenceLabels.BookmarkName(Path.Combine(root, "b.dita"), "gone")) &&
                  !links.Any(h => h.Anchor!.Value == CrossReferenceLabels.BookmarkName(Path.Combine(root, "b.dita"), "gone")),
                "DOCX: на исключённую цель ссылки-гиперссылки нет");

            // Цели для окна вставки ссылки.
            var second = project.TryGetDocument(Path.Combine(root, "b.dita"))!;
            var figures = XrefTargets.In(second, XrefTargetKind.Figure);
            Check(figures.Count == 2 && figures[0].Display(Labels.Russian) == "Рисунок 1. Вид сбоку", $"окно: рисунки документа ({string.Join(" | ", figures.Select(f => f.Display(Labels.Russian)))})");
            var items = XrefTargets.In(second, XrefTargetKind.NumberedItem);
            Check(items.Select(i => i.Display(Labels.Russian)).SequenceEqual(new[] { "1. раз", "2. два", "1. Первый пункт", "2. Второй пункт" }),
                $"окно: пункты списков и нумерованные абзацы ({string.Join(" | ", items.Select(i => i.Display(Labels.Russian)))})");
            Check(XrefTargets.In(second, XrefTargetKind.Table).Single().TopicId == "b" && XrefTargets.In(second, XrefTargetKind.Section).Single().Text == "Подробности",
                "окно: таблица и раздел с топиком");
            var first = project.TryGetDocument(Path.Combine(root, "a.dita"))!;
            Check(XrefTargets.In(first, XrefTargetKind.Footnote).Single().Display(Labels.Russian) == "1. Пояснение", "окно: сноски");

            var unnamed = items[0];
            var (id, added) = XrefTargets.EnsureId(second, unnamed);
            Check(added && id == "item_1" && unnamed.Node.GetAttribute("id") == "item_1" && second.IsDirty, $"цель без id получает уникальный id ({id})");
            Check(XrefTargets.EnsureId(second, items[1]) == ("s2", false), "цель с id — свой id, документ не трогается");
            Check(XrefTargets.Href(Path.Combine(root, "a.dita"), Path.Combine(root, "b.dita"), items[1], "s2") == "b.dita#b/s2", "href из другого файла — файл#топик/элемент");
            Check(XrefTargets.Href(Path.Combine(root, "b.dita"), Path.Combine(root, "b.dita"), items[1], "s2") == "#b/s2", "href в своём файле — #топик/элемент");
        });
    }
}
