using System.Globalization;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

// Предметный указатель в конце документа: поле INDEX, заранее заполненное терминами.
public sealed partial class DocxPublisher
{
    private sealed class IndexNode
    {
        public string? SortAs { get; set; }

        public List<string> See { get; } = new();

        public List<string> SeeAlso { get; } = new();

        public Dictionary<string, IndexNode> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Страница «Указатель»: заголовок и поле INDEX. Поля XE в тексте указывают, откуда Word возьмёт номера страниц;
    /// пока поля не обновлены (F9, Word делает это при открытии), в поле стоят сами термины без номеров — так указатель
    /// виден и в просмотрщиках без пересчёта полей.
    /// </summary>
    private static void WriteIndex(W.Body body, Labels labels, IReadOnlyList<IndexTermEntry> terms)
    {
        if (terms.Count == 0)
        {
            return;
        }

        var root = new IndexNode();
        foreach (var term in terms)
        {
            var node = root;
            foreach (var segment in term.Path)
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new IndexNode();
                    node.Children[segment] = child;
                }

                node = child;
            }

            node.SortAs ??= term.SortAs;
            node.See.AddRange(term.See.Where(t => !node.See.Contains(t)));
            node.SeeAlso.AddRange(term.SeeAlso.Where(t => !node.SeeAlso.Contains(t)));
        }

        var lines = new List<(int Depth, string Text)>();
        var culture = CultureInfo.GetCultureInfo(labels == Labels.Russian ? "ru-RU" : "en-US");
        Flatten(root, 0, StringComparer.Create(culture, ignoreCase: true), labels, lines);

        body.Append(PageBreakParagraph());
        body.Append(StyledParagraph(DocxStyleCatalog.TocHeading, labels.Index));

        const string instruction = " INDEX \\h \"A\" \\c \"1\" ";
        for (var i = 0; i < lines.Count; i++)
        {
            var (depth, text) = lines[i];
            var paragraph = new W.Paragraph(new W.ParagraphProperties(
                new W.Indentation { Left = (depth * 360).ToString(CultureInfo.InvariantCulture) }));
            if (i == 0)
            {
                paragraph.Append(
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin, Dirty = true }),
                    new W.Run(new W.FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }));
            }

            paragraph.Append(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            if (i == lines.Count - 1)
            {
                paragraph.Append(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
            }

            body.Append(paragraph);
        }
    }

    private static void Flatten(IndexNode node, int depth, StringComparer comparer, Labels labels, List<(int, string)> lines)
    {
        foreach (var (term, child) in node.Children.OrderBy(kv => kv.Value.SortAs ?? kv.Key, comparer).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var text = term;
            if (child.See.Count > 0)
            {
                text += ", " + labels.IndexSee + " " + string.Join(", ", child.See);
            }

            if (child.SeeAlso.Count > 0)
            {
                text += ", " + labels.IndexSeeAlso + " " + string.Join(", ", child.SeeAlso);
            }

            lines.Add((depth, text));
            Flatten(child, depth + 1, comparer, labels, lines);
        }
    }
}
