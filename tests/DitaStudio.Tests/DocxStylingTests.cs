using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Оформление DOCX: пользовательский CSS → стили Word и вёрстка «Оформление DOCX» (DocxLayout).
public static partial class Program
{
    private static void DocxStylingTests()
    {
        Section("DOCX: оформление из CSS и вёрстка");
        CssParserChecks();
        CssValueChecks();
        DocxStyleSheetChecks();
        DocxLayoutChecks();

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DocxStyledExportChecks(root);
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

    private static void CssParserChecks()
    {
        var sheet = CssParser.Parse("""
            /* комментарий { не правило } */
            p, .shortdesc { color: red; font-size: 12px !important }
            @media screen { h1 { color: blue } }
            @media print { h2 { color: green } }
            @media docx { h3 { color: navy } }
            @media print and (max-width: 600px) { h4 { color: gray } }
            @page { size: A4 landscape; margin: 10mm }
            @page :first { margin: 0 }
            @font-face { font-family: X; src: url("x.woff") }
            @import url("other.css");
            .a > .b { content: "}" }
            """, CssParser.DocxMedia);

        var selectors = sheet.Rules.SelectMany(r => r.Selectors).ToList();
        Check(selectors.Contains("p") && selectors.Contains(".shortdesc"), "css: список селекторов через запятую разбирается");
        Check(!selectors.Contains("h1"), "css: @media screen к DOCX не применяется");
        Check(selectors.Contains("h2") && selectors.Contains("h3"), "css: @media print и собственный @media docx применяются");
        Check(!selectors.Contains("h4"), "css: запрос с условием по ширине пропускается");
        Check(selectors.Contains(".a>.b"), "css: пробелы вокруг > нормализуются, «}» внутри строки не рвёт правило");
        Check(sheet.Rules.First().Declarations.Any(d => d.Property == "font-size" && d.Value == "12px"),
            "css: !important отбрасывается, значение остаётся");
        Check(sheet.PageRules.Count == 2 && sheet.PageRules.Count(r => r.Pseudo is null) == 1, "css: @page и @page :first разбираются");
        Check(sheet.SkippedAtRules.Contains("@font-face") && sheet.SkippedAtRules.Contains("@import"),
            "css: @font-face и @import отмечаются как пропущенные");
        Check(!sheet.Rules.Any(r => r.Selectors.Any(s => s.Contains("комментарий"))), "css: комментарии вырезаются");
    }

    private static void CssValueChecks()
    {
        Check(CssValues.TryParseLength("16px", out var px) && Math.Abs(px.ToPoints(11) - 12) < 0.001, "css: 16px = 12pt");
        Check(CssValues.TryParseLength("1.5em", out var em) && Math.Abs(em.ToPoints(10) - 15) < 0.001, "css: 1.5em от 10pt = 15pt");
        Check(CssValues.TryParseLength("25.4mm", out var mm) && Math.Abs(mm.ToPoints(11) - 72) < 0.001, "css: 25.4mm = 72pt");
        Check(CssValues.TryParseLength("0", out var zero) && zero.ToPoints(11) == 0, "css: 0 без единицы допустим");
        Check(!CssValues.TryParseLength("12", out _), "css: число без единицы (кроме 0) — не длина");
        Check(CssValues.TryParseColor("#abc", out var shortHex) && shortHex == "AABBCC", "css: #abc → AABBCC");
        Check(CssValues.TryParseColor("rgb(255, 0, 128)", out var rgb) && rgb == "FF0080", "css: rgb() → FF0080");
        Check(CssValues.TryParseColor("rgba(0 0 0 / 50%)", out var rgba) && rgba == "000000", "css: rgba() — альфа отбрасывается");
        Check(CssValues.TryParseColor("Navy", out var named) && named == "000080", "css: именованный цвет");
        Check(CssValues.TryParseColor("transparent", out var none) && none == string.Empty, "css: transparent — сброс заливки");
        Check(!CssValues.TryParseColor("linear-gradient(red, blue)", out _), "css: градиент — не цвет");
        Check(CssValues.ParseFontFamily("-apple-system, \"Segoe UI\", sans-serif") == "Segoe UI",
            "css: системные псевдонимы пропускаются, берётся первое настоящее семейство");
        Check(CssValues.ParseFontFamily("monospace") == "Consolas", "css: monospace → Consolas");
        Check(CssValues.TryParseFontSize("larger", 10, out var larger) && Math.Abs(larger - 12) < 0.001, "css: font-size: larger — ×1.2");
    }

    private static void DocxStyleSheetChecks()
    {
        var defaults = DocxStyleSheet.Default;
        Check(defaults.Warnings.Count == 0, "docx-css: без CSS нет предупреждений");
        Check(defaults.Styles[DocxStyleCatalog.CodeBlock].FontFamily == "Consolas" &&
              defaults.Styles[DocxStyleCatalog.CodeBlock].Background == "F2F2F2",
            "docx-css: стиль по умолчанию повторяет прежний экспорт (блок кода — Consolas на сером)");
        Check(Math.Abs(defaults.FontSizeOf("Heading1") - 18) < 0.001, "docx-css: заголовок 1 по умолчанию — 18pt");

        var sheet = DocxStyleSheet.FromCss("""
            :root { --accent: #1a5fb4; }
            body { font-family: Georgia, serif; font-size: 16px; margin: 40px auto; background: #eee; }
            p { margin: 0 0 8px; line-height: 1.4; }
            h1 { color: var(--accent); font-size: 2em; border-bottom: 1px solid var(--accent); padding-bottom: 4px; }
            .note { border-left: 4px solid #e66100; background-color: #fff4e5; padding: 6px 10px; }
            .note.danger { color: rgb(192, 28, 40); }
            .shortdesc { font-style: normal; text-transform: uppercase; }
            table { border: 2px solid #333; }
            th, td { border: 1px dotted #999; padding: 4pt; }
            th { background: #ddeeff; font-weight: bold; }
            .rev-changed { border-left: 3px double #00f; }
            .warning-box { color: #aa0000; border: 1px solid #aa0000; }
            .toc a:hover { color: red; }
            #main .x { color: red; }
            p:first-child { color: red; }
            .note { box-shadow: 0 0 2px #000; border-radius: 4px; }
            h1 { font-size: calc(2em + 1px); }
            @page { size: A5 landscape; margin: 15mm 20mm; }
            """);

        var normal = sheet.Styles[DocxStyleCatalog.Normal];
        Check(normal.FontFamily == "Georgia" && Math.Abs(normal.FontSizePt!.Value - 12) < 0.001,
            "docx-css: body → стиль «Обычный» (шрифт и 16px = 12pt)");
        Check(normal.Background is null && normal.IndentLeftPt is null && normal.SpaceBeforePt is null,
            "docx-css: поля и фон body не становятся оформлением каждого абзаца");
        var bodyText = sheet.Styles[DocxStyleCatalog.BodyText];
        Check(Math.Abs(bodyText.SpaceAfterPt!.Value - 6) < 0.001 && bodyText.SpaceBeforePt == 0,
            "docx-css: margin у p → интервалы стиля «Основной текст»");
        Check(bodyText.LineHeight is { Rule: DocxLineRule.Multiple } lh && Math.Abs(lh.Value - 1.4) < 0.001,
            "docx-css: line-height без единицы → множитель");
        Check(normal.SpaceAfterPt is null && normal.LineHeight is null,
            "docx-css: правила p не попадают в «Обычный» — заголовки и ячейки их не наследуют");

        var h1 = sheet.Styles["Heading1"];
        Check(h1.Color == "1A5FB4", "docx-css: var(--accent) подставляется");
        Check(Math.Abs(h1.FontSizePt!.Value - 24) < 0.001, "docx-css: 2em у h1 считается от «Обычного» (12pt × 2)");
        Check(h1.BorderBottom is { Style: "single", Color: "1A5FB4" } bb && Math.Abs(bb.SpacePt - 3) < 0.001,
            "docx-css: border-bottom + padding-bottom → нижняя рамка с отступом 3pt");
        Check(h1.Bold == true, "docx-css: несвязанные свойства заголовка (жирность по умолчанию) сохраняются");

        var note = sheet.Styles[DocxStyleCatalog.Note];
        Check(note.BorderLeft is { Color: "E66100" } nl && Math.Abs(nl.WidthPt - 3) < 0.001 && Math.Abs(nl.SpacePt - 7.5) < 0.001,
            "docx-css: .note — рамка слева 4px и padding-left 10px как отступ до рамки");
        Check(note.Background == "FFF4E5", "docx-css: background-color у .note → заливка абзаца");
        Check(note.SpaceBeforePt is { } nb && Math.Abs(nb - 4.5) < 0.001,
            "docx-css: padding-top без верхней рамки → прибавка к интервалу перед");
        Check(sheet.Styles[DocxStyleCatalog.NoteDanger].Color == "C01C28" && sheet.Styles[DocxStyleCatalog.Note].Color is null,
            "docx-css: .note.danger — отдельный стиль, обычные примечания не краснеют");

        var shortdesc = sheet.Styles[DocxStyleCatalog.Shortdesc];
        Check(shortdesc.Italic == false && shortdesc.Caps == true, "docx-css: font-style: normal снимает курсив по умолчанию; uppercase → Caps");

        Check(sheet.Table.Outer is { Color: "333333" } outer && Math.Abs(outer.WidthPt - 1.5) < 0.001,
            "docx-css: рамка table → внешняя рамка таблицы");
        Check(sheet.Table.Inner is { Style: "dotted", Color: "999999" }, "docx-css: рамка th/td → внутренние линии");
        Check(sheet.Table.HeaderFill == "DDEEFF" && sheet.Table.CellPaddingPt is { } pad && Math.Abs(pad - 4) < 0.001,
            "docx-css: фон th → заливка шапки, padding ячеек → поля ячеек");
        Check(sheet.Styles[DocxStyleCatalog.TableHeading].Bold == true && sheet.Styles[DocxStyleCatalog.TableText].BorderLeft is null,
            "docx-css: у th в стиль текста попадает только текст, рамка ячейки — нет");
        Check(sheet.RevBorder is { Style: "double", Color: "0000FF" }, "docx-css: .rev-changed → полоса пометки изменений");

        Check(sheet.CustomClasses.Contains("warning-box"), "docx-css: неизвестный класс — правило для outputclass");
        var overlay = sheet.PropsForClasses(new[] { "warning-box" }, 11);
        Check(overlay is { Color: "AA0000" } && overlay.BorderTop is not null, "docx-css: оформление класса outputclass");

        Check(Math.Abs(sheet.Page.WidthPt - 210 * DocxPageSetup.MmToPt) < 0.01 &&
              Math.Abs(sheet.Page.HeightPt - 148 * DocxPageSetup.MmToPt) < 0.01,
            "docx-css: @page size: A5 landscape");
        Check(Math.Abs(sheet.Page.TopPt - 15 * DocxPageSetup.MmToPt) < 0.01 && Math.Abs(sheet.Page.LeftPt - 20 * DocxPageSetup.MmToPt) < 0.01,
            "docx-css: @page margin с двумя значениями");

        var warnings = string.Join("\n", sheet.Warnings);
        Check(warnings.Contains("#main .x") && warnings.Contains("p:first-child"), "docx-css: предупреждение о неподдерживаемых селекторах");
        Check(!warnings.Contains(".toc"), "docx-css: служебные классы сайта и :hover молча пропускаются");
        Check(warnings.Contains("box-shadow") && warnings.Contains("border-radius"), "docx-css: предупреждение о свойствах, которых нет в Word");
        Check(warnings.Contains("calc("), "docx-css: нераспознанное значение попадает в предупреждение");
        Check(!warnings.Contains("margin"), "docx-css: поддержанные свойства в предупреждения не попадают");

        var docxOnly = DocxStyleSheet.FromCss("@media screen { p { color: red } } @media docx { p { color: green } }");
        Check(docxOnly.Styles[DocxStyleCatalog.BodyText].Color == "008000", "docx-css: @media docx — правила только для Word");

        ListMarkerChecks();

        var cascade = DocxStyleSheet.FromCss(".note.warning { color: #111 } .note { color: #222 }");
        Check(cascade.Styles[DocxStyleCatalog.NoteWarning].Color == "111111",
            "docx-css: более специфичный селектор побеждает независимо от порядка");
    }

    private static void ListMarkerChecks()
    {
        // Символы Symbol/Wingdings из области частного использования Unicode невидимы в редакторах
        // и незаметно теряются при перезаписи файла — в исходниках только \uXXXX.
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "DitaStudio.sln")))
        {
            repo = repo.Parent;
        }

        if (repo is not null)
        {
            var withPrivateUse = Directory.EnumerateFiles(Path.Combine(repo.FullName, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => File.ReadAllText(f).Any(c => c is >= '\uE000' and <= '\uF8FF'))
                .Select(Path.GetFileName)
                .ToList();
            Check(withPrivateUse.Count == 0,
                $"исходники: нет невидимых символов области частного использования Unicode ({string.Join(", ", withPrivateUse)})");
        }

        var defaults = DocxStyleSheet.Default;
        Check(defaults.BulletMarker(0) == DocxListMarker.Disc && defaults.BulletMarker(1) == DocxListMarker.Circle &&
              defaults.BulletMarker(2) == DocxListMarker.Square && defaults.BulletMarker(3) == DocxListMarker.Disc,
            "маркеры: по умолчанию disc, circle, square и дальше по кругу — как в браузере");
        Check(DocxListMarker.Disc.Text == "\uF0B7" && DocxListMarker.Disc.Font == "Symbol", "маркеры: disc — символ Symbol U+F0B7");

        var sheet = DocxStyleSheet.FromCss("""
            ul { list-style-type: square; }
            ul ul { list-style: "– " inside; }
            ul ul ul ul { list-style-type: none; }
            li::marker { color: #1a5fb4; }
            ul ul li::marker { color: red; }
            ol li::marker { color: green; }
            .dash { list-style-type: "—"; }
            ul.star { list-style: "★"; }
            .sl { list-style: none; }
            ol { list-style-type: lower-alpha; }
            ul { list-style-type: url(bullet.png); }
            """);

        Check(sheet.BulletMarker(0) == DocxListMarker.Square, "маркеры: ul { list-style-type: square } — первый уровень");
        Check(sheet.BulletMarker(1) is { Text: "–", Font: null, None: false } && sheet.BulletMarker(2).Text == "–",
            "маркеры: ul ul со строкой — второй уровень и глубже, шрифт абзаца, пробел отрезан");
        Check(sheet.BulletMarker(3).None && sheet.BulletMarker(8).None, "маркеры: none — список без маркера с 4-го уровня");
        Check(sheet.BulletColor(0) == "1A5FB4" && sheet.BulletColor(1) == "FF0000" && sheet.BulletColor(5) == "FF0000",
            "маркеры: li::marker { color } — цвет, более специфичное ul ul li::marker — глубже");
        Check(sheet.OrderedColor == "008000", "маркеры: ol li::marker { color } — цвет номеров");
        Check(sheet.MarkerForClasses(new[] { "dash" }) is { Text: "—" } &&
              sheet.MarkerForClasses(new[] { "star", "ul" }) is { Text: "★" },
            "маркеры: .dash и ul.star — свой маркер у списков с таким outputclass");
        Check(sheet.MarkerForClasses(new[] { "sl" })?.None == true, "маркеры: .sl — простой список (sl) без маркеров");
        Check(sheet.MarkerForClasses(new[] { "other" }) is null, "маркеры: для класса без правила — null");

        var warnings = string.Join("\n", sheet.Warnings);
        Check(warnings.Contains("lower-alpha") && warnings.Contains("url(bullet.png)"),
            "маркеры: вид нумерации ol и картинка-маркер — в предупреждениях");
        Check(!warnings.Contains("селекторы") , "маркеры: ul, ul ul, li::marker не считаются неподдерживаемыми селекторами");
        Check(DocxStyleSheet.FromCss("li { color: red; list-style: square }").Styles[DocxStyleCatalog.ListItem].Color == "FF0000" &&
              DocxStyleSheet.FromCss("li { list-style: square }").BulletMarker(0) == DocxListMarker.Square,
            "маркеры: li остаётся стилем «Элемент списка» и заодно задаёт маркер");
    }

    private static void DocxLayoutChecks()
    {
        var layout = new DocxLayout
        {
            TitlePage = false, Subtitle = "Подзаголовок", Author = "ООО «Ромашка»", TocDepth = 9,
            NumberHeadings = true, HeaderText = "{title} — стр. {page}", HeaderAlignment = DocxHeaderAlignment.Left,
            GutterMm = 12.5, Language = "en-US"
        };
        layout.Normalize();
        Check(layout.TocDepth == 6, "docx-layout: глубина оглавления ограничивается 6");

        var copy = DocxLayout.FromJson(layout.ToJson());
        Check(copy.TitlePage == false && copy.Author == "ООО «Ромашка»" && copy.NumberHeadings &&
              copy.HeaderAlignment == DocxHeaderAlignment.Left && Math.Abs(copy.GutterMm - 12.5) < 0.001 && copy.Language == "en-US",
            "docx-layout: JSON туда и обратно");
        Check(layout.ToJson().Contains("\"Left\""), "docx-layout: перечисления хранятся словами, а не числами");

        var partial = DocxLayout.FromJson("{\"NumberHeadings\": true, \"Unknown\": 1}");
        Check(partial.NumberHeadings && partial.TableOfContents && partial.TocDepth == 3,
            "docx-layout: недостающие поля — по умолчанию, неизвестные пропускаются");
    }

    private static void DocxStyledExportChecks(string root)
    {
        File.WriteAllText(Path.Combine(root, "a.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="a">
  <title>Первый раздел</title>
  <shortdesc>Кратко.</shortdesc>
  <conbody>
    <p outputclass="warning-box">Важный абзац с <codeph>кодом</codeph> и <ph outputclass="accent">акцентом</ph>.</p>
    <note type="danger">Опасно.</note>
    <section outputclass="warning-box"><title>Раздел</title><p>Внутри раздела.</p></section>
    <p><keyword>Слово</keyword> и <xref href="b.dita">ссылка</xref>.</p>
    <table><tgroup cols="1"><thead><row><entry>Шапка</entry></row></thead><tbody><row><entry>Ячейка</entry></row></tbody></tgroup></table>
    <ul outputclass="dash"><li>Пункт с тире</li></ul>
    <ul><li>Пункт с квадратом<ul><li>Вложенный пункт</li></ul></li></ul>
  </conbody>
</concept>
""");
        File.WriteAllText(Path.Combine(root, "b.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="b"><title>Второй раздел</title><taskbody><steps><step><cmd>Шаг</cmd></step></steps></taskbody></task>
""");
        File.WriteAllText(Path.Combine(root, "m.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map><title>Книга</title><topicref href="a.dita"/><topicref href="b.dita"/></map>
""");
        File.WriteAllText(Path.Combine(root, "custom.css"), """
body { font-family: Georgia; }
h1 { color: #1a5fb4; }
.note.danger { color: #c01c28; }
.warning-box { color: #aa0000; }
.accent { font-weight: bold; color: #0000ff; }
.keyword { font-style: italic; }
th { background: #ddeeff; }
ul { list-style-type: square; }
.dash { list-style-type: "—"; }
li::marker { color: #1a5fb4; }
@page { size: letter; margin: 1in; }
""");

        var project = new DitaProject(root);
        project.Scan();
        var mapPath = Path.Combine(root, "m.ditamap");

        // 1. Без CSS и с настройками по умолчанию — валидный документ, прежний вид
        var plain = Path.Combine(root, "plain.docx");
        new DocxPublisher(project).Publish(mapPath, new PublishOptions { Language = "ru" }, plain);
        Check(ValidationErrors(plain) is var e0 && e0.Count == 0, $"docx-export: документ без CSS проходит проверку OpenXML ({string.Join("; ", ValidationErrors(plain).Take(3))})");
        using (var doc = WordprocessingDocument.Open(plain, false))
        {
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
            Check(styles.Elements<Style>().Any(s => s.StyleId == "CodeChar") && styles.Elements<Style>().Any(s => s.StyleId == "Note"),
                "docx-export: именованные стили (CodeChar, Note…) есть в документе");
            var body = doc.MainDocumentPart.Document.Body!;
            Check(body.Descendants<Run>().Any(r => r.RunProperties?.RunStyle?.Val == "CodeChar" && r.InnerText == "кодом"),
                "docx-export: codeph получает символьный стиль, а не прямой шрифт");
            Check(!body.Descendants<RunFonts>().Any(), "docx-export: прямого указания шрифта в тексте больше нет");
            Check(body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "NoteDanger"),
                "docx-export: note type=danger → стиль NoteDanger");
            Check(body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Title") &&
                  body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "TOCHeading"),
                "docx-export: по умолчанию есть титул и заголовок оглавления");
            Check(body.Descendants<SimpleField>().Any(f => f.Instruction?.Value?.Contains("\"1-3\"") == true),
                "docx-export: оглавление по умолчанию — 3 уровня");
            Check(!styles.Elements<Style>().Any(s => s.StyleId!.Value!.Contains("warning_box")),
                "docx-export: без CSS производных стилей для классов не создаётся");

            Check(!body.Descendants<Text>().Any(t => t.Text.Contains("  ") || t.Text.Contains('\n')),
                "docx-export: пробелы и переносы строк исходного XML схлопываются, как в HTML");
            var numbering = doc.MainDocumentPart.NumberingDefinitionsPart!.Numbering!;
            var ordered = numbering.Elements<NumberingInstance>().Where(n => n.AbstractNumId?.Val == 1001).ToList();
            Check(ordered.Count > 0 && ordered.All(n => n.Elements<LevelOverride>().Any(o => o.StartOverrideNumberingValue?.Val == 1)),
                "docx-export: каждый нумерованный список начинается с 1, а не продолжает предыдущий");
            var bulletLevel = numbering.Elements<AbstractNum>().First(a => a.AbstractNumberId == 1000).Elements<Level>().First();
            Check(bulletLevel.LevelText?.Val == "\uF0B7", "docx-export: у маркированного списка есть символ маркера");
            var bulletFonts = bulletLevel.NumberingSymbolRunProperties!.GetFirstChild<RunFonts>()!;
            Check(bulletFonts.EastAsia == "Symbol" && bulletFonts.ComplexScript == "Symbol",
                "docx-export: шрифт маркера задан для всех групп символов (маркер не пропадает при шрифте из CSS)");
            Check(body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "BodyText"),
                "docx-export: абзацы <p> — стиль «Основной текст»");
        }

        // 2. С CSS проекта
        project.SetCustomCssPath("custom.css");
        var styled = Path.Combine(root, "styled.docx");
        var styledResult = new DocxPublisher(project).Publish(mapPath, new PublishOptions { Language = "ru" }, styled);
        Check(ValidationErrors(styled).Count == 0, $"docx-export: документ с CSS проходит проверку OpenXML ({string.Join("; ", ValidationErrors(styled).Take(3))})");
        Check(styledResult.Warnings.Count == 0, $"docx-export: для поддерживаемого CSS предупреждений нет ({string.Join("; ", styledResult.Warnings)})");
        using (var doc = WordprocessingDocument.Open(styled, false))
        {
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
            var normal = styles.Elements<Style>().First(s => s.StyleId == "Normal");
            Check(normal.StyleRunProperties?.RunFonts?.Ascii == "Georgia", "docx-export: шрифт body → стиль Normal");
            var heading1 = styles.Elements<Style>().First(s => s.StyleId == "Heading1");
            Check(heading1.StyleRunProperties?.Color?.Val == "1A5FB4", "docx-export: цвет h1 → стиль Heading1");

            var body = doc.MainDocumentPart.Document.Body!;
            var boxStyle = body.Descendants<Paragraph>()
                .Select(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
                .FirstOrDefault(id => id is not null && id.Contains("warning_box"));
            Check(boxStyle is not null, "docx-export: абзац с outputclass получает производный стиль");
            var derived = boxStyle is null ? null : styles.Elements<Style>().FirstOrDefault(s => s.StyleId == boxStyle);
            Check(derived?.BasedOn?.Val == "BodyText" && derived.StyleRunProperties?.Color?.Val == "AA0000",
                "docx-export: производный стиль основан на базовом и несёт оформление класса");
            Check(body.Descendants<Paragraph>().Count(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.Contains("warning_box") == true) >= 3,
                "docx-export: класс на section оформляет и её заголовок, и абзацы внутри");

            var accent = body.Descendants<Run>().FirstOrDefault(r => r.InnerText == "акцентом");
            var accentStyle = accent?.RunProperties?.RunStyle?.Val?.Value;
            Check(accentStyle is not null && styles.Elements<Style>().First(s => s.StyleId == accentStyle).StyleRunProperties?.Color?.Val == "0000FF",
                "docx-export: инлайн-элемент с outputclass → производный символьный стиль");
            var keyword = body.Descendants<Run>().FirstOrDefault(r => r.InnerText == "Слово");
            Check(keyword?.RunProperties?.RunStyle is not null, "docx-export: правило .keyword применяется к элементу keyword");
            var code = body.Descendants<Run>().First(r => r.InnerText == "кодом");
            Check(code.RunProperties?.RunStyle?.Val == "CodeChar", "docx-export: вложенный codeph сохраняет свой стиль внутри абзаца с классом");

            Check(body.Descendants<TableCell>().Any(c => c.TableCellProperties?.Shading?.Fill?.Value == "DDEEFF"),
                "docx-export: фон th → заливка ячеек шапки");
            var numberingDefs = doc.MainDocumentPart.NumberingDefinitionsPart!.Numbering!;
            var bulletLevels = numberingDefs.Elements<AbstractNum>().First(a => a.AbstractNumberId == 1000).Elements<Level>().ToList();
            Check(bulletLevels[0].LevelText?.Val == "\uF0A7" && bulletLevels[0].NumberingSymbolRunProperties?.GetFirstChild<Color>()?.Val == "1A5FB4",
                "docx-export: ul { list-style-type: square } и li::marker { color } → маркер и цвет первого уровня");
            Check(bulletLevels[1].LevelText?.Val == "\uF0A7", "docx-export: правило ul действует и на вложенные уровни");
            var dashAbstract = numberingDefs.Elements<AbstractNum>().FirstOrDefault(a => a.Elements<Level>().First().LevelText?.Val == "—");
            Check(dashAbstract is not null, "docx-export: список с outputclass=\"dash\" получает свою нумерацию с маркером «—»");
            var dashItem = body.Descendants<Paragraph>().First(p => p.InnerText == "Пункт с тире");
            var dashNumId = dashItem.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
            Check(dashAbstract is not null && numberingDefs.Elements<NumberingInstance>()
                    .Any(n => n.NumberID?.Value == dashNumId && n.AbstractNumId?.Val?.Value == dashAbstract.AbstractNumberId?.Value),
                "docx-export: пункт списка .dash ссылается именно на эту нумерацию");
            var pageSize = body.Elements<SectionProperties>().Last().GetFirstChild<PageSize>()!;
            Check(pageSize.Width?.Value == 12240 && pageSize.Height?.Value == 15840, "docx-export: @page size: letter → 8.5×11 дюймов");
        }

        // 3. Вёрстка из «Оформление DOCX»
        var layout = new DocxLayout
        {
            TitlePage = true, Subtitle = "Руководство", Author = "Отдел документации", TitlePageDate = true,
            TableOfContents = true, TocDepth = 2, NumberHeadings = true, NumberingDepth = 2,
            PageBreakBeforeTopLevel = true, HeaderText = "{title}", FooterText = "Стр. {page} из {pages}",
            NoHeaderOnFirstPage = true, MirrorMargins = true, GutterMm = 10, Language = "en-US",
            AutoHyphenation = true, NumberFiguresAndTables = false
        };
        project.SetDocxLayout(layout);
        var reopened = new DitaProject(root);
        Check(reopened.DocxLayout.NumberHeadings && reopened.DocxLayout.FooterText == "Стр. {page} из {pages}",
            "docx-layout: настройки сохраняются в проекте и читаются при открытии");
        reopened.Scan();

        var laidOut = Path.Combine(root, "layout.docx");
        new DocxPublisher(reopened).Publish(mapPath, new PublishOptions { Language = "ru" }, laidOut);
        Check(ValidationErrors(laidOut).Count == 0, $"docx-layout: документ с колонтитулами и нумерацией проходит проверку OpenXML ({string.Join("; ", ValidationErrors(laidOut).Take(3))})");
        using (var doc = WordprocessingDocument.Open(laidOut, false))
        {
            var main = doc.MainDocumentPart!;
            var body = main.Document.Body!;
            Check(body.Descendants<Paragraph>().Any(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Subtitle" && p.InnerText == "Руководство"),
                "docx-layout: подзаголовок на титуле");
            Check(body.Descendants<SimpleField>().Any(f => f.Instruction?.Value?.Contains("\"1-2\"") == true), "docx-layout: глубина оглавления");
            var heading1 = main.StyleDefinitionsPart!.Styles!.Elements<Style>().First(s => s.StyleId == "Heading1");
            Check(heading1.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val == 9000,
                "docx-layout: нумерация заголовков привязана к стилю Heading1");
            Check(heading1.StyleParagraphProperties?.PageBreakBefore is not null, "docx-layout: разрыв страницы перед топиком верхнего уровня");
            var heading3 = main.StyleDefinitionsPart.Styles.Elements<Style>().First(s => s.StyleId == "Heading3");
            Check(heading3.StyleParagraphProperties?.NumberingProperties is null, "docx-layout: уровни глубже заданного не нумеруются");

            var section = body.Elements<SectionProperties>().Last();
            Check(section.Elements<HeaderReference>().Count() == 2 && section.Elements<FooterReference>().Count() == 2 &&
                  section.GetFirstChild<TitlePage>() is not null,
                "docx-layout: колонтитулы и пустые колонтитулы первой страницы");
            Check(main.FooterParts.Any(f => f.Footer!.Descendants<SimpleField>().Any(sf => sf.Instruction!.Value!.Contains("NUMPAGES"))),
                "docx-layout: {pages} → поле NUMPAGES");
            Check(main.HeaderParts.Any(h => h.Header!.InnerText == "Книга"), "docx-layout: {title} → название карты");
            Check(section.GetFirstChild<PageMargin>()!.Gutter!.Value == 567, "docx-layout: переплёт 10 мм = 567 twips");
            var settings = main.DocumentSettingsPart!.Settings!;
            Check(settings.GetFirstChild<MirrorMargins>() is not null && settings.GetFirstChild<AutoHyphenation>() is not null,
                "docx-layout: зеркальные поля и автоперенос");
            Check(main.StyleDefinitionsPart.Styles.GetFirstChild<DocDefaults>()!.Descendants<Languages>().First().Val == "en-US",
                "docx-layout: язык документа");
            Check(doc.PackageProperties.Creator == "Отдел документации" && doc.PackageProperties.Title == "Книга",
                "docx-layout: свойства файла — автор и название");
        }

        var noFront = new DocxLayout { TitlePage = false, TableOfContents = false };
        var bare = Path.Combine(root, "bare.docx");
        new DocxPublisher(reopened).Publish(mapPath, new PublishOptions { Language = "ru" }, bare, styles: null, layout: noFront);
        using (var doc = WordprocessingDocument.Open(bare, false))
        {
            var first = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().First();
            Check(first.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1",
                "docx-layout: без титула и оглавления документ начинается сразу с первого топика");
        }

        File.WriteAllText(Path.Combine(root, ".ditastudio-docx"), "{ не json");
        var broken = new DitaProject(root);
        Check(broken.DocxLayoutWarning is not null && broken.DocxLayout.TableOfContents,
            "docx-layout: повреждённый файл настроек — предупреждение и значения по умолчанию");
    }

    /// <summary>Любой собранный DOCX обязан проходить проверку схемы OpenXML — иначе Word
    /// откроет его с сообщением о повреждении или молча выбросит часть содержимого.</summary>
    private static void CheckValidDocx(string path, string where)
    {
        var errors = ValidationErrors(path);
        Check(errors.Count == 0, $"DOCX ({where}) проходит проверку схемы OpenXML" +
            (errors.Count > 0 ? ": " + string.Join("; ", errors.Take(3)) : string.Empty));
    }

    private static List<string> ValidationErrors(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return new OpenXmlValidator()
            .Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToList();
    }
}
