using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Настоящий каскад CSS в DOCX (замечание В10): селекторы сопоставляются с деревом DITA —
// потомок, ребёнок, атрибут, :first-child, :nth-child(), :not(), ::before/::after.
internal static partial class CoreChecks
{
    private const string CascadeTopic = """
        <topic id="t"><title>Каскад</title><body>
        <ol><li>Один</li><li type="tip">Два</li><li>Три</li></ol>
        <ul><li>Маркер</li></ul>
        <note type="warning"><p>Внимание</p></note>
        <note type="tip"><p>Совет</p></note>
        <p outputclass="lead big">Ведущий</p>
        <p>Обычный</p>
        <p/>
        <div outputclass="box"><p>В боксе</p></div>
        <p>Вне бокса</p>
        <table><tgroup cols="1"><tbody><row><entry>Шапка</entry></row><row><entry>Тело</entry></row></tbody></tgroup></table>
        <p>Слово <keyword>ключ</keyword></p>
        </body></topic>
        """;

    internal static void CssSelectorTests()
    {
        Section("Селекторы CSS: разбор и сопоставление с деревом DITA");

        var document = DitaDocument.Parse(CascadeTopic);
        var all = document.Root.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element).ToList();
        List<string> Match(string selector)
        {
            var parsed = CssSelector.TryParse(selector, out var error);
            Check(parsed is not null, $"«{selector}» разобран ({error})");
            return parsed is null ? new List<string>() : all.Where(parsed.Matches).Select(n => n.Name + ":" + n.InnerText.Trim()).ToList();
        }

        Check(Match("ol > li").SequenceEqual(new[] { "li:Один", "li:Два", "li:Три" }), "ребёнок: ol > li — только пункты нумерованного списка");
        Check(Match("body li").Count == 4, "потомок: body li — все пункты");
        Check(Match("ul li").SequenceEqual(new[] { "li:Маркер" }), "потомок: ul li");
        Check(Match("li:first-child").Count == 2, "li:first-child — первый пункт каждого списка");
        Check(Match("li:last-child").Select(m => m).Contains("li:Три"), "li:last-child");
        Check(Match("ol > li:nth-child(2)").SequenceEqual(new[] { "li:Два" }), ":nth-child(2)");
        Check(Match("li:nth-child(odd)").Count == 3, ":nth-child(odd) — 1-й и 3-й в ol, 1-й в ul");
        Check(Match("ol li:nth-child(2n+1)").Count == 2, ":nth-child(2n+1)");
        Check(Match("ol li:nth-child(-n+2)").Count == 2, ":nth-child(-n+2) — первые два");
        Check(Match("ol li:not(:first-child)").Count == 2, ":not(:first-child)");
        Check(Match("li:not([type])").Count == 3, ":not([type]) — пункты без атрибута type");
        Check(Match("li[type=\"tip\"]").SequenceEqual(new[] { "li:Два" }), "[type=\"tip\"]");
        Check(Match("[type]").Count == 3, "[type] — есть атрибут (li и две note)");
        Check(Match("note[type^=warn] p").SequenceEqual(new[] { "p:Внимание" }), "[type^=warn] и потомок");
        Check(Match("note[type$=ip]").Count == 1 && Match("note[type*=arn]").Count == 1, "[type$=] и [type*=]");
        Check(Match("note[type~=tip]").Count == 1 && Match("note[type|=tip]").Count == 1, "[type~=] и [type|=]");
        Check(Match(".lead").Count == 1 && Match("p.big").Count == 1 && Match("p.lead.big").Count == 1 && Match("p.lead.other").Count == 0,
            "классы outputclass и их сочетания");
        Check(Match("li + li").Count == 2 && Match("li ~ li").Count == 2, "соседи: + и ~");
        Check(Match("p:empty").Count == 1, "p:empty");
        Check(Match(":root").Count == 1 && Match(":root")[0].StartsWith("topic:"), ":root — корень топика");
        Check(Match("p:first-of-type").Count == 4 && Match("note:last-of-type").Count == 1, ":first-of-type и :last-of-type — по имени среди сестёр");
        Check(Match("*").Count == all.Count, "* — все элементы");
        Check(Match("LI > .missing").Count == 0 && Match("OL > LI").Count == 3, "имена элементов без учёта регистра");

        // Специфичность: id > класс/атрибут/псевдокласс > тип.
        Check(CssSelector.TryParse("li", out _)!.Specificity == 1 && CssSelector.TryParse("ol > li", out _)!.Specificity == 2, "специфичность: типы");
        Check(CssSelector.TryParse("li:first-child", out _)!.Specificity == 101 && CssSelector.TryParse("li[type]", out _)!.Specificity == 101 &&
              CssSelector.TryParse(".a.b", out _)!.Specificity == 200, "специфичность: классы, атрибуты, псевдоклассы");
        Check(CssSelector.TryParse("#x", out _)!.Specificity == 10000, "специфичность: id");
        Check(CssSelector.TryParse("li:not(.a)", out _)!.Specificity == 101, "специфичность: :not() как его аргумент");

