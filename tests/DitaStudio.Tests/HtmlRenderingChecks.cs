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

// Рендер HTML: сложные конструкции, списки и шаги.
internal static partial class CoreChecks
{
    /// <summary>
    /// Элементы публикации, которых нет в основных DocxTests/HtmlPublisher-проверках:
    /// figure/dl/parml/simpletable/properties/choicetable, image (естественный размер, явные
    /// width/height, слишком широкое изображение — обрезка по MaxWidthEmu, отсутствующий файл,
    /// неподдерживаемый формат, внешняя ссылка), xref (внешний, неразрешённый, keyref без href).
    /// Один и тот же проект публикуется и в HTML, и в DOCX — расхождения в поведении между
    /// рендерерами (например, внешние изображения) фиксируются как есть, не как баг.
    /// </summary>
    internal static void AdvancedRenderingTests()
    {
        Section("HTML/DOCX: figure/dl/parml/simpletable/image/xref (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "real.png"), BuildMinimalPng(200, 100));
            File.WriteAllBytes(Path.Combine(root, "wide.png"), BuildMinimalPng(2000, 1000));
            File.WriteAllBytes(Path.Combine(root, "pic.webp"), new byte[] { 1, 2, 3, 4 });

            File.WriteAllText(Path.Combine(root, "second.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="second">
  <title>Второй топик</title>
  <conbody><p>Текст.</p></conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "advanced.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="advanced">
  <title>Продвинутые элементы</title>
  <conbody>
    <fig><title>Схема</title>
      <p>Внутри рисунка.</p>
      <desc>Описание рисунка.</desc>
    </fig>
    <dl>
      <dlhead><dthd>Термин</dthd><ddhd>Значение</ddhd></dlhead>
      <dlentry><dt>Ключ</dt><dd>Значение ключа</dd></dlentry>
    </dl>
    <parml>
      <plentry><pt>-x</pt><pd>Включить X</pd></plentry>
    </parml>
    <simpletable>
      <sthead><stentry>H1</stentry><stentry>H2</stentry></sthead>
      <strow><stentry>A1</stentry><stentry>B1</stentry></strow>
    </simpletable>
    <properties>
      <prophead><proptypehd>Тип</proptypehd><propvaluehd>Значение</propvaluehd><propdeschd>Описание</propdeschd></prophead>
      <property><proptype>color</proptype><propvalue>red</propvalue><propdesc>Красный</propdesc></property>
    </properties>
    <choicetable>
      <chhead><choptionhd>Опция</choptionhd><chdeschd>Описание</chdeschd></chhead>
      <chrow><choption>A</choption><chdesc>Вариант A</chdesc></chrow>
    </choicetable>
    <p>Без размеров: <image href="real.png"/></p>
    <p>Явные размеры: <image href="real.png" width="2in" height="1in"/></p>
    <p>Слишком широкое: <image href="wide.png"/></p>
    <p>Через keyref: <image keyref="img-key"/></p>
    <p>Нет файла: <image href="missing.png"/></p>
    <p>Неподдерживаемый формат: <image href="pic.webp"/></p>
    <p>Внешнее: <image href="https://example.com/pic.png"/></p>
    <p>Внешняя ссылка: <xref href="https://example.com" scope="external">Сайт</xref></p>
    <p>Неразрешённая: <xref href="nowhere.dita#topic"/></p>
    <p>По ключу без href: <xref keyref="label-key"/></p>
    <related-links>
      <link href="second.dita#second"/>
      <link href="https://example.com" scope="external"><linktext>Внешний сайт</linktext></link>
    </related-links>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест расширенного рендера</title>
  <keydef keys="img-key" href="real.png"/>
  <keydef keys="label-key"><topicmeta><keywords><keyword>Голый текст</keyword></keywords></topicmeta></keydef>
  <topicref href="advanced.dita"/>
  <topicref href="second.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var mapPath = Path.Combine(root, "guide.ditamap");

            // --------------------------------------------------------------- HTML
            var htmlResult = new HtmlPublisher(project).Publish(mapPath, new PublishOptions { OutputDirectory = Path.Combine(root, "html-out"), SingleFile = true });
            var html = File.ReadAllText(htmlResult.EntryFile);

            Check(html.Contains("<figcaption class=\"fig-title\">Рисунок 1. Схема</figcaption>"), "html: подпись рисунка пронумерована");
            Check(html.Contains("<div class=\"desc\">Описание рисунка.</div>"), "html: desc рисунка отрисован");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<dt>Ключ</dt>\\s*<dd>Значение ключа</dd>"), "html: dlentry (dt/dd) отрисован");
            Check(html.Contains("class=\"dlhead\""), "html: dlhead отрисован");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<dt>-x</dt>\\s*<dd>Включить X</dd>"), "html: parml (pt/pd как dt/dd) отрисован");
            Check(html.Contains("H1") && html.Contains("A1"), "html: generic simpletable отрисован");
            Check(html.Contains("color") && html.Contains("red") && html.Contains("Красный"), "html: properties отрисован");
            Check(html.Contains("Вариант A"), "html: choicetable отрисован");

            // CopyImages по умолчанию включён — HtmlPublisher копирует файлы в media/ и переписывает
            // src, кешируя по абсолютному исходному пути (real.png использован трижды и должен
            // трижды сослаться на один и тот же скопированный файл).
            Check(html.Contains("<img src=\"media/real.png\" alt=\"\" />"), "html: изображение без width/height скопировано в media/, атрибуты размера не добавлены");
            Check(html.Contains("width=\"2in\"") && html.Contains("height=\"1in\""), "html: явные width/height перенесены в <img> как есть (без пересчёта в EMU — это забота DOCX)");
            Check(html.Contains("src=\"media/wide.png\""), "html: слишком широкое изображение всё равно отрисовано (у HTML нет понятия печатной полосы)");
            Check(html.Split("src=\"media/real.png\"").Length - 1 == 3,
                $"html: keyref и прямой href на одно и то же изображение резолвятся в один и тот же скопированный файл (3 вхождения): {html.Split("src=\"media/real.png\"").Length - 1}");
            Check(html.Contains("src=\"missing.png\""), "html: несуществующий файл — копирование не удалось, но <img> всё равно отрисован с исходным именем (не 'media/', в отличие от успешно скопированных)");
            Check(html.Contains("src=\"media/pic.webp\""), "html: неподдерживаемый для DOCX формат в HTML не особый случай — обычный скопированный <img>");
            Check(html.Contains("src=\"https://example.com/pic.png\""), "html: внешнее изображение отрисовано как обычный <img> (в отличие от DOCX, который его пропускает)");

            Check(html.Contains("<a href=\"https://example.com\">Сайт</a>"), "html: внешняя xref стала обычной ссылкой");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a href=\"#topic\">#topic</a>"),
                "html (single-file): неразрешённая xref внутри публикации деградирует до якоря по topicId, а не до буквального href (это забота DOCX — там именно буквальный href)");
            Check(html.Contains("Голый текст") && !html.Contains("label-key"), "html: xref по keyref без href показывает KeyText");
            Check(html.Contains("related-links") && html.Contains("Второй топик") && html.Contains("Внешний сайт"),
                "html: related-links собрал и внутреннюю, и внешнюю ссылку");

            // --------------------------------------------------------------- DOCX
            var docxPath = Path.Combine(root, "out.docx");
            var docxResult = new DocxPublisher(project).Publish(mapPath, new PublishOptions { Language = "ru" }, docxPath);
            CheckValidDocx(docxPath, "HTML/DOCX: figure/dl/parml/simpletable/image/xref (по отчёту покрытия)");

            using var doc = WordprocessingDocument.Open(docxPath, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Рисунок 1. Схема"), "docx: подпись рисунка пронумерована");
            Check(text.Contains("Описание рисунка."), "docx: desc рисунка отрисован");
            Check(text.Contains("Ключ") && text.Contains("Значение ключа"), "docx: dlentry (dt/dd) отрисован");
            Check(text.Contains("-x") && text.Contains("Включить X"), "docx: parml (pt/pd) отрисован");
            Check(text.Contains("H1") && text.Contains("A1"), "docx: generic simpletable отрисован");
            Check(text.Contains("Красный"), "docx: properties отрисован (заданные заголовки колонок использованы)");
            Check(text.Contains("Вариант A"), "docx: choicetable отрисован");

            var drawings = body.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToList();
            Check(drawings.Count == 4, $"docx: во внедрение попали 4 изображения (натуральный размер, явные размеры, слишком широкое, keyref): {drawings.Count}");

            if (drawings.Count >= 4)
            {
                var natural = drawings[0].Extent!;
                Check(natural.Cx!.Value == 200L * 9525 && natural.Cy!.Value == 100L * 9525,
                    $"docx: естественный размер картинки взят из PNG-заголовка (200x100 px @96dpi): {natural.Cx},{natural.Cy}");

                var explicitSize = drawings[1].Extent!;
                Check(explicitSize.Cx!.Value == 2L * 914400 && explicitSize.Cy!.Value == 914400,
                    $"docx: явные width/height (2in x 1in) переопределяют естественный размер: {explicitSize.Cx},{explicitSize.Cy}");

                var wide = drawings[2].Extent!;
                const long maxWidthEmu = 6L * 914400;
                Check(wide.Cx!.Value == maxWidthEmu && wide.Cx!.Value < 2000L * 9525,
                    $"docx: слишком широкое изображение (2000px) обрезано по MaxWidthEmu (6 дюймов): {wide.Cx}");
            }

            Check(docxResult.Warnings.Any(w => w.Contains("Изображение не найдено") && w.Contains("missing.png")),
                "docx: отсутствующий файл изображения дал предупреждение");
            Check(docxResult.Warnings.Any(w => w.Contains("не поддерживается") && w.Contains("pic.webp")),
                "docx: неподдерживаемый формат дал предупреждение");
            Check(text.Contains("pic.webp"), "docx: неподдерживаемый формат показан как текстовая ссылка на имя файла");
            Check(!text.Contains("example.com/pic.png"), "docx: внешнее изображение молча пропущено (не встроено, не текстом)");

            var hyperlinks = body.Descendants<Hyperlink>().ToList();
            Check(hyperlinks.Any(h => h.Id?.Value is not null), "docx: внешняя xref стала гиперссылкой по Relationship Id");
            Check(text.Contains("nowhere.dita#topic"), "docx: неразрешённая xref деградирует до буквального href как простого текста (без гиперссылки)");
            Check(text.Contains("Голый текст"), "docx: xref по keyref без href показывает KeyText как обычный текст");
            Check(text.Contains("Второй топик") && text.Contains("Внешний сайт"), "docx: related-links собрал и внутреннюю, и внешнюю ссылку");
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

    /// <summary>
    /// Остальные конструкции HtmlRenderer, которых нет ни в одном другом тесте: CALS-таблица,
    /// object/video/audio, foreign/svg-container, coderef, сноски, предметный указатель
    /// (indexterm/RenderIndexSection), abbreviated-form (все три исхода), RenderInline-варианты
    /// (overline/q/cite/menucascade/state/boolean/tm), spectitle-заголовок раздела, hazardstatement,
    /// вложенный топик (RenderNestedTopic) и глоссарная статья как отдельный тип топика.
    /// </summary>
    internal static void HtmlRendererMiscTests()
    {
        Section("HtmlRenderer: таблица/медиа/foreign/сноски/указатель/abbreviated-form (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "code.txt"), "int main() { return 0; }");
            File.WriteAllBytes(Path.Combine(root, "video.mp4"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(root, "audio.mp3"), new byte[] { 4, 5, 6 });
            File.WriteAllBytes(Path.Combine(root, "flashthing.bin"), new byte[] { 7, 8 });

            File.WriteAllText(Path.Combine(root, "glossary.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<glossentry id="acr-glossentry">
  <glossterm>Central Processing Unit</glossterm>
  <glossAcronym>CPU</glossAcronym>
  <glossdef>Определение.</glossdef>
</glossentry>
""");

            File.WriteAllText(Path.Combine(root, "plain.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="plain">
  <title>Обычный топик, не глоссарий</title>
  <conbody><p>Текст.</p></conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "main.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="main">
  <title>Продвинутые конструкции</title>
  <conbody>
    <concept id="nested"><title>Вложенный топик</title><conbody><p>Внутри вложенного.</p></conbody></concept>
    <section spectitle="Пользовательский раздел"><p>Текст раздела без явного title.</p></section>
    <hazardstatement type="warning">
      <messagepanel><typeoftext>ОПАСНО</typeoftext><howtoavoid>Не делайте так.</howtoavoid></messagepanel>
    </hazardstatement>
    <table>
      <title>Заголовок таблицы</title>
      <tgroup cols="3">
        <colspec colname="c1" colnum="1" colwidth="2*"/>
        <colspec colname="c2" colnum="2" colwidth="1*"/>
        <colspec colname="c3" colnum="3" colwidth="1*"/>
        <thead><row><entry>H1</entry><entry>H2</entry><entry>H3</entry></row></thead>
        <tbody>
          <row><entry namest="c1" nameend="c2" align="center">Объединённая</entry><entry morerows="1" valign="top">R</entry></row>
          <row><entry>X</entry><entry>Y</entry></row>
        </tbody>
      </tgroup>
    </table>
    <object data="flashthing.bin" type="application/octet-stream"/>
    <object>Без data — дети рендерятся как есть</object>
    <video href="video.mp4"/>
    <audio href="audio.mp3" controls="false"/>
    <video><media-source href="video.mp4"/></video>
    <svg-container><svg width="10" height="10"><circle r="5"/></svg></svg-container>
    <codeblock><coderef href="code.txt"/></codeblock>
    <codeblock><coderef href="missing.txt"/></codeblock>
    <p>Сноска с меткой<fn callout="*">Особая сноска.</fn> и обычная<fn>Вторая сноска.</fn>.</p>
    <p>Термин <indexterm>CPU<indexterm>детали</indexterm></indexterm> встречается и здесь: <indexterm>CPU</indexterm>.</p>
    <p>По ключу глоссария: <abbreviated-form keyref="cpu-key"/>; по ключу без глоссария: <abbreviated-form keyref="plain-key"/>; по несуществующему ключу: <abbreviated-form keyref="no-such-key"/>; без keyref: <abbreviated-form/>.</p>
    <p><overline>надчёркнутый</overline> <q>цитата</q> <cite>Источник</cite> <menucascade><uicontrol>Файл</uicontrol><uicontrol>Открыть</uicontrol></menucascade> <state name="mode" value="on"/> <boolean state="yes"/> <tm tmtype="reg">Reg</tm> <tm tmtype="service">Serv</tm> <tm>Trade</tm></p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест HtmlRenderer</title>
  <keydef keys="cpu-key" href="glossary.dita"/>
  <keydef keys="plain-key" href="plain.dita"><topicmeta><keywords><keyword>Обычная ссылка</keyword></keywords></topicmeta></keydef>
  <topicref href="main.dita"/>
  <topicref href="glossary.dita"/>
  <topicref href="plain.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var htmlResult = new HtmlPublisher(project).Publish(
                Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "html-out"), SingleFile = true });
            var html = File.ReadAllText(htmlResult.EntryFile);

            // FigureNumber/TableNumber: публично объявленные счётчики, нигде в проекте больше не
            // используемые (HtmlPublisher их не трогает) — минимальная проверка самих геттеров/сеттеров.
            var standaloneRenderer = new HtmlRenderer(project) { FigureNumber = 5, TableNumber = 3 };
            Check(standaloneRenderer.FigureNumber == 5 && standaloneRenderer.TableNumber == 3,
                "FigureNumber/TableNumber — обычные читаемые/записываемые свойства");

            Check(html.Contains("Вложенный топик") && html.Contains("Внутри вложенного."), "вложенный топик (RenderNestedTopic) отрисован");
            Check(html.Contains("Пользовательский раздел"), "section без title использует spectitle как заголовок");

            Check(html.Contains("class=\"typeoftext\"") && html.Contains("ОПАСНО") && html.Contains("Не делайте так."),
                "hazardstatement/messagepanel отрисован по произвольным именам дочерних элементов");

            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<col style=\"width:2\\*\"[^>]*/>|<col />"), "CALS-таблица: colgroup сгенерирован");
            Check(html.Contains("<th>H1</th>") || html.Contains("<th>H1"), "CALS-таблица: заголовок thead/th отрисован");
            Check(html.Contains("colspan=\"2\""), "CALS-таблица: namest/nameend дали colspan");
            Check(html.Contains("rowspan=\"2\""), "CALS-таблица: morerows=1 дал rowspan=2 (morerows+1)");
            Check(html.Contains("text-align:center"), "CALS-таблица: align превращён в style");
            Check(html.Contains("vertical-align:top"), "CALS-таблица: valign превращён в style");
            Check(html.Contains("Заголовок 1. Заголовок таблицы") || html.Contains("Таблица 1. Заголовок таблицы"),
                $"CALS-таблица пронумерована подписью (реальный текст подписи см. Labels.Table)");

            Check(html.Contains("<object data=\"media/flashthing.bin\"") || html.Contains("<object data=\"flashthing.bin\""),
                "object с data встроен как <object>");
            Check(html.Contains("Без data — дети рендерятся как есть"), "object без data рендерит своих детей как есть");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<video src=\"[^\"]*video\\.mp4\" controls>"), "video с href и без controls=\"false\" получает атрибут controls");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<audio src=\"[^\"]*audio\\.mp3\"></audio>"), "audio с controls=\"false\" не получает атрибут controls");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<video src=\"[^\"]*video\\.mp4\" controls></video>\\s*<video src=\"[^\"]*video\\.mp4\" controls>") ||
                  html.Split("video.mp4").Length - 1 >= 2,
                "video без href, но с media-source, тоже находит источник");

            Check(html.Contains("<circle r=\"5\"") && html.Contains("<svg"), "svg-container передан как есть (RenderForeign)");

            Check(html.Contains("int main() { return 0; }"), "coderef на существующий файл вставляет его содержимое");
            Check(html.Contains("<!-- coderef не найден: missing.txt -->"), "coderef на несуществующий файл даёт HTML-комментарий, а не падает");

            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a class=\"fn-ref\" href=\"#fn1\"[^>]*>\\[\\*\\]</a>"), "сноска с callout использует его как маркер вместо номера");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a class=\"fn-ref\" href=\"#fn2\"[^>]*>\\[2\\]</a>"), "вторая сноска без callout нумеруется автоматически");
            Check(html.Contains("class=\"footnotes\"") && html.Contains("Особая сноска.") && html.Contains("Вторая сноска."),
                "блок сносок в конце топика собрал обе сноски по тексту");

            Check(html.Contains("class=\"index-terms\""), "предметный указатель отрисован (RenderIndexSection)");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<li>CPU\\s*<a href=\"[^\"]*\">1</a>"),
                "верхний уровень указателя: термин CPU встретился дважды в одном топике, но Distinct() схлопнул ссылки в одну");
            Check(html.Contains("<li>детали"), "вложенный indexterm стал подпунктом указателя");

            Check(html.Contains("<abbr class=\"abbreviated-form\">CPU</abbr>"), "abbreviated-form: акроним из глоссария найден и использован");
            Check(html.Contains("<abbr class=\"abbreviated-form\">Обычная ссылка</abbr>"), "abbreviated-form: ключ резолвится, но цель не глоссарий — используется KeyText ключа");
            Check(html.Contains("<abbr class=\"abbreviated-form\">no-such-key</abbr>"), "abbreviated-form: ключ не резолвится вовсе — используется буквальный keyref");
            Check(!html.Contains("<abbr class=\"abbreviated-form\"></abbr>"), "abbreviated-form без keyref не создаёт пустой <abbr>");

            Check(html.Contains("<span style=\"text-decoration:overline\">надчёркнутый</span>"), "overline отрисован");
            Check(html.Contains("<q>цитата</q>"), "q отрисован как <q>");
            Check(html.Contains("<cite>Источник</cite>"), "cite отрисован");
            Check(html.Contains("class=\"menucascade\"") && html.Contains("Файл") && html.Contains("Открыть") && html.Contains("&rarr;"),
                "menucascade собрал цепочку uicontrol через разделитель");
            Check(html.Contains("class=\"state\">mode=on</span>"), "state отрисован как name=value");
            Check(html.Contains("class=\"boolean\">yes</span>"), "boolean отрисован по атрибуту state");
            Check(html.Contains("Reg&reg;") || html.Contains("Reg&amp;reg;"), "tm tmtype=\"reg\" даёт символ ®");
            Check(html.Contains("Serv&#8480;") || html.Contains("Serv&amp;#8480;"), "tm tmtype=\"service\" даёт символ ℠");
            Check(html.Contains("Trade&trade;") || html.Contains("Trade&amp;trade;"), "tm без tmtype по умолчанию — ™");
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

    // ------------------------------------------- списки и шаги (диспетчер рендера)

    /// <summary>
    /// Характеризационные проверки для веток switch, которые сознательно НЕ объединены
    /// общей категорией (ul/sl/choices, steps/steps-unordered, li/step) — HtmlRenderer и
    /// DocxRenderer расходятся тут по возможностям формата. Фиксируют текущее поведение,
    /// чтобы следующий шаг унификации диспетчера не сломал его молча.
    /// </summary>
    internal static void ListAndStepsDispatchTests()
    {
        Section("Списки и шаги: защита перед унификацией диспетчера рендера");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "lists.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="lists">
  <title>Списки</title>
  <conbody>
    <ul>
      <li>Обычный пункт<ul><li>Вложенный пункт</li></ul></li>
    </ul>
    <ol>
      <li>Пункт по порядку</li>
    </ol>
    <sl>
      <sli>Простой пункт</sli>
    </sl>
    <choices>
      <choice>Вариант выбора</choice>
    </choices>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "steps.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="steps-task">
  <title>Шаги</title>
  <taskbody>
    <steps>
      <step>
        <cmd>Первый шаг</cmd>
        <info><p>Пояснение к шагу</p></info>
        <substeps>
          <substep><cmd>Подшаг А</cmd></substep>
        </substeps>
        <stepresult><p>Результат первого шага</p></stepresult>
      </step>
    </steps>
    <steps-unordered>
      <step><cmd>Проверить диск</cmd></step>
    </steps-unordered>
  </taskbody>
</task>
""");

            File.WriteAllText(Path.Combine(root, "coverage.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест покрытия списков и шагов</title>
  <topicref href="lists.dita"/>
  <topicref href="steps.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            // --- HTML ---
            var htmlPublisher = new HtmlPublisher(project);
            var htmlResult = htmlPublisher.Publish(Path.Combine(root, "coverage.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-html"),
                SingleFile = true
            });
            var html = File.ReadAllText(htmlResult.EntryFile);

            Check(html.Contains("<li>Обычный пункт"), "html: <li> обычного ul отрисован");
            Check(html.Contains("<li>Вложенный пункт</li>"), "html: вложенный список внутри <li> дошёл до вывода");
            Check(html.Contains("<ul class=\"sl\""), "html: <sl> обёрнут в <ul class=\"sl\">");
            Check(html.Contains("<li>Простой пункт</li>"), "html: <sli> отрисован как <li>");
            Check(html.Contains("<ul class=\"choices\""), "html: <choices> обёрнут в <ul class=\"choices\">");
            Check(html.Contains("<li>Вариант выбора</li>"), "html: <choice> отрисован как <li>");
            Check(html.Contains("<li>Пункт по порядку</li>"), "html: <li> обычного ol отрисован");

            Check(Regex.Matches(html, "Порядок действий").Count == 2,
                "html: заголовок шагов сгенерирован и для <steps>, и для <steps-unordered>");
            Check(html.Contains("<ol class=\"steps\""), "html: <steps> обёрнут в <ol class=\"steps\">");
            Check(html.Contains("<ul class=\"steps\""), "html: <steps-unordered> обёрнут в <ul class=\"steps\">");
            Check(html.Contains("<div class=\"cmd\">Первый шаг</div>"), "html: <cmd> отрисован своим div");
            Check(html.Contains("<div class=\"info\">") && html.Contains("Пояснение к шагу"),
                "html: <info> отрисован своим div");
            Check(html.Contains("<div class=\"stepresult\">") && html.Contains("Результат первого шага"),
                "html: <stepresult> отрисован своим div");
            Check(html.Contains("Подшаг А"), "html: <substeps>/<substep> дошли до вывода");
            Check(html.Contains("Проверить диск"), "html: шаг внутри <steps-unordered> отрисован");

            // --- DOCX ---
            var docxOut = Path.Combine(root, "out.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "coverage.ditamap"), new PublishOptions(), docxOut);
            CheckValidDocx(docxOut, "Списки и шаги: защита перед унификацией диспетчера рендера");

            using var doc = WordprocessingDocument.Open(docxOut, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Обычный пункт") && text.Contains("Вложенный пункт"), "docx: ul и вложенный ul отрисованы");
            Check(text.Contains("Простой пункт"), "docx: sl отрисован");
            Check(text.Contains("Вариант выбора"), "docx: choices отрисован");
            Check(text.Contains("Пункт по порядку"), "docx: ol отрисован");
            Check(text.Contains("Первый шаг") && text.Contains("Пояснение к шагу") && text.Contains("Результат первого шага"),
                "docx: cmd/info/stepresult шага отрисованы");
            Check(text.Contains("Подшаг А"), "docx: substeps/substep отрисованы");
            Check(text.Contains("Проверить диск"), "docx: шаг внутри steps-unordered отрисован");

            var bulletGroup = new[] { "Обычный пункт", "Простой пункт", "Вариант выбора", "Проверить диск" }
                .Select(t => FindNumberingAbstractId(doc, t)).ToList();
            var decimalGroup = new[] { "Пункт по порядку", "Первый шаг", "Подшаг А" }
                .Select(t => FindNumberingAbstractId(doc, t)).ToList();

            Check(bulletGroup.All(id => id is not null) && bulletGroup.Distinct().Count() == 1,
                $"docx: ul/sl/choices/steps-unordered используют одно и то же маркированное оформление списка: [{string.Join(",", bulletGroup)}]");
            Check(decimalGroup.All(id => id is not null) && decimalGroup.Distinct().Count() == 1,
                $"docx: ol/steps/substeps используют одно и то же нумерованное оформление списка: [{string.Join(",", decimalGroup)}]");
            Check(bulletGroup[0] != decimalGroup[0],
                "docx: маркированный и нумерованный список используют разное оформление (ordered-флаг не перепутан)");
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

    private static int? FindNumberingAbstractId(WordprocessingDocument doc, string paragraphText)
    {
        var body = doc.MainDocumentPart!.Document.Body!;
        var paragraph = body.Descendants<Paragraph>().FirstOrDefault(p => p.InnerText.Contains(paragraphText));
        var numId = paragraph?.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
        if (numId is null)
        {
            return null;
        }

        var numbering = doc.MainDocumentPart.NumberingDefinitionsPart!.Numbering!;
        return numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId)?.AbstractNumId?.Val?.Value;
    }
}
