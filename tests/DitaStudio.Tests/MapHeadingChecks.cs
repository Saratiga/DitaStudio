using DocumentFormat.OpenXml.Packaging;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;

namespace DitaStudio.Tests;

// Издание целиком (единый HTML, PDF, DOCX): разделы карты без своего топика (topichead) — заголовки своего уровня, а не «пропавшие»
// строки, после которых топики раздела приклеивались к предыдущей главе; названия строк карты — с подставленными ключами,
// у статьи глоссария — glossterm; правила CSS для картинки не дают рамки абзацу вокруг неё.
internal static partial class CoreChecks
{
    internal static void MapHeadingTests()
    {
        Section("Издание: разделы topichead, заголовки с ключами, глоссарий");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"),
                "<?xml version=\"1.0\"?><concept id=\"intro\"><title>О <keyword keyref=\"product\"/></title><conbody><p>Текст</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "first.dita"),
                "<?xml version=\"1.0\"?><topic id=\"first\"><title>Первый урок</title><body><p>Один</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "second.dita"),
                "<?xml version=\"1.0\"?><topic id=\"second\"><title>Второй урок</title><body><p>Два</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "term.dita"),
                "<?xml version=\"1.0\"?><glossentry id=\"term\"><glossterm>топик</glossterm><glossdef>Часть текста.</glossdef></glossentry>");
            File.WriteAllText(Path.Combine(root, "pic.png"), string.Empty);
            File.WriteAllText(Path.Combine(root, "styled.dita"),
                "<?xml version=\"1.0\"?><topic id=\"styled\"><title>Рисунок</title><body><fig><title>Схема</title><image href=\"pic.png\"><alt>Схема</alt></image></fig></body></topic>");
            var mapPath = Path.Combine(root, "guide.ditamap");
            File.WriteAllText(mapPath, """
                <?xml version="1.0"?>
                <map>
                  <title>Руководство</title>
                  <keydef keys="product"><topicmeta><keywords><keyword>Метео</keyword></keywords></topicmeta></keydef>
                  <topicref href="intro.dita"/>
                  <topichead navtitle="Уроки">
                    <topicref href="first.dita"/>
                    <topicref href="second.dita"/>
                  </topichead>
                  <topichead navtitle="Пустой раздел"/>
                  <topicgroup>
                    <topicref href="term.dita"/>
                  </topicgroup>
                </map>
                """);

            var project = new DitaProject(root);
            project.Scan();
            project.DocxLayout.NumberHeadings = true;

            // --- дерево карты
            var tree = MapTree.Build(project, mapPath);
            var order = tree.PublicationOrderWithHeadings.Select(i => i.Title).ToList();
            Check(order.SequenceEqual(new[] { "О Метео", "Уроки", "Первый урок", "Второй урок", "топик" }),
                "порядок издания: топик, раздел «Уроки», его топики, глоссарий (пустой раздел и topicgroup — нет): " + string.Join(" | ", order));
            Check(tree.Items.Single(i => i.Title == "Уроки").IsHeadingOnly, "topichead с топиками — строка-заголовок");
            Check(!tree.Items.Any(i => i.ElementName == "topicgroup" && i.IsHeadingOnly), "topicgroup без названия заголовком не считается");
            Check(tree.PublicationOrder.Count() == 4, "PublicationOrder по-прежнему только топики");

