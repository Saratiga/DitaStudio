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

    /// <summary>П. 2: размер бумаги, ориентация и поля — в DOCX и в @page для PDF.</summary>
    internal static void PageSetupTests()
    {
        Section("Параметры страницы");

        var layout = new DocxLayout { PaperSize = "a5", Landscape = true, MarginTopMm = 10, MarginLeftMm = 250, MarginRightMm = double.NaN };
        layout.Normalize();
        Check(layout.PaperSize == "A5", $"размер бумаги приводится к имени из списка: {layout.PaperSize}");
        Check(layout.MarginLeftMm == 100 && layout.MarginRightMm is null, "поля ограничены 0–100 мм, NaN — «по CSS»");
        var restored = DocxLayout.FromJson(layout.ToJson());
        Check(restored.PaperSize == "A5" && restored.Landscape && restored.MarginTopMm == 10 && restored.MarginBottomMm is null,
            "параметры страницы переживают сохранение в JSON");
        Check(new DocxLayout { PaperSize = "Tabloid" }.Clone().PaperSize == string.Empty, "неизвестный размер — «как в CSS»");
        Check(new DocxLayout().PageCss() == string.Empty, "без параметров страницы @page не добавляется");
        Check(layout.PageCss().Contains("@page { size: A5 landscape; margin-top: 10mm; margin-left: 100mm; }"),
            "правило @page: " + layout.PageCss());
        Check(new DocxLayout { Landscape = true }.PageCss().Contains("size: landscape;"), "только ориентация — size: landscape");

        // Свой размер листа
        var custom = new DocxLayout { PaperSize = "A4", PaperWidthMm = 100, PaperHeightMm = 2500 };
        custom.Normalize();
        Check(custom.PaperWidthMm == 100 && custom.PaperHeightMm == DocxLayout.MaxPaperMm && custom.PaperSize == string.Empty,
            $"свой размер: высота ограничена {DocxLayout.MaxPaperMm} мм, имя формата сбрасывается ({custom.PaperWidthMm} × {custom.PaperHeightMm})");
        Check(custom.PaperMm() == (100, 2000), "PaperMm возвращает свой размер");
        var tooSmall = new DocxLayout { PaperWidthMm = 10, PaperHeightMm = 20 };
        tooSmall.Normalize();
        Check(tooSmall.PaperWidthMm == DocxLayout.MinPaperMm && tooSmall.PaperHeightMm == DocxLayout.MinPaperMm, "свой размер: не меньше 50 мм");
        var half = new DocxLayout { PaperSize = "A5", PaperWidthMm = 120 };
        half.Normalize();
        Check(!half.HasCustomPaper && half.PaperWidthMm is null && half.PaperSize == "A5" && half.PaperMm() == (148, 210),
            "свой размер задан не полностью (только ширина) — не действует, остаётся выбранный формат");
        var nan = new DocxLayout { PaperWidthMm = double.NaN, PaperHeightMm = 200 };
        nan.Normalize();
        Check(!nan.HasCustomPaper, "свой размер: NaN — «как в CSS»");
        var restoredCustom = DocxLayout.FromJson(new DocxLayout { PaperWidthMm = 120.5, PaperHeightMm = 300, Landscape = true }.ToJson());
        Check(restoredCustom.PaperWidthMm == 120.5 && restoredCustom.PaperHeightMm == 300 && restoredCustom.Landscape,
            "свой размер переживает сохранение в JSON");
        var customCss = new DocxLayout { PaperWidthMm = 120.5, PaperHeightMm = 300 }.PageCss();
        Check(customCss.Contains("size: 120.5mm 300mm;"), "правило @page для своего размера: " + customCss);
        Check(new DocxLayout { PaperWidthMm = 120, PaperHeightMm = 300, Landscape = true }.PageCss().Contains("size: 300mm 120mm;"),
            "своя альбомная страница — размеры «шире, чем выше» (landscape с длинами в CSS не сочетается)");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<concept id=\"a\"><title>A</title><conbody><p>Текст</p></conbody></concept>",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { PaperSize = "A5", Landscape = true, MarginTopMm = 10, MarginBottomMm = 15 });
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с параметрами страницы");
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var section = package.MainDocumentPart!.Document.Body!.Elements<W.SectionProperties>().Last();
                var size = section.GetFirstChild<W.PageSize>()!;
                var margin = section.GetFirstChild<W.PageMargin>()!;
                Check(Math.Abs((int)size.Width!.Value - 11906) <= 2 && Math.Abs((int)size.Height!.Value - 8391) <= 2,
                    $"DOCX: A5 альбомная — 210 × 148 мм: {size.Width.Value} × {size.Height.Value}");
                Check(size.Orient?.Value == W.PageOrientationValues.Landscape, "DOCX: ориентация альбомная");
                Check(Math.Abs(margin.Top!.Value - 567) <= 1 && Math.Abs(margin.Bottom!.Value - 850) <= 1,
                    $"DOCX: поля 10 и 15 мм: {margin.Top.Value}, {margin.Bottom.Value}");
                Check(Math.Abs((int)margin.Left!.Value - 1134) <= 1, $"DOCX: незаданное поле — по умолчанию 20 мм: {margin.Left.Value}");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            Check(File.ReadAllText(single.EntryFile).Contains("@page { size: A5 landscape; margin-top: 10mm; margin-bottom: 15mm; }"),
                "HTML для печати в PDF: правило @page из параметров страницы");

            // Свой размер листа: 100 × 200 мм книжная, затем альбомная
            project.SetDocxLayout(new DocxLayout { PaperWidthMm = 100, PaperHeightMm = 200 });
            var customFile = Path.Combine(root, "custom.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, customFile);
            CheckValidDocx(customFile, "DOCX со своим размером листа");
            using (var package = WordprocessingDocument.Open(customFile, false))
            {
                var size = package.MainDocumentPart!.Document.Body!.Elements<W.SectionProperties>().Last().GetFirstChild<W.PageSize>()!;
                Check(Math.Abs((int)size.Width!.Value - 5669) <= 2 && Math.Abs((int)size.Height!.Value - 11339) <= 2,
                    $"DOCX: свой размер 100 × 200 мм — {size.Width.Value} × {size.Height.Value} twips");
                Check(size.Orient?.Value != W.PageOrientationValues.Landscape, "DOCX: свой размер — книжная ориентация");
            }

            project.SetDocxLayout(new DocxLayout { PaperWidthMm = 100, PaperHeightMm = 200, Landscape = true });
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, customFile);
            using (var package = WordprocessingDocument.Open(customFile, false))
            {
                var size = package.MainDocumentPart!.Document.Body!.Elements<W.SectionProperties>().Last().GetFirstChild<W.PageSize>()!;
                Check(Math.Abs((int)size.Width!.Value - 11339) <= 2 && Math.Abs((int)size.Height!.Value - 5669) <= 2 &&
                      size.Orient?.Value == W.PageOrientationValues.Landscape,
                    $"DOCX: свой размер, альбомная — {size.Width.Value} × {size.Height.Value} twips");
            }

            var customHtml = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out2"), SingleFile = true });
            Check(File.ReadAllText(customHtml.EntryFile).Contains("@page { size: 200mm 100mm; }"),
                "HTML для печати в PDF: @page со своим размером");
        });
    }

    /// <summary>П. 14: оглавление DOCX — сразу со строками-ссылками, а не с текстом-заглушкой.</summary>
    internal static void TocTests()
    {
        Section("Оглавление DOCX");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<concept id=\"a\"><title>Глава А</title><conbody><p>Текст</p></conbody></concept>",
            ["a1.dita"] = "<concept id=\"a1\"><title>Раздел А.1</title><conbody><p>Текст</p></conbody></concept>",
            ["a11.dita"] = "<concept id=\"a11\"><title>Пункт А.1.1</title><conbody><p>Текст</p></conbody></concept>",
            ["t.dita"] = "<reference id=\"t\"><title/><refbody><p>Только таблица</p></refbody></reference>",
            ["b.dita"] = "<concept id=\"b\"><title>Глава Б</title><conbody><p>Текст</p></conbody></concept>",
            ["guide.ditamap"] = """
<map><title>Книга</title>
  <topicref href="a.dita"><topicref href="a1.dita"><topicref href="a11.dita"/></topicref><topicref href="t.dita"/></topicref>
  <topicref href="b.dita"/>
</map>
"""
        }, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { TocDepth = 2 });
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с готовым оглавлением");

            using var package = WordprocessingDocument.Open(outFile, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var rows = body.Elements<W.Paragraph>().Where(IsTocRow).ToList();
            var shown = string.Join(" | ", rows.Select(r => r.ParagraphProperties!.ParagraphStyleId!.Val!.Value + ":" +
                                                         string.Concat(r.Descendants<W.Hyperlink>().Select(h => h.InnerText))));
            Check(shown == "TOC1:Глава А | TOC2:Раздел А.1 | TOC1:Глава Б",
                "строки оглавления — по уровням, глубина 2, без топика без заголовка: " + shown);
            var bookmarks = body.Descendants<W.BookmarkStart>().Select(b => b.Name?.Value).ToHashSet();
            Check(rows.All(r => r.Descendants<W.Hyperlink>().SingleOrDefault()?.Anchor?.Value is { } anchor && bookmarks.Contains(anchor)),
                "каждая строка — ссылка на закладку топика");
            var chars = body.Descendants<W.FieldChar>().Where(f => f.Ancestors<W.Paragraph>().First() is var p && rows.Contains(p)).ToList();
            Check(chars.Select(c => c.FieldCharType!.Value).SequenceEqual(new[] { W.FieldCharValues.Begin, W.FieldCharValues.Separate, W.FieldCharValues.End }) &&
                  chars[0].Dirty?.Value == true,
                "поле TOC обрамляет строки (начало, разделитель, конец) и помечено к обновлению");
            Check(body.Descendants<W.FieldCode>().Any(f => f.Text.Contains("TOC \\o \"1-2\"")), "команда поля — глубина 2");
            Check(!body.Descendants<W.Text>().Any(t => t.Text.Contains("Обновите оглавление")), "текста-заглушки нет");
            var styles = package.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<W.Style>().Select(st => st.StyleId?.Value).ToHashSet();
            Check(styles.Contains("TOC1") && styles.Contains("TOC2"), "стили строк оглавления определены");
        });
    }

    /// <summary>П. 21: заголовок без номера не нумеруется и не попадает в оглавление; своё название оглавления.</summary>
    internal static void UnnumberedTitleTests()
    {
        Section("Заголовок без номера");

        WithProject(new Dictionary<string, string>
        {
            ["intro.dita"] = "<concept id=\"intro\"><title outputclass=\"nonumber\">Введение</title><conbody><p>Текст</p></conbody></concept>",
            ["main.dita"] = "<concept id=\"main\"><title>Основная глава</title><conbody><section><title outputclass=\"nonumber\">Справочно</title><p>x</p></section></conbody></concept>",
            ["annex.dita"] = "<concept id=\"annex\"><title>Приложение</title><conbody><p>Текст</p></conbody></concept>",
            ["guide.ditamap"] = """
<map><title>Книга</title><topicref href="intro.dita"/><topicref href="main.dita"/><topicref href="annex.dita" toc="no"/></map>
"""
        }, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { NumberHeadings = true, TocTitle = "Оглавление" });
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с заголовками без номера");

            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                string? StyleOf(string text) => body.Elements<W.Paragraph>().FirstOrDefault(p => !IsTocRow(p) && p.InnerText == text)
                    ?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                Check(StyleOf("Введение") == "HeadingPlain1", $"title с nonumber — стиль без номера: {StyleOf("Введение")}");
                Check(StyleOf("Основная глава") == "Heading1", "обычный заголовок — Heading1");
                Check(StyleOf("Справочно") == "HeadingPlain2", $"заголовок раздела с nonumber: {StyleOf("Справочно")}");
                Check(StyleOf("Приложение") == "HeadingPlain1", $"строка карты с toc=\"no\" — тоже без номера: {StyleOf("Приложение")}");

                var plain = package.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<W.Style>().Single(st => st.StyleId == "HeadingPlain1");
                Check(plain.BasedOn?.Val?.Value == "Heading1", "стиль без номера основан на Heading1 (тот же вид)");
                Check(plain.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value == 0, "нумерация в стиле снята (numId 0)");
                Check(plain.StyleParagraphProperties?.OutlineLevel?.Val?.Value == 9, "уровень структуры — основной текст: Word не соберёт его в оглавление");

                var rows = body.Elements<W.Paragraph>().Where(IsTocRow).Select(r => string.Concat(r.Descendants<W.Hyperlink>().Select(h => h.InnerText))).ToList();
                Check(string.Join(" | ", rows) == "Основная глава", "в готовом оглавлении только нумеруемый топик: " + string.Join(" | ", rows));
                Check(body.Elements<W.Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TOCHeading" && p.InnerText == "Оглавление"),
                    "своё название оглавления");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            var toc = html[html.IndexOf("toc-inline", StringComparison.Ordinal)..html.IndexOf("</nav>", StringComparison.Ordinal)];
            Check(toc.Contains("<h2>Оглавление</h2>") && toc.Contains("Основная глава") && !toc.Contains("Введение") && !toc.Contains("Приложение"),
                "HTML: оглавление со своим названием, без заголовков без номера и toc=\"no\"");
        });
    }

    /// <summary>П. 22: флажок «публиковать» у строки карты — processing-role с наследованием.</summary>
    internal static void ExcludeFromPublicationTests()
    {
        Section("Исключение топиков из публикации");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<concept id=\"a\"><title>Глава А</title><conbody><p>Текст А</p></conbody></concept>",
            ["draft.dita"] = "<concept id=\"draft\"><title>Черновик</title><conbody><p id=\"shared\">Общий абзац</p></conbody></concept>",
            ["sub.dita"] = "<concept id=\"sub\"><title>Подраздел черновика</title><conbody><p>Текст П</p></conbody></concept>",
            ["back.dita"] = "<concept id=\"back\"><title>Возвращённый</title><conbody><p>Текст В</p></conbody></concept>",
            ["b.dita"] = "<concept id=\"b\"><title>Глава Б</title><conbody><p conref=\"draft.dita#draft/shared\"/></conbody></concept>",
            ["guide.ditamap"] = """
<map><title>Книга</title>
  <topicref href="a.dita"/>
  <topicref href="draft.dita" processing-role="resource-only">
    <topicref href="sub.dita"/>
    <topicref href="back.dita" processing-role="normal"/>
  </topicref>
  <topicref href="b.dita"/>
</map>
"""
        }, (root, project) =>
        {
            var tree = MapTree.Build(project, Path.Combine(root, "guide.ditamap"));
            var order = string.Join(",", tree.PublicationOrder.Select(i => Path.GetFileNameWithoutExtension(i.TargetPath)));
            Check(order == "a,back,b", "исключение наследуется ветке, потомок может вернуть normal: " + order);
            Check(tree.Items.Single(i => i.Href == "sub.dita").IsExcluded, "подраздел исключённой ветки помечен исключённым");

            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var text = package.MainDocumentPart!.Document.Body!.InnerText;
                Check(!text.Contains("Черновик") && !text.Contains("Текст П") && text.Contains("Возвращённый"), "DOCX: исключённых топиков нет");
                Check(text.Contains("Общий абзац"), "DOCX: conref из исключённого топика по-прежнему работает");
            }

            var site = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "site") });
            var index = File.ReadAllText(site.EntryFile);
            Check(!site.Files.Any(f => f.Contains("draft", StringComparison.OrdinalIgnoreCase) || f.Contains("sub", StringComparison.OrdinalIgnoreCase)),
                "сайт: страниц исключённых топиков нет: " + string.Join(", ", site.Files.Select(Path.GetFileName)));
            Check(!index.Contains(">Черновик</a>") && !index.Contains("Подраздел черновика"),
                "сайт: исключённые топики не в навигации (у ветки с возвращённым потомком — только подпись без ссылки)");

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "single"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(!html.Contains("Черновик") && html.Contains("Общий абзац"), "единый HTML (и PDF): исключённых нет, conref работает");
        });
    }

    /// <summary>П. 7: картинка (логотип) в колонтитулах DOCX; для PDF — data-URI.</summary>
    internal static void HeaderImageTests()
    {
        Section("Картинка в колонтитуле");
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<concept id=\"a\"><title>A</title><conbody><p>Текст</p></conbody></concept>",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            File.WriteAllBytes(Path.Combine(root, "images", "logo.png"), Convert.FromBase64String(png));
            Check(DocxLayout.ResolveImage(root, "images/logo.png") is not null && DocxLayout.ResolveImage(root, "images/none.png") is null,
                "путь картинки разрешается от папки проекта, отсутствующая — null");
            Check(DocxLayout.ImageDataUri(DocxLayout.ResolveImage(root, "images/logo.png")!).StartsWith("data:image/png;base64,iVBOR", StringComparison.Ordinal),
                "data-URI для шаблона колонтитула PDF");

            project.SetDocxLayout(new DocxLayout
            {
                TitlePage = false,
                NoHeaderOnFirstPage = false,
                HeaderText = "Руководство",
                HeaderAlignment = DocxHeaderAlignment.Right,
                HeaderImage = "images/logo.png",
                HeaderImageAlignment = DocxHeaderAlignment.Left,
                HeaderImageHeightMm = 12,
                FooterImage = "images/missing.png"
            });
            var outFile = Path.Combine(root, "book.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с логотипом в колонтитуле");
            Check(result.Warnings.Any(w => w.Contains("missing.png")), "отсутствующая картинка нижнего колонтитула — предупреждение");

            using var package = WordprocessingDocument.Open(outFile, false);
            var header = package.MainDocumentPart!.HeaderParts.Single();
            var paragraph = header.Header!.Elements<W.Paragraph>().Single();
            var drawing = paragraph.Descendants<W.Drawing>().SingleOrDefault();
            Check(drawing is not null && header.ImageParts.Count() == 1, "картинка встроена в сам колонтитул");
            var blip = paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Single().Embed!.Value!;
            Check(header.GetPartById(blip) is ImagePart, "ссылка картинки ведёт на часть колонтитула");
            var extent = paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single();
            Check(extent.Cy!.Value == 12 * 36000 && extent.Cx!.Value == 12 * 36000, $"высота 12 мм, ширина по пропорциям: {extent.Cx.Value}×{extent.Cy.Value}");
            Check(paragraph.ParagraphProperties?.Tabs?.Elements<W.TabStop>().Count() == 2, "позиции табуляции по центру и справа");
            var children = paragraph.ChildElements.Where(e => e is not W.ParagraphProperties).ToList();
            var drawingIndex = children.FindIndex(e => e.Descendants<W.Drawing>().Any());
            var textIndex = children.FindIndex(e => e.InnerText == "Руководство");
            var tabs = children.Count(e => e.Descendants<W.TabChar>().Any());
            Check(drawingIndex == 0 && textIndex > drawingIndex && tabs == 2, "логотип слева, текст справа после двух табуляций");
            Check(package.MainDocumentPart.FooterParts.All(f => !f.Footer!.Descendants<W.Drawing>().Any()), "без найденной картинки подвала рисунка нет");
        });
    }

    /// <summary>Абзацы DOCX с текстом, содержащим <paramref name="text"/>.</summary>
    private static W.Paragraph DocxParagraph(W.Body body, string text) =>
        body.Descendants<W.Paragraph>().First(p => !IsTocRow(p) && p.InnerText.Contains(text));

    /// <summary>Итоговое выравнивание абзаца DOCX: прямое или из стиля (с цепочкой basedOn).</summary>
    private static string? DocxJustification(WordprocessingDocument package, W.Paragraph paragraph)
    {
        if (paragraph.ParagraphProperties?.Justification?.Val?.ToString() is { } direct)
        {
            return direct;
        }

        var styles = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<W.Style>().ToDictionary(st => st.StyleId!.Value!);
        for (var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value; id is not null && styles.TryGetValue(id, out var style); id = style.BasedOn?.Val?.Value)
        {
            if (style.StyleParagraphProperties?.Justification?.Val?.ToString() is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>П. 15: выравнивание абзацев, заголовков и ячеек — классы align-… и @align ячеек.</summary>
    internal static void TextAlignmentTests()
    {
        Section("Выравнивание текста");

        var p = DitaDocument.Parse("<p outputclass=\"keep align-left\">x</p>").Root;
        Check(TextFormatting.SetToken(p, TextFormatting.AlignPrefix, "align-center") && p.GetAttribute("outputclass") == "keep align-center",
            "класс группы заменяется, чужие классы остаются: " + p.GetAttribute("outputclass"));
        Check(TextFormatting.AlignmentOf(p) == "center", "выравнивание читается из класса");
        TextFormatting.SetToken(p, TextFormatting.AlignPrefix, null);
        Check(p.GetAttribute("outputclass") == "keep", "null снимает класс группы");
        Check(TextFormatting.Css.Contains(".align-center { text-align: center; }") && TextFormatting.Css.Contains(".size-10 { font-size: 10pt; }"),
            "встроенный CSS классов оформления");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = """
<concept id="a"><title outputclass="align-center">Заголовок по центру</title><conbody>
<p outputclass="align-right">Абзац справа</p><p>Абзац обычный</p>
<table><tgroup cols="1"><tbody><row><entry align="center">Ячейка по центру</entry></row></tbody></tgroup></table>
</conbody></concept>
""",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с выравниванием");
            Check(result.Warnings.Count == 0, "встроенный CSS оформления не даёт предупреждений: " + string.Join("; ", result.Warnings));
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                Check(DocxJustification(package, DocxParagraph(body, "Абзац справа")) == "right", "DOCX: абзац справа — " + DocxJustification(package, DocxParagraph(body, "Абзац справа")));
                Check(DocxJustification(package, DocxParagraph(body, "Заголовок по центру")) == "center", "DOCX: заголовок по центру");
                Check(DocxJustification(package, DocxParagraph(body, "Абзац обычный")) is null or "left" or "both", "DOCX: обычный абзац не выровнен по центру/справа");
                Check(DocxJustification(package, DocxParagraph(body, "Ячейка по центру")) == "center", "DOCX: ячейка по @align");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("class=\"align-right\"") && html.Contains(".align-right { text-align: right; }"), "HTML: класс и его CSS");
        });
    }

    /// <summary>Итоговое значение свойства прогона DOCX: прямое, из стиля знака, из стиля абзаца (с basedOn).</summary>
    private static string? DocxRunValue(WordprocessingDocument package, W.Run run, Func<W.RunProperties?, W.StyleRunProperties?, string?> read)
    {
        if (read(run.RunProperties, null) is { } direct)
        {
            return direct;
        }

        var styles = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<W.Style>().ToDictionary(st => st.StyleId!.Value!);
        string? FromStyle(string? id)
        {
            for (; id is not null && styles.TryGetValue(id, out var style); id = style.BasedOn?.Val?.Value)
            {
                if (read(null, style.StyleRunProperties) is { } value)
                {
                    return value;
                }
            }

            return null;
        }

        return FromStyle(run.RunProperties?.RunStyle?.Val?.Value)
               ?? FromStyle(run.Ancestors<W.Paragraph>().First().ParagraphProperties?.ParagraphStyleId?.Val?.Value);
    }

    /// <summary>П. 16: размер шрифта фразы и абзаца — классы size-N.</summary>
    internal static void TextSizeTests()
    {
        Section("Размер шрифта");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = """
<concept id="a"><title>T</title><conbody>
<p>Обычный <ph outputclass="size-10">мелкий</ph> текст</p>
<p outputclass="size-14">Крупный абзац</p>
</conbody></concept>
""",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с размерами шрифта");
            Check(result.Warnings.Count == 0, "без предупреждений: " + string.Join("; ", result.Warnings));
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                string? Size(string text) => DocxRunValue(package, body.Descendants<W.Run>().First(r => r.InnerText.Contains(text)),
                    (direct, style) => direct?.FontSize?.Val?.Value ?? style?.FontSize?.Val?.Value);
                Check(Size("мелкий") == "20", $"DOCX: фраза size-10 — 10 пт: {Size("мелкий")}");
                Check(Size("Крупный") == "28", $"DOCX: абзац size-14 — 14 пт: {Size("Крупный")}");
                Check(Size("Обычный") != "20", "DOCX: остальной текст абзаца не мельчает");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("class=\"size-10\"") && html.Contains(".size-10 { font-size: 10pt; }"), "HTML: класс фразы и его CSS");
        });
    }

    /// <summary>П. 20: цвет текста — классы color-….</summary>
    internal static void TextColorTests()
    {
        Section("Цвет текста");
        Check(TextFormatting.ColorOf(DitaDocument.Parse("<ph outputclass=\"color-green\">x</ph>").Root) == "#00873C", "цвет читается из класса");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = """
<concept id="a"><title>T</title><conbody>
<p>Обычный <ph outputclass="color-red">красный</ph> текст</p>
<p outputclass="color-blue">Синий абзац</p>
</conbody></concept>
""",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с цветом текста");
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                string? Color(string text) => DocxRunValue(package, body.Descendants<W.Run>().First(r => r.InnerText.Contains(text)),
                    (direct, style) => direct?.Color?.Val?.Value ?? style?.Color?.Val?.Value);
                Check(Color("красный")?.ToUpperInvariant() == "C00000", $"DOCX: фраза color-red: {Color("красный")}");
                Check(Color("Синий")?.ToUpperInvariant() == "1F5FBF", $"DOCX: абзац color-blue: {Color("Синий")}");
                Check(Color("Обычный")?.ToUpperInvariant() != "C00000", "DOCX: остальной текст не красный");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            Check(File.ReadAllText(single.EntryFile).Contains(".color-red { color: #C00000; }"), "HTML: CSS цвета");
        });
    }

    /// <summary>П. 8б: ширина столбцов (доли) и высота строк (row-height-Nmm) в публикации.</summary>
    internal static void TableResizeTests()
    {
        Section("Ширина столбцов и высота строк");

        var fractions = TableLayout.Fractions(new[] { "1*", "3*" })!;
        Check(Math.Abs(fractions[0] - 0.25) < 1e-9 && Math.Abs(fractions[1] - 0.75) < 1e-9, "доли 1*:3*");
        Check(TableLayout.Fractions(new[] { "20mm", "60mm" }) is { } abs && Math.Abs(abs[0] - 0.25) < 1e-6, "абсолютные — пропорционально");
        Check(TableLayout.Fractions(new[] { null, "" }) is null, "без ширин — null");
        var tgroup = DitaDocument.Parse("<tgroup cols=\"2\"><tbody><row><entry>a</entry><entry>b</entry></row></tbody></tgroup>").Root;
        TableLayout.SetCalsWidths(tgroup, new[] { 150.0, 50.0 });
        Check(string.Join(",", tgroup.ElementChildren().Where(e => e.Name == "colspec").Select(c => c.GetAttribute("colwidth"))) == "75*,25*",
            "запись долей создаёт colspec и пишет «N*»");
        var row = tgroup.Descendants().First(n => n.Name == "row");
        TableLayout.SetRowHeight(row, 12.4);
        Check(row.GetAttribute("outputclass") == "row-height-12mm" && TableLayout.RowHeightMm(row) == 12, "высота строки — класс row-height-12mm");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = """
<concept id="a"><title>T</title><conbody>
<table><tgroup cols="2"><colspec colname="c1" colwidth="1*"/><colspec colname="c2" colwidth="3*"/><tbody>
<row outputclass="row-height-15mm"><entry>Узкий</entry><entry>Широкий</entry></row>
<row><entry namest="c1" nameend="c2">Объединённая</entry></row>
</tbody></tgroup></table>
<simpletable relcolwidth="2* 1*"><strow><stentry>Левый</stentry><stentry>Правый</stentry></strow></simpletable>
</conbody></concept>
""",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        }, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с шириной столбцов и высотой строк");
            using (var package = WordprocessingDocument.Open(outFile, false))
            {
                var body = package.MainDocumentPart!.Document.Body!;
                string? Width(string text) => body.Descendants<W.TableCell>().First(c => c.InnerText == text).TableCellProperties?.TableCellWidth?.Width?.Value;
                Check(Width("Узкий") == "1250" && Width("Широкий") == "3750", $"DOCX: ячейки 25 % и 75 %: {Width("Узкий")}, {Width("Широкий")}");
                Check(Width("Объединённая") == "5000", "DOCX: объединённая ячейка — вся ширина");
                Check(Width("Левый") == "3333" && Width("Правый") == "1667", $"DOCX: simpletable 2:1: {Width("Левый")}, {Width("Правый")}");
                var height = body.Descendants<W.TableRow>().First(r => r.InnerText.Contains("Узкий")).TableRowProperties?.GetFirstChild<W.TableRowHeight>();
                Check(height?.Val?.Value == 850 && height.HeightType?.Value == W.HeightRuleValues.AtLeast, "DOCX: строка не ниже 15 мм");
            }

            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("<col style=\"width:25%\" />") && html.Contains("<col style=\"width:75%\" />"), "HTML: столбцы CALS в процентах");
            Check(html.Contains("<col style=\"width:66.67%\" />"), "HTML: столбцы simpletable в процентах");
            Check(html.Contains("<tr style=\"height:15mm\">"), "HTML: высота строки");
        });
    }

    /// <summary>П. 19: нумерованные абзацы с номером по заголовкам (2.3.1).</summary>
    internal static void NumberedParagraphTests()
    {
        Section("Нумерованные абзацы");

        var counter = new HeadingNumbering(numberHeadings: true, depth: 3);
        var sequence = new List<string?>
        {
            counter.Heading(1, false), counter.Heading(2, false), counter.Paragraph(), counter.Paragraph(),
            counter.Heading(2, false), counter.Heading(1, false), counter.Heading(2, false), counter.Heading(2, false),
            counter.Heading(2, false), counter.Paragraph(), counter.Heading(3, false), counter.Heading(2, true), counter.Paragraph()
        };
        Check(string.Join(" ", sequence.Select(n => n ?? "—")) == "1 1.1 1.1.1 1.1.2 1.2 2 2.1 2.2 2.3 2.3.1 2.3.2 — 2.3.2.1",
            "счётчик: абзац — на уровень ниже заголовка, следующий подзаголовок продолжает счёт (ГОСТ 2.105): " +
            string.Join(" ", sequence.Select(n => n ?? "—")));
        var simple = new HeadingNumbering(numberHeadings: false, depth: 3);
        Check(simple.Paragraph() == "1" && simple.Paragraph() == "2" && simple.Heading(1, false) is null && simple.Paragraph() == "1",
            "без нумерации заголовков: 1, 2 — и заново после заголовка");

        var files = new Dictionary<string, string>
        {
            ["a.dita"] = "<concept id=\"a\"><title>Глава 1</title><conbody><p>Текст</p></conbody></concept>",
            ["b.dita"] = "<concept id=\"b\"><title>Глава 2</title><conbody><p>Текст</p></conbody></concept>",
            ["b1.dita"] = "<concept id=\"b1\"><title>Раздел 2.1</title><conbody><p>x</p></conbody></concept>",
            ["b2.dita"] = "<concept id=\"b2\"><title>Раздел 2.2</title><conbody><p>x</p></conbody></concept>",
            ["b3.dita"] = "<concept id=\"b3\"><title>Раздел 2.3</title><conbody><p outputclass=\"numbered\">Первый пункт</p><p outputclass=\"numbered\">Второй пункт</p></conbody></concept>",
            ["guide.ditamap"] = """
<map><title>Книга</title>
  <topicref href="a.dita"/>
  <topicref href="b.dita"><topicref href="b1.dita"/><topicref href="b2.dita"/><topicref href="b3.dita"/></topicref>
</map>
"""
        };

        WithProject(files, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { NumberHeadings = true, NumberingDepth = 3 });
            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("<span class=\"para-number\">2.3.1</span> Первый пункт") && html.Contains("<span class=\"para-number\">2.3.2</span> Второй пункт"),
                "HTML/PDF: абзацы 2.3.1 и 2.3.2");
            Check(html.Contains("<span class=\"heading-number\">2.3</span> Раздел 2.3"), "HTML/PDF: заголовки тоже с номерами");

            var site = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "site") });
            var page = File.ReadAllText(site.Files.First(f => f.EndsWith("b3.html", StringComparison.OrdinalIgnoreCase)));
            Check(page.Contains("<span class=\"para-number\">2.3.1</span>"), "сайт: номера те же, что в издании");

            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с нумерованными абзацами");
            using var package = WordprocessingDocument.Open(outFile, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var first = body.Descendants<W.Paragraph>().First(p => p.InnerText == "Первый пункт");
            var numPr = first.ParagraphProperties?.NumberingProperties;
            Check(numPr?.NumberingId?.Val?.Value == 9000 && numPr.NumberingLevelReference?.Val?.Value == 2,
                $"DOCX: абзац в списке заголовков на 3-м уровне: numId {numPr?.NumberingId?.Val?.Value}, ilvl {numPr?.NumberingLevelReference?.Val?.Value}");
            var level = package.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Elements<W.AbstractNum>()
                .Single(a => a.AbstractNumberId?.Value == 1002).Elements<W.Level>().Single(l => l.LevelIndex?.Value == 2);
            Check(level.LevelText?.Val?.Value == "%1.%2.%3", "DOCX: формат уровня — 2.3.1");
        });

        WithProject(files, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с нумерованными абзацами без нумерации заголовков");
            using var package = WordprocessingDocument.Open(outFile, false);
            var numPr = package.MainDocumentPart!.Document.Body!.Descendants<W.Paragraph>().First(p => p.InnerText == "Второй пункт").ParagraphProperties?.NumberingProperties;
            Check(numPr?.NumberingId?.Val?.Value is { } id && id != 9000 && numPr.NumberingLevelReference?.Val?.Value == 0,
                "DOCX без нумерации заголовков: свой простой список 1, 2");
        });
    }

    internal static void PagePlacementTests()
    {
        Section("Положение блока на листе");

        var topic = DitaDocument.Parse("""
<concept id="c"><title>Т</title><conbody>
  <p id="p1">Текст</p>
  <section><p id="p2">В разделе</p></section>
  <ul><li><p id="p3">В списке</p></li></ul>
</conbody></concept>
""");
        DitaNode ById(string id) => topic.Root.Descendants().First(n => n.GetAttribute("id") == id);
        Check(PagePlacement.CanPlace(ById("p1")) && PagePlacement.CanPlace(ById("p2")), "абзац в теле и в разделе можно поставить на лист");
        Check(!PagePlacement.CanPlace(ById("p3")), "абзац в списке — нельзя (отдельного листа там нет)");
        Check(PagePlacement.PlaceableFor(ById("p3")) is null && PagePlacement.PlaceableFor(ById("p2")) == ById("p2"),
            "ближайший блок для листа: у абзаца в разделе — он сам, у абзаца в списке — нет");
        ById("p1").SetAttribute("outputclass", "numbered");
        Check(PagePlacement.Set(ById("p1"), "place-bottom-right") && ById("p1").GetAttribute("outputclass") == "numbered place-bottom-right",
            "положение — класс place-… рядом с прочими классами");
        Check(PagePlacement.Of(ById("p1")) == "place-bottom-right" && PagePlacement.Vertical("place-bottom-right") == "bottom" &&
              PagePlacement.Horizontal("place-bottom-right") == "right", "положение читается: низ, право");
        Check(PagePlacement.Set(ById("p1"), null) && ById("p1").GetAttribute("outputclass") == "numbered", "«Обычное» снимает только класс положения");
        Check(!PagePlacement.Set(ById("p1"), "place-nowhere"), "неизвестное положение не ставится");
        ById("p3").SetAttribute("outputclass", "place-top-left");
        Check(PagePlacement.Of(ById("p3")) is null, "класс у абзаца в списке ничего не делает");

        var files = new Dictionary<string, string>
        {
            ["a.dita"] = """
<concept id="a"><title>Глава</title><conbody>
  <p>До грифа</p>
  <p outputclass="place-bottom-right">Для служебного пользования</p>
  <p>После грифа</p>
  <fig outputclass="place-middle-center"><title>Схема</title><p>Рисунок</p></fig>
  <simpletable outputclass="place-top-left"><strow><stentry>Ячейка</stentry></strow></simpletable>
</conbody></concept>
""",
            ["guide.ditamap"] = "<map><title>Книга</title><topicref href=\"a.dita\"/></map>"
        };

        WithProject(files, (root, project) =>
        {
            project.SetDocxLayout(new DocxLayout { NoHeaderOnFirstPage = true, HeaderText = "Шапка" });
            var single = new HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("<div class=\"page-place place-bottom-right\">\n<p class=\"place-bottom-right\">Для служебного пользования</p>") ||
                  System.Text.RegularExpressions.Regex.IsMatch(html, "<div class=\"page-place place-bottom-right\">\\s*<p[^>]*>Для служебного пользования</p>\\s*</div>"),
                "HTML/PDF: абзац в обёртке page-place");
            Check(html.Contains("<div class=\"page-place place-middle-center\">") && html.Contains("<div class=\"page-place place-top-left\">"),
                "HTML/PDF: рисунок и простая таблица тоже в обёртке");
            Check(html.Contains(".page-place { break-before: page;") && html.Contains("height: 100vh"),
                "CSS печати: отдельный лист высотой в область текста");
            Check(html.Contains(".page-place.place-bottom-right { justify-content: flex-end; }") ||
                  html.Contains(".page-place.place-bottom-left, .page-place.place-bottom-center, .page-place.place-bottom-right { justify-content: flex-end; }"),
                "CSS печати: низ листа — flex-end");

            var outFile = Path.Combine(root, "book.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            Check(result.Warnings.Count == 0, "DOCX без предупреждений: " + string.Join("; ", result.Warnings));
            CheckValidDocx(outFile, "DOCX с блоками на отдельных листах");
            using var package = WordprocessingDocument.Open(outFile, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var sections = body.Descendants<W.SectionProperties>().ToList();
            // Разделы: [начало..«До грифа»] [гриф, внизу] [«После грифа»] [рисунок, центр] [таблица, верх — основной раздел]
            Check(sections.Count == 5, $"DOCX: 5 разделов (гриф, текст, рисунок — отдельно; таблица в конце — в основном), получено {sections.Count}");
            string? VAlign(W.SectionProperties s) => s.GetFirstChild<W.VerticalTextAlignmentOnPage>()?.Val?.Value.ToString();
            var stamp = body.Descendants<W.Paragraph>().First(p => p.InnerText == "Для служебного пользования");
            Check(stamp.ParagraphProperties?.SectionProperties is { } own && VAlign(own) == W.VerticalJustificationValues.Bottom.ToString(),
                "DOCX: раздел грифа кончается им самим и выровнен по низу листа");
            Check(stamp.ParagraphProperties?.Justification?.Val?.Value == W.JustificationValues.Right, "DOCX: гриф — по правому краю");
            var before = body.Descendants<W.Paragraph>().First(p => p.InnerText == "До грифа");
            Check(before.ParagraphProperties?.SectionProperties is { } previous && VAlign(previous) is null,
                "DOCX: разрыв раздела — в конце абзаца перед грифом, без лишней строки");
            Check(sections.All(s => s.GetFirstChild<W.PageSize>() is not null && s.GetFirstChild<W.PageMargin>() is not null &&
                                    s.Elements<W.HeaderReference>().Any()),
                "DOCX: у каждого раздела размер листа, поля и колонтитулы основного");
            Check(sections.Count(s => s.GetFirstChild<W.TitlePage>() is not null) == 1 && before.ParagraphProperties!.SectionProperties!.GetFirstChild<W.TitlePage>() is not null,
                "DOCX: «первый лист без колонтитула» — только у первого раздела");
            var figure = body.Descendants<W.Paragraph>().Single(p => p.InnerText == "Рисунок");
            var caption = body.Descendants<W.Paragraph>().Single(p => p.InnerText.EndsWith("Схема", StringComparison.Ordinal));
            Check(caption.ParagraphProperties?.SectionProperties is { } figSection && VAlign(figSection) == W.VerticalJustificationValues.Center.ToString() &&
                  figure.ParagraphProperties?.Justification?.Val?.Value == W.JustificationValues.Center &&
                  caption.ParagraphProperties.Justification?.Val?.Value == W.JustificationValues.Center,
                "DOCX: рисунок с подписью — по центру листа");
            var main = body.Elements<W.SectionProperties>().Single();
            Check(VAlign(main) == W.VerticalJustificationValues.Top.ToString(), "DOCX: таблица в конце — выравнивание основного раздела, без пустого листа");
            var table = body.Descendants<W.Table>().Last();
            Check(table.GetFirstChild<W.TableProperties>()?.TableJustification?.Val?.Value == W.TableRowAlignmentValues.Left, "DOCX: таблица слева");
        });

        // Блок в самом начале и два подряд — без пустых разделов.
        files["a.dita"] = """
<concept id="a"><title>Глава</title><conbody>
  <p outputclass="place-top-center">Первый</p>
  <p outputclass="place-bottom-center">Второй</p>
  <p>Текст</p>
</conbody></concept>
""";
        WithProject(files, (root, project) =>
        {
            var outFile = Path.Combine(root, "book.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "DOCX с блоками на листах подряд");
            using var package = WordprocessingDocument.Open(outFile, false);
            var body = package.MainDocumentPart!.Document.Body!;
            var paragraphs = body.Elements<W.Paragraph>().Select(p => (p.InnerText, Section: p.ParagraphProperties?.SectionProperties is not null)).ToList();
            Check(paragraphs.Count(p => p.Section) == 3, "DOCX: разделы — заголовок, «Первый», «Второй» (без пустых): " +
                string.Join(" | ", paragraphs.Select(p => p.InnerText + (p.Section ? "§" : ""))));
            Check(paragraphs.All(p => !p.Section || p.InnerText.Length > 0), "DOCX: пустых абзацев-меток не осталось: " + string.Join(" | ", paragraphs.Select(p => p.InnerText + (p.Section ? "§" : ""))));
        });
    }
}
