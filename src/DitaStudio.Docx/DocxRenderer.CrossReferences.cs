using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// DOCX: перекрёстные ссылки на рисунки, таблицы, пункты, сноски и разделы. Цели при отрисовке записывают номер и
// подпись (CrossReferenceLabels, общий с HTML) и получают скрытую закладку «_Ref…»; пустая ссылка выводится меткой,
// которую FinishCrossReferences заменяет текстом после обхода всего издания. Ссылка на цель без закладки
// (цель не вошла в издание) остаётся обычным текстом.
public sealed partial class DocxRenderer
{
    private CrossReferenceLabels? _xrefs;
    private HeadingNumbering? _numberMirror;
    private string? _lastHeadingNumber;

    /// <summary>Номера и подписи целей перекрёстных ссылок, записанные при отрисовке.</summary>
    public CrossReferenceLabels CrossReferences => _xrefs ??= _options.CrossReferences ?? new CrossReferenceLabels();

    // Номера заголовков и нумерованных абзацев Word считает сам (список заголовков); для текста ссылки они считаются
    // тем же правилом, что и в HTML.
    private HeadingNumbering NumberMirror => _numberMirror ??= new HeadingNumbering(_options.HeadingNumId is not null, _options.NumberingDepth);

    private void TrackHeadingNumber(int level, bool unnumbered) => _lastHeadingNumber = NumberMirror.Heading(level, unnumbered);

    /// <summary>Цель ссылки с id: записывает номер и ставит закладку в начало её абзаца.</summary>
    private void RecordTarget(W.Paragraph? paragraph, DitaNode node, XrefTargetKind kind, string? label, string? number, string text, string? separator = null)
    {
        var id = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        CrossReferences.Record(_document.FilePath, id, kind, label, number, text, separator);
        if (paragraph is not null)
        {
            PrependBookmark(paragraph, CrossReferenceLabels.BookmarkName(_document.FilePath, id!));
        }
    }

    private void RecordCaption(W.Paragraph? caption, DitaNode node, XrefTargetKind kind, string word, int number, DitaNode title)
    {
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RecordTarget(caption, node, kind, _options.NumberFiguresAndTables ? word + " " + text : null, text, title.InnerText,
            CaptionRules.Separator(_options.CaptionSeparator));
    }

    /// <summary>Пункт нумерованного списка или шаг: номер по порядку («3»), у маркированного — без номера.</summary>
    private void RecordListItem(W.Paragraph? first, DitaNode node)
    {
        if (string.IsNullOrWhiteSpace(node.GetAttribute("id")) || node.Parent is not { } list)
        {
            return;
        }

        var number = list.Name is "ol" or "steps" or "substeps"
            ? (list.ElementChildren().Where(e => e.Name == node.Name).TakeWhile(e => !ReferenceEquals(e, node)).Count() + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var text = node.Name is "step" or "substep" ? node.FirstElement("cmd")?.InnerText ?? node.InnerText : node.InnerText;
        RecordTarget(first, node, XrefTargetKind.NumberedItem, null, number, text);
    }

    /// <summary>
    /// После обхода издания: метки пустых ссылок — текстом по номерам целей (в тексте и в сносках); ссылки на закладки
    /// «_Ref…», которых в документе нет (цель исключена условием или не входит в карту), — обычным текстом.
    /// </summary>
    public void FinishCrossReferences()
    {
        var roots = new List<OpenXmlElement>();
        if (_mainPart.Document?.Body is { } body)
        {
            roots.Add(body);
        }

        if (_mainPart.FootnotesPart?.Footnotes is { } footnotes)
        {
            roots.Add(footnotes);
        }

        var bookmarks = roots.SelectMany(r => r.Descendants<W.BookmarkStart>()).Select(b => b.Name?.Value).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            foreach (var text in root.Descendants<W.Text>().Where(t => CrossReferenceLabels.HasTokens(t.Text)))
            {
                text.Text = CrossReferences.Resolve(text.Text);
            }

            foreach (var link in root.Descendants<W.Hyperlink>()
                         .Where(h => h.Anchor?.Value is { } anchor && anchor.StartsWith("_Ref", StringComparison.Ordinal) && !bookmarks.Contains(anchor))
                         .ToList())
            {
                foreach (var child in link.ChildElements.ToList())
                {
                    child.Remove();
                    link.InsertBeforeSelf(child);
                }

                link.Remove();
            }
        }
    }
}
