using System.Text;
using System.Windows.Input;
using DitaStudio.Core.Schema;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;

namespace DitaStudio.App.Authoring;

/// <summary>
/// Редактор исходного XML на AvalonEdit: подсветка, номера строк, поиск по Ctrl+F.
/// Своё здесь только одно — автодополнение по каталогу DITA.
/// </summary>
public sealed class XmlSourceEditor : TextEditor
{
    private CompletionWindow? _completion;

    public XmlSourceEditor()
    {
        FontFamily = InlineStyles.Mono;
        FontSize = 13;
        ShowLineNumbers = true;
        WordWrap = false;
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("XML");
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 2;
        Options.HighlightCurrentLine = true;

        SearchPanel.Install(this);

        TextChanged += (_, _) => TextEdited?.Invoke(this, EventArgs.Empty);
        TextArea.TextEntered += OnTextEntered;
        TextArea.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Текст изменён пользователем.</summary>
    public event EventHandler? TextEdited;

    public void FocusEditor() => TextArea.Focus();

    public int CurrentLine => TextArea.Caret.Line;

    /// <summary>Ставит курсор на указанную строку (1-based).</summary>
    public void GoToLine(int line)
    {
        if (line < 1 || line > Document.LineCount)
        {
            return;
        }

        TextArea.Caret.Line = line;
        TextArea.Caret.Column = 1;
        ScrollToLine(line);
        TextArea.Focus();
    }

    // --------------------------------------------------------- автодополнение

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        // Подсказка появляется там, где она уместна: имя элемента, имя атрибута, значение.
        if (e.Text is "<" or " " or "\"")
        {
            ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        var context = XmlContext.Analyze(TextBeforeCaret());
        var items = Suggestions(context);
        if (items.Count == 0)
        {
            return;
        }

        _completion?.Close();
        _completion = new CompletionWindow(TextArea)
        {
            StartOffset = Math.Max(0, CaretOffset - context.Prefix.Length),
            EndOffset = CaretOffset,
            Width = 340,
            MaxHeight = 260
        };

        foreach (var item in items.Take(120))
        {
            _completion.CompletionList.CompletionData.Add(item);
        }

        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private static List<CompletionItem> Suggestions(XmlContext context)
    {
        var catalog = DitaCatalog.Default;
        var result = new List<CompletionItem>();

        switch (context.Kind)
        {
            case XmlContextKind.ElementName:
            {
                var parent = context.OpenElements.Count > 0 ? context.OpenElements[^1] : null;
                var def = parent is null ? null : catalog.Get(parent);
                var names = def is null
                    ? catalog.Elements.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList()
                    : def.Automaton.AllowedNames.ToList();

                foreach (var name in names)
                {
                    var child = catalog.Get(name);
                    result.Add(CompletionItem.Element(name, child?.Description ?? string.Empty, child?.IsEmpty ?? false));
                }

                break;
            }

            case XmlContextKind.AttributeName:
            {
                var def = catalog.Get(context.CurrentElement ?? string.Empty);
                if (def is null)
                {
                    break;
                }

                foreach (var attr in def.Attributes.Values.OrderBy(a => a.Name, StringComparer.Ordinal))
                {
                    result.Add(CompletionItem.Attribute(attr.Name, attr.Description));
                }

                break;
            }

            case XmlContextKind.AttributeValue:
            {
                var def = catalog.Get(context.CurrentElement ?? string.Empty);
                if (def is null || context.CurrentAttribute is null ||
                    !def.Attributes.TryGetValue(context.CurrentAttribute, out var attr))
                {
                    break;
                }

                foreach (var value in attr.Values)
                {
                    result.Add(CompletionItem.Value(value));
                }

                break;
            }
        }

        if (context.Prefix.Length > 0)
        {
            result = result
                .Where(i => i.Text.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return result;
    }

    private string TextBeforeCaret()
    {
        var start = Math.Max(0, CaretOffset - 20000);
        return Document.GetText(start, CaretOffset - start);
    }

    /// <summary>Пункт списка автодополнения.</summary>
    private sealed class CompletionItem : ICompletionData
    {
        private readonly string _insert;
        private readonly int _caretBack;

        private CompletionItem(string text, string insert, int caretBack, string description)
        {
            Text = text;
            _insert = insert;
            _caretBack = caretBack;
            Description = description;
        }

        public static CompletionItem Element(string name, string description, bool empty) =>
            empty
                ? new CompletionItem(name, $"{name}/>", 2, description)
                : new CompletionItem(name, $"{name}></{name}>", name.Length + 3, description);

        public static CompletionItem Attribute(string name, string description) =>
            new(name, $"{name}=\"\"", 1, description);

        public static CompletionItem Value(string value) => new(value, value, 0, string.Empty);

        public System.Windows.Media.ImageSource? Image => null;

        public string Text { get; }

        public object Content => Text;

        public object Description { get; }

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _insert);
            textArea.Caret.Offset -= _caretBack;
        }
    }
}

internal enum XmlContextKind
{
    None,
    ElementName,
    AttributeName,
    AttributeValue,
    Content
}

/// <summary>Разбор состояния разметки перед курсором — что уместно подсказать.</summary>
internal sealed class XmlContext
{
    public XmlContextKind Kind { get; private init; } = XmlContextKind.None;

    public string Prefix { get; private init; } = string.Empty;

    public string? CurrentElement { get; private init; }

    public string? CurrentAttribute { get; private init; }

    public List<string> OpenElements { get; private init; } = new();

    public static XmlContext Analyze(string before)
    {
        var open = new List<string>();
        var i = 0;
        var insideTag = false;
        var tagStart = 0;

        while (i < before.Length)
        {
            var ch = before[i];
            if (ch == '<')
            {
                insideTag = true;
                tagStart = i;
            }
            else if (ch == '>' && insideTag)
            {
                insideTag = false;
                ApplyTag(before[tagStart..(i + 1)], open);
            }

            i++;
        }

        if (!insideTag)
        {
            return new XmlContext
            {
                Kind = XmlContextKind.Content,
                OpenElements = open,
                CurrentElement = open.Count > 0 ? open[^1] : null
            };
        }

        var current = before[tagStart..];
        if (current.StartsWith("<!", StringComparison.Ordinal) || current.StartsWith("<?", StringComparison.Ordinal))
        {
            return new XmlContext { Kind = XmlContextKind.None, OpenElements = open };
        }

        var nameEnd = 1;
        while (nameEnd < current.Length && (char.IsLetterOrDigit(current[nameEnd]) || current[nameEnd] is '-' or '_' or ':'))
        {
            nameEnd++;
        }

        if (nameEnd >= current.Length)
        {
            return new XmlContext
            {
                Kind = XmlContextKind.ElementName,
                Prefix = current[1..],
                OpenElements = open,
                CurrentElement = open.Count > 0 ? open[^1] : null
            };
        }

        var elementName = current[1..nameEnd];
        var rest = current[nameEnd..];

        if (rest.Count(c => c == '"') % 2 == 1)
        {
            var lastQuote = rest.LastIndexOf('"');
            var beforeQuote = rest[..lastQuote].TrimEnd();
            var eq = beforeQuote.LastIndexOf('=');
            var attrName = eq > 0 ? beforeQuote[..eq].Trim().Split(' ', '\t', '\n').Last() : null;
            return new XmlContext
            {
                Kind = XmlContextKind.AttributeValue,
                Prefix = rest[(lastQuote + 1)..],
                CurrentElement = elementName,
                CurrentAttribute = attrName,
                OpenElements = open
            };
        }

        var tail = rest.Split(' ', '\t', '\n').Last();
        return new XmlContext
        {
            Kind = XmlContextKind.AttributeName,
            Prefix = tail.Contains('=') ? string.Empty : tail,
            CurrentElement = elementName,
            OpenElements = open
        };
    }

    private static void ApplyTag(string tag, List<string> open)
    {
        if (tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal))
        {
            return;
        }

        if (tag.StartsWith("</", StringComparison.Ordinal))
        {
            if (open.Count > 0)
            {
                open.RemoveAt(open.Count - 1);
            }

            return;
        }

        if (tag.EndsWith("/>", StringComparison.Ordinal))
        {
            return;
        }

        var name = new StringBuilder();
        for (var i = 1; i < tag.Length; i++)
        {
            var ch = tag[i];
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':')
            {
                name.Append(ch);
            }
            else
            {
                break;
            }
        }

        if (name.Length > 0)
        {
            open.Add(name.ToString());
        }
    }
}
