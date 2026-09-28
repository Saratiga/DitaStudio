using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Views;

/// <summary>
/// Редактор исходного XML на AvaloniaEdit (порт AvalonEdit из WPF-версии): подсветка, номера
/// строк, поиск по Ctrl+F, автодополнение по каталогу DITA (<see cref="XmlCompletion"/>) —
/// после «&lt;», пробела в теге, кавычки и по Ctrl+Space.
/// </summary>
public sealed class XmlSourceEditor : TextEditor
{
    private CompletionWindow? _completion;

    // TextEditor стилизуется темой AvaloniaEdit по своему типу — наследник берёт ту же тему.
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public XmlSourceEditor()
    {
        FontSize = 13;
        ShowLineNumbers = true;
        WordWrap = false;
        SyntaxHighlighting = HighlightingFor(ActualThemeVariant);
        ActualThemeVariantChanged += (_, _) => SyntaxHighlighting = HighlightingFor(ActualThemeVariant);
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 2;
        Options.HighlightCurrentLine = true;

        this.Bind(FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        this.Bind(BackgroundProperty, this.GetResourceObservable("Surface"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("TextPrimary"));
        this.Bind(LineNumbersForegroundProperty, this.GetResourceObservable("TagBrush"));

        SearchPanel.Install(this);

        TextChanged += (_, _) => TextEdited?.Invoke(this, EventArgs.Empty);
        TextArea.TextEntered += OnTextEntered;
        TextArea.AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static IHighlightingDefinition? _darkXml;

    /// <summary>Светлая тема — стандартная XML-подсветка (как в WPF-версии). Для тёмной —
    /// её копия с цветами тёмной палитры редактора: стандартные синий и бордовый на тёмном
    /// фоне почти не читаются.</summary>
    private static IHighlightingDefinition? HighlightingFor(Avalonia.Styling.ThemeVariant theme)
    {
        if (theme != Avalonia.Styling.ThemeVariant.Dark)
        {
            return HighlightingManager.Instance.GetDefinition("XML");
        }

        if (_darkXml is null)
        {
            using var stream = typeof(TextEditor).Assembly.GetManifestResourceStream("AvaloniaEdit.Highlighting.Resources.XML-Mode.xshd");
            if (stream is null)
            {
                return HighlightingManager.Instance.GetDefinition("XML");
            }

            using var reader = System.Xml.XmlReader.Create(stream);
            var definition = AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance);
            void Color(string name, string hex)
            {
                if (definition.GetNamedColor(name) is { } color)
                {
                    color.Foreground = new SimpleHighlightingBrush(Avalonia.Media.Color.Parse(hex));
                }
            }

            Color("Comment", "#7D8590");
            Color("CData", "#9AA0AA");
            Color("DocType", "#7D8590");
            Color("XmlDeclaration", "#7D8590");
            Color("XmlTag", "#6FA8E8");
            Color("AttributeName", "#C792EA");
            Color("AttributeValue", "#E0A040");
            Color("Entity", "#4CAF6D");
            Color("BrokenEntity", "#E5534B");
            _darkXml = definition;
        }

        return _darkXml;
    }

    /// <summary>Текст изменён пользователем.</summary>
    public event EventHandler? TextEdited;

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

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        // Подсказка появляется там, где она уместна: имя элемента, имя атрибута, значение.
        if (e.Text is "<" or " " or "\"")
        {
            ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        var start = Math.Max(0, CaretOffset - 20000);
        var (items, prefix) = XmlCompletion.Suggest(Document.GetText(start, CaretOffset - start));
        if (items.Count == 0)
        {
            return;
        }

        _completion?.Close();
        _completion = new CompletionWindow(TextArea)
        {
            StartOffset = Math.Max(0, CaretOffset - prefix.Length),
            EndOffset = CaretOffset,
            Width = 340,
            MaxHeight = 260
        };

        foreach (var item in items.Take(120))
        {
            _completion.CompletionList.CompletionData.Add(new CompletionItem(item));
        }

        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private sealed class CompletionItem : ICompletionData
    {
        private readonly XmlSuggestion _suggestion;

        public CompletionItem(XmlSuggestion suggestion)
        {
            _suggestion = suggestion;
        }

        public IImage? Image => null;

        public string Text => _suggestion.Text;

        public object Content => _suggestion.Text;

        public object Description => _suggestion.Description;

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _suggestion.Insert);
            textArea.Caret.Offset -= _suggestion.CaretBack;
        }
    }
}
