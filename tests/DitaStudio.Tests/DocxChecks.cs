using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Экспорт DOCX и размеры изображений.
internal static partial class CoreChecks
{
    internal static void DocxTests()
    {
        Section("Экспорт в DOCX");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="intro">
  <title>Введение<fn>Сноска про введение.</fn></title>
  <conbody>
    <p>Смотрите <xref href="install.dita#install">установку</xref>. Ключ: <keyword keyref="product-name"/>.</p>
    <table>
      <tgroup cols="2">
        <colspec colname="c1" colnum="1" colwidth="1*"/>
        <colspec colname="c2" colnum="2" colwidth="1*"/>
        <thead><row><entry colname="c1">A</entry><entry colname="c2">B</entry></row></thead>
        <tbody>
          <row><entry colname="c1" morerows="1">R1</entry><entry colname="c2">X</entry></row>
          <row><entry colname="c2">Y</entry></row>
        </tbody>
      </tgroup>
    </table>
    <note type="tip">Совет.</note>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "install.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="install">
  <title>Установка</title>
  <taskbody>
    <steps>
      <step><cmd>Первый шаг</cmd></step>
      <step><cmd>Второй шаг</cmd></step>
      <step><cmd>Третий шаг</cmd><info>Пояснение без обёртки p.</info><stepresult>Результат без обёртки p.</stepresult></step>
    </steps>
  </taskbody>
</task>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест DOCX</title>
  <keydef keys="product-name"><topicmeta><keywords><keyword>Пример</keyword></keywords></topicmeta></keydef>
  <topicref href="intro.dita"/>
  <topicref href="install.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var outFile = Path.Combine(root, "out.docx");
            var publisher = new DocxPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "Экспорт в DOCX");

            Check(result.Warnings.Count == 0, "экспорт в DOCX прошёл без предупреждений: " + string.Join("; ", result.Warnings));
            Check(File.Exists(outFile), "файл .docx создан");