            // --- единый HTML: заголовок раздела с номером, топики раздела — 2.1, 2.2; в оглавлении — то же
            var single = new HtmlPublisher(project).Publish(mapPath,
                new PublishOptions { OutputDirectory = Path.Combine(root, "single"), SingleFile = true, Language = "ru" });
            var html = File.ReadAllText(single.EntryFile);
            Check(html.Contains("<article class=\"topichead\">") && html.Contains(" Уроки</h1>"), "в едином HTML есть заголовок раздела «Уроки» уровня 1");
            Check(html.Contains("<span class=\"heading-number\">2</span> Уроки"), "раздел «Уроки» — глава 2");
            Check(html.Contains("<span class=\"heading-number\">2.1</span> Первый урок") && html.Contains("<span class=\"heading-number\">2.2</span> Второй урок"),
                "топики раздела — 2.1 и 2.2, а не продолжение главы 1");
            var tocStart = html.IndexOf("class=\"toc toc-inline\"", StringComparison.Ordinal);
            var toc = html[tocStart..html.IndexOf("</nav>", tocStart, StringComparison.Ordinal)];
            Check(toc.Contains("href=\"#head-1\">Уроки</a>"), "строка оглавления ведёт на заголовок раздела");
            Check(toc.Contains(">О Метео</a>"), "название с ключом в оглавлении — с текстом ключа: " + toc);
            Check(toc.Contains(">топик</a>") && !toc.Contains("term.dita"), "статья глоссария в оглавлении — по glossterm, а не по имени файла");
            Check(!html.Contains("Пустой раздел"), "раздел без топиков в издание не попадает");

            // --- DOCX: абзац-заголовок раздела с закладкой и строка оглавления на неё
            var docxPath = Path.Combine(root, "guide.docx");
            var docx = new DocxPublisher(project).Publish(mapPath, new PublishOptions { OutputDirectory = root, Language = "ru" }, docxPath);
            Check(docx.Warnings.Count == 0, "DOCX без предупреждений: " + string.Join("; ", docx.Warnings));
            CheckValidDocx(docxPath, "издание с topichead");
            using (var word = WordprocessingDocument.Open(docxPath, false))
            {
                var body = word.MainDocumentPart!.Document.Body!;
                var headings = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading", StringComparison.Ordinal) == true)
                    .Select(p => (Style: p.ParagraphProperties!.ParagraphStyleId!.Val!.Value, Text: p.InnerText))
                    .ToList();
                Check(headings.Any(h => h.Style == "Heading1" && h.Text == "Уроки"), "в DOCX раздел «Уроки» — заголовок 1");
                Check(headings.Any(h => h.Style == "Heading2" && h.Text == "Первый урок"), "его топик — заголовок 2");
                var bookmark = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.BookmarkStart>().FirstOrDefault(b => b.Name?.Value?.StartsWith("_head_") == true);
                Check(bookmark is not null, "у заголовка раздела есть закладка");
                Check(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>().Any(h => h.Anchor?.Value == bookmark?.Name?.Value && h.InnerText == "Уроки"),
                    "строка оглавления DOCX ведёт на заголовок раздела");
                Check(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>().Any(h => h.InnerText == "О Метео"), "строка оглавления с ключом — «О Метео»");
            }

            // --- рамка картинки из CSS — у рисунка, а не у абзаца
            var picMap = Path.Combine(root, "pic.ditamap");
            File.WriteAllText(picMap, "<?xml version=\"1.0\"?><map><title>Рисунок</title><topicref href=\"styled.dita\"/></map>");
            File.WriteAllBytes(Path.Combine(root, "pic.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            var cssPath = Path.Combine(root, "custom.css");
            File.WriteAllText(cssPath, ".fig image { border: 1pt solid #c8ccd2; }");
            project.SetCustomCssPath("custom.css");
            var picDocx = Path.Combine(root, "pic.docx");
            new DocxPublisher(project).Publish(picMap, new PublishOptions { OutputDirectory = root, Language = "ru" }, picDocx);
            CheckValidDocx(picDocx, "рамка картинки");
            using (var word = WordprocessingDocument.Open(picDocx, false))
            {
                var xml = word.MainDocumentPart!.Document.OuterXml;
                var styles = word.MainDocumentPart.StyleDefinitionsPart!.Styles!.OuterXml;
                Check(xml.Contains("<a:ln"), "у рисунка есть контур из CSS");
                Check(!styles.Contains("fig image"), "производного стиля абзаца для картинки нет (рамка не легла на абзац)");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }
}
