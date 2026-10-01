using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Д3: маркер (выделение текста цветом, как в Word) — классы mark-…, HTML, DOCX (w:highlight и w:shd).
internal static partial class CoreChecks
{
    internal static void MarkerTests()
    {
        Section("Маркер: классы mark-…, HTML и DOCX");

        // Классы и разбор.
        Check(TextFormatting.Marks.Select(m => m.Token).SequenceEqual(new[] { "mark-red", "mark-yellow", "mark-green", "mark-blue" }), "четыре стандартных цвета маркера");
        Check(TextFormatting.ParseMarkToken("mark-yellow") == "#FFFF00" && TextFormatting.ParseMarkToken("mark-ff8800") == "#FF8800" &&
              TextFormatting.ParseMarkToken("mark-zzzzzz") is null && TextFormatting.ParseMarkToken("color-red") is null && TextFormatting.ParseMarkToken("mark-fff") is null,
            "цвет по классу: стандартное имя и свой шестнадцатеричный код; чужое и неверное — не маркер");
        Check(TextFormatting.MarkToken("#ff8800") == "mark-ff8800" && TextFormatting.MarkToken("FFFF00") == "mark-yellow" && TextFormatting.MarkToken("#12") is null,
            "класс по цвету: свой — mark-ff8800, совпадающий со стандартным — стандартное имя, неверный — null");
        Check(TextFormatting.IsCustomMark("mark-ff8800") && !TextFormatting.IsCustomMark("mark-yellow") && TextFormatting.CustomMarkCss("mark-ff8800") == "background-color: #FF8800" &&
              TextFormatting.CustomClassCss("size-13_5") == "font-size: 13.5pt" && TextFormatting.CustomClassCss("mark-red") is null,
            "правило CSS своих классов: размер и цвет маркера; стандартные — во встроенном CSS");
        var node = DitaNode.Element("ph");
        node.SetAttribute("outputclass", "color-red mark-green size-14");
        Check(TextFormatting.MarkOf(node) == "#00FF00" && TextFormatting.ColorOf(node) == "#C00000" && TextFormatting.SizeOf(node) == 14, "маркер, цвет и размер — независимые группы классов");
        Check(TextFormatting.SetToken(node, TextFormatting.MarkPrefix, "mark-red") && node.GetAttribute("outputclass") == "color-red size-14 mark-red", "замена маркера не трогает другие классы");
        Check(TextFormatting.SetToken(node, TextFormatting.MarkPrefix, null) && node.GetAttribute("outputclass") == "color-red size-14", "«Нет цвета» снимает только маркер");
        Check(TextFormatting.IsDarkColor("#0000FF") && TextFormatting.IsDarkColor("#000080") && TextFormatting.IsDarkColor("#800000") && !TextFormatting.IsDarkColor("#FF0000") && !TextFormatting.IsDarkColor("#FFFF00") && !TextFormatting.IsDarkColor("#808080"),
            "тёмный фон: синий и тёмные цвета — да; красный, жёлтый, серый — нет");
        Check(TextFormatting.CustomMarkCss("mark-000070") == "background-color: #000070; color: #FFFFFF" && TextFormatting.Css.Contains(".mark-blue { background-color: #0000FF; color: #FFFFFF; }") &&
              TextFormatting.Css.IndexOf(".mark-blue", StringComparison.Ordinal) < TextFormatting.Css.IndexOf(".color-red", StringComparison.Ordinal),
            "тёмный маркер ставит белый текст по умолчанию, а явный цвет текста (он в CSS позже) главнее");
        Check(TextFormatting.Css.Contains(".mark-yellow { background-color: #FFFF00; }"), "встроенный CSS знает стандартные цвета маркера");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>Т</title><body>
                <p>Начало <ph outputclass="mark-yellow">ЖЁЛТЫЙ</ph> <ph outputclass="mark-red">КРАСНЫЙ</ph> <ph outputclass="mark-ff8800">СВОЙ</ph>
                <ph outputclass="mark-blue size-14">СИНИЙ_КРУПНО</ph> <ph outputclass="mark-00ffff">БИРЮЗОВЫЙ</ph> <ph outputclass="mark-000070">ТЁМНЫЙ</ph> <ph outputclass="mark-000080">ПАЛИТРА_ТЁМНЫЙ</ph> <ph outputclass="mark-blue color-red">СИНИЙ_КРАСНЫЙ_ТЕКСТ</ph> конец.</p>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>М</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            var map = Path.Combine(root, "m.ditamap");
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(map, new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(html.Contains("mark-yellow") && html.Contains(".mark-yellow { background-color: #FFFF00; }"), "HTML: стандартный маркер — класс и встроенное правило");
            Check(html.Contains("style=\"background-color: #FF8800\""), "HTML: свой цвет маркера — стилем элемента");
            Check(html.Contains("mark-blue size-14") || (html.Contains("mark-blue") && html.Contains("size-14")), "HTML: маркер и размер вместе");

            var docx = Path.Combine(root, "d.docx");
            new DocxPublisher(project).Publish(map, new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "маркер");
            using var doc = WordprocessingDocument.Open(docx, false);
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToList();

            Run RunOf(string text) => doc.MainDocumentPart!.Document.Body!.Descendants<Run>().First(r => r.InnerText.Contains(text));

            // Стиль знаков, на который ссылается прогон с данным текстом (заливка и размер лежат в стиле, выделение — в самом прогоне).
            StyleRunProperties? StyleOf(Run run) => styles.FirstOrDefault(s => s.StyleId?.Value == run.RunProperties?.RunStyle?.Val?.Value)?.StyleRunProperties;

            Check(RunOf("ЖЁЛТЫЙ").RunProperties?.Highlight?.Val?.Value == HighlightColorValues.Yellow, "DOCX: жёлтый — настоящее выделение текста (w:highlight yellow) на прогоне");
            Check(RunOf("КРАСНЫЙ").RunProperties?.Highlight?.Val?.Value == HighlightColorValues.Red, "DOCX: красный — w:highlight red");
            var blue = RunOf("СИНИЙ_КРУПНО");
            Check(blue.RunProperties?.Highlight?.Val?.Value == HighlightColorValues.Blue && StyleOf(blue)?.FontSize?.Val?.Value == "28" && StyleOf(blue)?.Shading is null,
                "DOCX: синий маркер вместе с размером 14 пт: выделение на прогоне, размер в стиле, заливки в стиле нет");
            var custom = RunOf("СВОЙ");
            Check(custom.RunProperties?.Highlight is null && StyleOf(custom)?.Shading?.Fill?.Value == "FF8800", "DOCX: свой цвет — заливка знаков в стиле (w:shd FF8800)");
            Check(RunOf("БИРЮЗОВЫЙ").RunProperties?.Highlight?.Val?.Value == HighlightColorValues.Cyan, "DOCX: свой цвет из палитры Word (00FFFF) — тоже w:highlight");
            Check(blue.RunProperties?.Color?.Val?.Value == "FFFFFF", "DOCX: на тёмном выделении (синий) текст белый");
            var darkCustom = RunOf("ТЁМНЫЙ");
            Check(StyleOf(darkCustom)?.Shading?.Fill?.Value == "000070" && StyleOf(darkCustom)?.Color?.Val?.Value == "FFFFFF", "DOCX: свой тёмный цвет вне палитры Word — заливка и белый текст в стиле");
            var darkPalette = RunOf("ПАЛИТРА_ТЁМНЫЙ");
            Check(darkPalette.RunProperties?.Highlight?.Val?.Value == HighlightColorValues.DarkBlue && darkPalette.RunProperties?.Color?.Val?.Value == "FFFFFF", "DOCX: тёмно-синий из палитры Word — выделение и белый текст на прогоне");
            var redOnBlue = RunOf("СИНИЙ_КРАСНЫЙ_ТЕКСТ");
            Check(redOnBlue.RunProperties?.Color is null && StyleOf(redOnBlue)?.Color?.Val?.Value == "C00000", "DOCX: явный цвет текста на тёмном маркере остаётся (белый не навязывается)");
            var plain = RunOf("Начало");
            Check(plain.RunProperties?.Highlight is null && StyleOf(plain)?.Shading is null, "DOCX: текст без маркера без выделения");
        });
    }
}