            using var doc = WordprocessingDocument.Open(outFile, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Пример"), "ключ product-name подставлен в DOCX");
            Check(text.Contains("Установка"), "заголовок второго топика попал в DOCX");
            Check(text.Contains("Пояснение без обёртки p."), "info с голым текстом (без <p>) не потерян в DOCX");
            Check(text.Contains("Результат без обёртки p."), "stepresult с голым текстом (без <p>) не потерян в DOCX");

            var footnotesPart = doc.MainDocumentPart.FootnotesPart;
            var realFootnotes = footnotesPart?.Footnotes?.Elements<Footnote>().Count(f => (f.Id?.Value ?? 0) > 0) ?? 0;
            Check(realFootnotes == 1, $"настоящая сноска Word создана: {realFootnotes}");
            Check(body.Descendants<FootnoteReference>().Count() == 1, "ссылка на сноску вставлена в текст");

            var hyperlinks = body.Descendants<Hyperlink>().ToList();
            Check(hyperlinks.Count == 1 && hyperlinks[0].Anchor is not null,
                "перекрёстная ссылка стала внутренней гиперссылкой на закладку");

            var table = body.Descendants<Table>().First();
            var rows = table.Elements<TableRow>().ToList();
            Check(rows.Count == 3, "в таблице три строки (шапка + 2 строки тела)");

            var bodyRow1Cells = rows[1].Elements<TableCell>().ToList();
            var bodyRow2Cells = rows[2].Elements<TableCell>().ToList();
            Check(bodyRow1Cells[0].TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Restart,
                "объединение ячеек по вертикали начато (Restart)");
            Check(bodyRow2Cells.Count == 2 &&
                  bodyRow2Cells[0].TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Continue,
                "вторая строка таблицы получила ячейку-продолжение объединения");

            var tocField = body.Descendants<SimpleField>().FirstOrDefault(f => f.Instruction?.Value?.Contains("TOC") == true);
            Check(tocField is not null, "поле оглавления (TOC) добавлено");

            var headings = body.Elements<Paragraph>()
                .Count(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading") == true);
            Check(headings == 2, $"оба топика получили заголовки-абзацы: {headings}");
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

    internal static void DocxRendererMiscTests()
    {
        Section("DocxRenderer: топик-уровневые ветки, CALS-таблица, сноски, закладки (по отчёту покрытия)");

        Check(DocxRenderer.SafeBookmarkName("123abc") == "_123abc", "SafeBookmarkName: имя, начинающееся с цифры, получает ведущее '_'");
        Check(DocxRenderer.SafeBookmarkName("!!!") == "___", "SafeBookmarkName: символы вне буквы/цифры заменяются на '_' (не даёт пустую строку)");
        Check(DocxRenderer.SafeBookmarkName(new string('a', 60)).Length == 40, "SafeBookmarkName обрезает длинное имя до 40 символов");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "main.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="main">
  <title outputclass="page-break-before">Заголовок с разрывом страницы</title>
  <shortdesc audience="internal">Это описание должно быть исключено.</shortdesc>
  <prolog><author>Автор</author></prolog>
  <titlealts><navtitle>Альт</navtitle></titlealts>
  <abstract><p>Реферат концепции.</p></abstract>
  <conbody>
    <concept id="nested"><title>Вложенный топик</title><conbody><p>Внутри.</p></conbody></concept>
    <codeblock>Строка 1
Строка 2
Строка 3</codeblock>
    <lq>Цитата с отступом и курсивом.</lq>
    <p>Сноска раз<fn>Первая.</fn> и сноска два<fn>Вторая.</fn>.</p>
    <table>
      <tgroup>
        <tbody>
          <row><entry>A</entry><entry>B</entry><entry>C</entry></row>
          <row><entry>D</entry><entry>E</entry><entry>F</entry></row>
        </tbody>
      </tgroup>
    </table>
    <table>
      <tgroup cols="3">
        <colspec colname="c1" colnum="1"/><colspec colname="c2" colnum="2"/><colspec colname="c3" colnum="3"/>
        <tbody>
          <row>
            <entry namest="c1" nameend="c2" morerows="1" align="center" valign="top">Span+rowspan</entry>
            <entry align="right">R</entry>
          </row>
          <row><entry align="justify" valign="bottom">X</entry></row>
        </tbody>
      </tgroup>
    </table>
    <properties>
      <property><proptype>color</proptype><propvalue>red</propvalue><propdesc>Красный</propdesc></property>
    </properties>
  </conbody>
  <related-links>
    <link href="second.dita#second"/>
  </related-links>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "second.dita"), "<concept id=\"second\"><title>Второй</title><conbody><p>x</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map><title>T</title><topicref href="main.dita"/><topicref href="second.dita"/></map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var options = new PublishOptions { Language = "ru" };
            options.ExcludeConditions["audience"] = new HashSet<string> { "internal" };

            var outFile = Path.Combine(root, "out.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), options, outFile);
            CheckValidDocx(outFile, "DocxRenderer: топик-уровневые ветки, CALS-таблица, сноски, закладки (по отчёту покрытия)");
            Check(result.Warnings.Count == 0, "публикация без предупреждений: " + string.Join("; ", result.Warnings));

            using var doc = WordprocessingDocument.Open(outFile, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(!text.Contains("Это описание должно быть исключено"), "shortdesc с audience=internal исключён фильтром ditaval на уровне RenderTopic");
            Check(!text.Contains("Автор") && !text.Contains("Альт"), "prolog и titlealts — молчаливо пропускаемые ветки, их содержимое не попадает в вывод");
            Check(text.Contains("Реферат концепции."), "abstract топика (не только conbody) отрисован через RenderTopic");
            Check(text.Contains("Вложенный топик") && text.Contains("Внутри."), "вложенный топик внутри conbody отрисован рекурсивным RenderTopic");
            Check(text.Contains("Второй"), "related-links как ПРЯМОЙ ребёнок топика (не внутри conbody) отрисован");

            var titleHeading = body.Elements<Paragraph>().First(p => p.InnerText.Contains("Заголовок с разрывом"));
            Check(titleHeading.ParagraphProperties?.GetFirstChild<PageBreakBefore>() is not null,
                "outputclass=\"page-break-before\" на title даёт PageBreakBefore в DOCX");

            Check(text.Contains("Строка 1") && text.Contains("Строка 2") && text.Contains("Строка 3"),
                "многострочный codeblock — все строки попали в вывод");
            Check(body.Descendants<Break>().Any(), "перенос строки внутри codeblock стал <w:br/> (не потерян)");

            var footnotesPart = doc.MainDocumentPart.FootnotesPart;
            var realFootnotes = footnotesPart?.Footnotes?.Elements<Footnote>().Count(f => (f.Id?.Value ?? 0) > 0) ?? 0;
            Check(realFootnotes == 2, $"обе сноски в одном документе создали отдельные настоящие сноски Word (не только первая): {realFootnotes}");
            var separatorFootnotes = footnotesPart?.Footnotes?.Elements<Footnote>().Count(f => (f.Id?.Value ?? 0) <= 0) ?? 0;
            Check(separatorFootnotes == 2, "служебные Separator/ContinuationSeparator созданы один раз, а не по разу на сноску (EnsureFootnotesRoot идемпотентен)");

            var tables = body.Descendants<Table>().ToList();
            Check(tables.Count == 3, $"обе CALS-таблицы и simpletable (properties) отрисованы как <w:tbl>: {tables.Count}");

            var noColspecTable = tables[0];
            Check(noColspecTable.Elements<TableGrid>().First().Elements<GridColumn>().Count() == 3,
                "таблица без единого colspec определяет число колонок по факту строк (3 entry в строке)");

            var spanTable = tables[1];
            var spanRow = spanTable.Elements<TableRow>().First();
            var spanCells = spanRow.Elements<TableCell>().ToList();
            Check(spanCells[0].TableCellProperties?.GetFirstChild<GridSpan>()?.Val?.Value == 2,
                "namest/nameend на entry дали colspan=2 (реальный CALS span, не через colname)");
            Check(spanCells[0].TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Restart,
                "тот же entry с morerows=1 начал вертикальное объединение");
            Check(spanCells[0].TableCellProperties?.GetFirstChild<TableCellVerticalAlignment>()?.Val?.Value == TableVerticalAlignmentValues.Top,
                "valign=\"top\" дал TableCellVerticalAlignment.Top");
            var centerJustification = spanCells[0].Elements<Paragraph>().First().ParagraphProperties?.Justification?.Val?.Value;
            Check(centerJustification == JustificationValues.Center, "align=\"center\" дал Justification.Center");
            Check(spanCells[1].Elements<Paragraph>().First().ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Right,
                "align=\"right\" дал Justification.Right");

            var secondSpanRow = spanTable.Elements<TableRow>().ElementAt(1);
            var continuationCell = secondSpanRow.Elements<TableCell>().First();
            Check(continuationCell.TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Continue &&
                  continuationCell.TableCellProperties?.GetFirstChild<GridSpan>()?.Val?.Value == 2,
                "ячейка-продолжение вертикального объединения тоже несёт GridSpan=2 (продолжает и colspan, и rowspan одновременно)");
            Check(secondSpanRow.Elements<TableCell>().ElementAt(1).Elements<Paragraph>().First().ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Both,
                "align=\"justify\" дал Justification.Both");
            Check(secondSpanRow.Elements<TableCell>().ElementAt(1).TableCellProperties?.GetFirstChild<TableCellVerticalAlignment>()?.Val?.Value == TableVerticalAlignmentValues.Bottom,
                "valign=\"bottom\" дал TableCellVerticalAlignment.Bottom");

            Check(text.Contains("Красный"), "properties без явного <prophead> получили синтетический заголовок (headLabels) и не потеряли содержимое");
            var simpleTableHeader = body.Descendants<Table>().SelectMany(t => t.Elements<TableRow>()).FirstOrDefault(r => r.InnerText.Contains(L_TypeLabelRu));
            Check(simpleTableHeader is not null, $"синтетический заголовок properties использует локализованную метку '{L_TypeLabelRu}'");
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

    private const string L_TypeLabelRu = "Тип";

    /// <summary>Минимальный (нерабочий как изображение, но валидный по заголовку) PNG —
    /// сигнатура + честный IHDR с шириной/высотой; ImageSize читает только эти байты,
    /// пиксельные данные и CRC ей не нужны.</summary>
    private static byte[] BuildMinimalPng(int width, int height)
    {
        var bytes = new List<byte>(33);
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void AppendBigEndian(int value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        AppendBigEndian(13);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
        AppendBigEndian(width);
        AppendBigEndian(height);
        bytes.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        AppendBigEndian(0);
        return bytes.ToArray();
    }

    private static byte[] BuildMinimalGif(int width, int height)
    {
        var bytes = new List<byte> { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' };
        bytes.Add((byte)(width & 0xFF));
        bytes.Add((byte)((width >> 8) & 0xFF));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)((height >> 8) & 0xFF));
        while (bytes.Count < 24)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>signedHeight отрицательный проверяет ветку Math.Abs в ReadPixelSize.</summary>
    private static byte[] BuildMinimalBmp(int width, int signedHeight)
    {
        var bytes = new List<byte>(30) { (byte)'B', (byte)'M' };
        while (bytes.Count < 18)
        {
            bytes.Add(0);
        }

        bytes.AddRange(BitConverter.GetBytes(width));
        bytes.AddRange(BitConverter.GetBytes(signedHeight));
        while (bytes.Count < 30)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>SOI + один незначащий APP0-сегмент (проверяет пропуск сегмента по длине) + SOF0 с
    /// шириной/высотой — ReadJpegSize возвращает результат сразу после SOF0, дальше можно не писать.</summary>
    private static byte[] BuildMinimalJpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange(new byte[14]); // тело APP0 — 16 (длина) - 2 = 14 байт, содержимое неважно
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11 });
        bytes.Add(8); // precision
        bytes.Add((byte)((height >> 8) & 0xFF));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)((width >> 8) & 0xFF));
        bytes.Add((byte)(width & 0xFF));
        return bytes.ToArray();
    }

    internal static void ImageSizeFormatsTests()
    {
        Section("ImageSize через DocxRenderer.RenderImage: GIF/BMP/JPEG, единицы измерения (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "pic.gif"), BuildMinimalGif(160, 90));
            File.WriteAllBytes(Path.Combine(root, "pic.bmp"), BuildMinimalBmp(320, -240));
            File.WriteAllBytes(Path.Combine(root, "pic.jpg"), BuildMinimalJpeg(400, 300));
            File.WriteAllBytes(Path.Combine(root, "unit.png"), BuildMinimalPng(1000, 500));

            File.WriteAllText(Path.Combine(root, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="t">
  <title>T</title>
  <conbody>
    <p><image href="pic.gif"/></p>
    <p><image href="pic.bmp"/></p>
    <p><image href="pic.jpg"/></p>
    <p><image href="unit.png" width="2cm"/></p>
    <p><image href="unit.png" width="10mm"/></p>
    <p><image href="unit.png" width="36pt"/></p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map><title>T</title><topicref href="topic.dita"/></map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var outFile = Path.Combine(root, "out.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            CheckValidDocx(outFile, "ImageSize через DocxRenderer.RenderImage: GIF/BMP/JPEG, единицы измерения (по отчёту покрытия)");
            Check(result.Warnings.Count == 0, "публикация без предупреждений: " + string.Join("; ", result.Warnings));

            using var doc = WordprocessingDocument.Open(outFile, false);
            var drawings = doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToList();
            Check(drawings.Count == 6, $"все 6 изображений встроены: {drawings.Count}");

            const long emuPerInch = 914400;
            const long emuPerPixel = emuPerInch / 96;

            Check(drawings[0].Extent!.Cx!.Value == 160L * emuPerPixel && drawings[0].Extent!.Cy!.Value == 90L * emuPerPixel,
                $"GIF: размер прочитан из заголовка (160x90): {drawings[0].Extent!.Cx},{drawings[0].Extent!.Cy}");
            Check(drawings[1].Extent!.Cx!.Value == 320L * emuPerPixel && drawings[1].Extent!.Cy!.Value == 240L * emuPerPixel,
                $"BMP: размер прочитан из заголовка, отрицательная высота стала положительной через Math.Abs (320x240): {drawings[1].Extent!.Cx},{drawings[1].Extent!.Cy}");
            Check(drawings[2].Extent!.Cx!.Value == 400L * emuPerPixel && drawings[2].Extent!.Cy!.Value == 300L * emuPerPixel,
                $"JPEG: размер прочитан из маркера SOF0 после пропуска APP0 (400x300): {drawings[2].Extent!.Cx},{drawings[2].Extent!.Cy}");

            Check(drawings[3].Extent!.Cx!.Value == (long)(2 * emuPerInch / 2.54), $"width=\"2cm\" переведён в EMU через дюймы/2.54: {drawings[3].Extent!.Cx}");
            Check(drawings[4].Extent!.Cx!.Value == (long)(10 * emuPerInch / 25.4), $"width=\"10mm\" переведён в EMU через дюймы/25.4: {drawings[4].Extent!.Cx}");
            Check(drawings[5].Extent!.Cx!.Value == (long)(36 * emuPerInch / 72), $"width=\"36pt\" переведён в EMU через дюймы/72 (0.5in = 457200 EMU): {drawings[5].Extent!.Cx}");
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