        // Псевдоэлементы запоминаются; неподдерживаемое отклоняется с причиной.
        Check(CssSelector.TryParse("li::before", out _)!.PseudoElement == "before" && CssSelector.TryParse("p:after", out _)!.PseudoElement == "after", "::before и :after");
        Check(CssSelector.TryParse("li:hover", out var hoverError) is null && hoverError!.Contains("hover"), "li:hover — не поддерживается, причина названа");
        Check(CssSelector.TryParse("p:nth-child(foo)", out _) is null && CssSelector.TryParse("a[", out _) is null && CssSelector.TryParse("> p", out _) is null,
            "нелепые селекторы отклоняются, а не роняют разбор");
    }

    internal static void CssCascadeDocxTests()
    {
        Section("Каскад CSS в DOCX: потомок, ребёнок, атрибут, :nth-child, :not, ::before/::after");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = CascadeTopic,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>",
            ["custom.css"] = """
                ol > li { color: #C00000; }
                ol > li:first-child { font-weight: bold; }
                li:nth-child(2) { font-style: italic; }
                ul li { font-size: 14pt; }
                note[type="warning"] { background-color: #FFF2CC; }
                div.box > p { font-size: 13pt; }
                p:not(.lead):not(:empty) { color: #0000FF; }
                p.lead::after { content: " ←"; }
                ol > li:last-child::before { content: "№ " attr(type); }
                table { width: 60%; }
                entry { font-size: 12pt; }
                tbody > row:first-child > entry { font-weight: bold; }
                p > keyword { color: #7030A0; }
                .never > .missing { color: red; }
                li:hover { color: green; }
                """
        }, (root, project) =>
        {
            project.SetCustomCssPath("custom.css");
            var docx = Path.Combine(root, "d.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "m.ditamap"), new PublishOptions { Language = "ru" }, docx);
            CheckValidDocx(docx, "каскад CSS по элементам DITA");
            Note("предупреждения: " + string.Join(" | ", result.Warnings));
            using var doc = WordprocessingDocument.Open(docx, false);
            var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);
            Paragraph Para(string text) => doc.MainDocumentPart.Document.Body!.Descendants<Paragraph>().First(p => p.InnerText.Contains(text));
            Style? StyleOf(string text)
            {
                var id = Para(text).ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                return id is not null && styles.TryGetValue(id, out var style) ? style : null;
            }

            string? Color(string text) => StyleOf(text)?.StyleRunProperties?.Color?.Val?.Value;

            Check(Color("Один") == "C00000" && Color("Три") == "C00000", "ol > li: красный у пунктов нумерованного списка");
            Check(StyleOf("Маркер")?.StyleRunProperties?.Color?.Val?.Value is null, "ol > li не действует на ul");
            Check(StyleOf("Один")?.StyleRunProperties?.Bold is not null && StyleOf("Два")?.StyleRunProperties?.Bold is null,
                "ol > li:first-child: жирный только у первого — правила складываются каскадом");
            Check(StyleOf("Два")?.StyleRunProperties?.Italic is not null && StyleOf("Один")?.StyleRunProperties?.Italic is null, "li:nth-child(2): курсив у второго");
            Check(StyleOf("Маркер")?.StyleRunProperties?.FontSize?.Val?.Value == "28" && StyleOf("Один")?.StyleRunProperties?.FontSize?.Val?.Value != "28",
                "ul li: 14 pt только в маркированном списке");
            Check(StyleOf("Внимание")?.StyleParagraphProperties?.Shading?.Fill?.Value == "FFF2CC" && StyleOf("Совет")?.StyleParagraphProperties?.Shading?.Fill?.Value != "FFF2CC",
                "note[type=\"warning\"]: заливка только у предупреждения");
            Check(StyleOf("В боксе")?.StyleRunProperties?.FontSize?.Val?.Value == "26" && StyleOf("Вне бокса")?.StyleRunProperties?.FontSize?.Val?.Value != "26", "div.box > p: размер только у абзаца внутри div");
            Check(Color("Обычный") == "0000FF" && Color("Ведущий") != "0000FF", "p:not(.lead):not(:empty): синий у обычного, не у .lead");
            Check(Para("Ведущий").InnerText.EndsWith(" ←"), "p.lead::after { content }: текст в конце абзаца");
            Check(Para("Три").InnerText.StartsWith("№ ") && !Para("Один").InnerText.StartsWith("№"), "li::before { content: \"№ \" attr(type) } — только у последнего пункта");
            Check(StyleOf("Шапка")?.StyleRunProperties?.FontSize?.Val?.Value == "24" && StyleOf("Тело")?.StyleRunProperties?.FontSize?.Val?.Value == "24",
                "entry { font-size }: правило по имени любого элемента DITA");
            Check(StyleOf("Шапка")?.StyleRunProperties?.Bold is not null && StyleOf("Тело")?.StyleRunProperties?.Bold is null,
                "tbody > row:first-child > entry: цепочка структуры таблицы");
            var tableWidth = doc.MainDocumentPart.Document.Body!.Descendants<Table>().Last().GetFirstChild<TableProperties>()!.GetFirstChild<TableWidth>()!;
            Check(tableWidth.Type?.Value == TableWidthUnitValues.Pct && tableWidth.Width?.Value == "3000", $"table {{ width: 60% }} — 60 % ширины текста ({tableWidth.Width?.Value})");
            var keywordRun = Para("Слово").Descendants<Run>().First(r => r.InnerText == "ключ");
            var keywordStyle = keywordRun.RunProperties?.RunStyle?.Val?.Value;
            Check(keywordStyle is not null && styles[keywordStyle].StyleRunProperties?.Color?.Val?.Value == "7030A0", "p > keyword: символьный стиль у фразового элемента");
            Check(result.Warnings.Any(w => w.Contains(".missing") && w.Contains("не совпали")), "селектор без совпадений — предупреждение");
            Check(!result.Warnings.Any(w => w.Contains("ol > li")), "сработавшие селекторы предупреждений не дают");
        });
    }
}
