using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.Authoring;

public enum StructureRequest
{
    Split,
    MergeWithPrevious,
    Indent,
    Outdent,
    DeleteForward,
    NextBlock,
    PreviousBlock
}

public sealed class StructureRequestEventArgs : EventArgs
{
    public StructureRequestEventArgs(StructureRequest request, DitaNode node, int caretOffset)
    {
        Request = request;
        Node = node;
        CaretOffset = caretOffset;
    }

    public StructureRequest Request { get; }

    public DitaNode Node { get; }

    public int CaretOffset { get; }

    public bool Handled { get; set; }
}

/// <summary>
/// Редактор смешанного содержимого одного элемента DITA (абзац, заголовок, ячейка, cmd...).
/// Текст правится напрямую, фразовые элементы сохраняются как оформленные прогоны
/// и восстанавливаются в исходную структуру при записи обратно в модель.
/// </summary>
public sealed class InlineEditor : RichTextBox
{
    private sealed class InlineRef
    {
        public InlineRef(IReadOnlyList<DitaNode> chain)
        {
            Chain = chain;
        }

        public IReadOnlyList<DitaNode> Chain { get; }
    }

    // Читаются из текущей темы при каждой перестройке — см. AuthorView.TagBrush и соседей.
    private static Brush ChipBackground => ThemeManager.Brush("EditorChipBackground");
    private static Brush ChipBorder => ThemeManager.Brush("EditorChipBorder");
    private static Brush ChipText => ThemeManager.Brush("EditorChipText");

    private bool _building;
    private bool _dirty;

    public InlineEditor(DitaNode node)
    {
        Node = node;
        AcceptsTab = false;
        AcceptsReturn = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        Margin = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsUndoEnabled = true;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
        SpellCheck.IsEnabled = true;

        Build();

        TextChanged += OnTextChanged;
        LostKeyboardFocus += (_, _) => Flush();
        GotKeyboardFocus += (_, _) => Focused?.Invoke(this, EventArgs.Empty);
        PreviewKeyDown += OnPreviewKeyDown;
        DataObject.AddPastingHandler(this, OnPaste);
    }

    /// <summary>Элемент DITA, который редактируется.</summary>
    public DitaNode Node { get; }

    public event EventHandler<StructureRequestEventArgs>? StructureRequested;

    public event EventHandler? ContentChanged;

    public event EventHandler? Focused;

    // ------------------------------------------------------------- построение

    private void Build()
    {
        _building = true;
        try
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            AppendChildren(Node, paragraph, new List<DitaNode>());
            var doc = new FlowDocument(paragraph)
            {
                PagePadding = new Thickness(0),
                FontFamily = FontFamily,
                FontSize = FontSize,
                LineHeight = FontSize * 1.45
            };
            Document = doc;
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>Перестраивает содержимое из модели (после отмены или правки исходного кода).</summary>
    public void Reload() => Build();

    private void AppendChildren(DitaNode parent, Paragraph paragraph, List<DitaNode> chain)
    {
        foreach (var child in parent.Children)
        {
            switch (child.Kind)
            {
                case NodeKind.Text:
                    paragraph.Inlines.Add(CreateRun(child.Value, chain));
                    break;

                case NodeKind.Comment:
                    paragraph.Inlines.Add(CreateChip(child, chain, "комментарий"));
                    break;

                case NodeKind.Element:
                {
                    var def = DitaCatalog.Default.Get(child.Name);
                    var isInline = def?.IsInline ?? false;
                    var hasText = child.DescendantsAndSelf().Any(n => n.Kind == NodeKind.Text && n.Value.Length > 0);

                    if (isInline && hasText)
                    {
                        var nested = new List<DitaNode>(chain) { child };
                        AppendChildren(child, paragraph, nested);
                    }
                    else
                    {
                        paragraph.Inlines.Add(CreateChip(child, chain, null));
                    }

                    break;
                }
            }
        }
    }

    private Run CreateRun(string text, IReadOnlyList<DitaNode> chain)
    {
        var run = new Run(text);
        if (chain.Count > 0)
        {
            run.Tag = new InlineRef(chain.ToList());
            foreach (var owner in chain)
            {
                InlineStyles.Apply(run, owner.Name);
            }
        }

        return run;
    }

    private InlineUIContainer CreateChip(DitaNode node, IReadOnlyList<DitaNode> chain, string? overrideLabel)
    {
        var label = overrideLabel ?? DescribeChip(node);
        var border = new Border
        {
            Background = ChipBackground,
            BorderBrush = ChipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = ChipText,
                FontFamily = InlineStyles.Mono
            },
            ToolTip = Core.Model.XmlSerializer.ToXml(node)
        };

        return new InlineUIContainer(border)
        {
            Tag = new InlineRef(new List<DitaNode>(chain) { node }),
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static string DescribeChip(DitaNode node)
    {
        switch (node.Name)
        {
            case "image":
            {
                var href = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "?";
                return $"🖼 {System.IO.Path.GetFileName(href)}";
            }

            case "xref":
            case "link":
            {
                var target = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "?";
                return $"🔗 {target}";
            }

            case "fn":
                return "ˣ сноска";

            case "indexterm":
            {
                var parts = new List<string>();
                CollectIndextermText(node, parts);
                return $"☰ {string.Join(" / ", parts)}";
            }

            case "abbreviated-form":
                return $"◆ {node.GetAttribute("keyref")}";

            case "ph" when node.HasAttribute("conref") || node.HasAttribute("conkeyref"):
                return $"⇗ {node.GetAttribute("conref") ?? node.GetAttribute("conkeyref")}";
        }

        if (node.HasAttribute("conref") || node.HasAttribute("conkeyref"))
        {
            return $"⇗ {node.Name}";
        }

        var text = node.InnerText.Trim();
        return text.Length > 0 ? $"<{node.Name}> {Trim(text)}" : $"<{node.Name}/>";
    }

    private static string Trim(string value) => value.Length <= 24 ? value : value[..24] + "…";

    /// <summary>Термин indexterm и вложенные подпункты — отдельными кусками, а не InnerText одной
    /// строкой (иначе «Установка» и вложенный подпункт «первый запуск» слипаются в одно слово).</summary>
    private static void CollectIndextermText(DitaNode node, List<string> parts)
    {
        var direct = string.Concat(node.Children.Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();
        if (direct.Length > 0)
        {
            parts.Add(direct);
        }

        foreach (var child in node.ElementChildren().Where(c => c.Name == "indexterm"))
        {
            CollectIndextermText(child, parts);
        }
    }

    // --------------------------------------------------------------- запись

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_building)
        {
            return;
        }

        _dirty = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Записывает изменения обратно в модель, если они есть.</summary>
    public void Flush()
    {
        if (!_dirty || _building)
        {
            return;
        }

        _dirty = false;
        WriteBack();
    }

    public void WriteBack()
    {
        if (Document.Blocks.FirstBlock is not Paragraph paragraph)
        {
            return;
        }

        var children = new List<DitaNode>();
        var originalPath = new List<DitaNode>();
        var builtPath = new List<DitaNode>();

        void Append(DitaNode child)
        {
            if (builtPath.Count > 0)
            {
                builtPath[^1].Add(child);
            }
            else
            {
                children.Add(child);
            }
        }

        void Descend(IReadOnlyList<DitaNode> chain, int skipLast)
        {
            var target = chain.Count - skipLast;

            var common = 0;
            while (common < originalPath.Count && common < target &&
                   ReferenceEquals(originalPath[common], chain[common]))
            {
                common++;
            }

            originalPath.RemoveRange(common, originalPath.Count - common);
            builtPath.RemoveRange(common, builtPath.Count - common);

            for (var i = common; i < target; i++)
            {
                var source = chain[i];
                var clone = DitaNode.Element(source.Name);
                foreach (var attr in source.Attributes)
                {
                    clone.SetAttribute(attr.Name, attr.Value);
                }

                Append(clone);
                originalPath.Add(source);
                builtPath.Add(clone);
            }
        }

        foreach (var inline in Flatten(paragraph.Inlines))
        {
            switch (inline)
            {
                case Run run:
                {
                    if (run.Text.Length == 0)
                    {
                        continue;
                    }

                    var chain = (run.Tag as InlineRef)?.Chain ?? Array.Empty<DitaNode>();
                    Descend(chain, 0);
                    Append(DitaNode.Text(run.Text));
                    break;
                }

                case InlineUIContainer container:
                {
                    var chain = (container.Tag as InlineRef)?.Chain;
                    if (chain is null || chain.Count == 0)
                    {
                        continue;
                    }

                    Descend(chain, 1);
                    Append(chain[^1].CloneDeep());
                    break;
                }

                case LineBreak:
                {
                    Descend(Array.Empty<DitaNode>(), 0);
                    Append(DitaNode.Text(" "));
                    break;
                }
            }
        }

        foreach (var existing in Node.Children.ToList())
        {
            Node.Remove(existing);
        }

        foreach (var child in children)
        {
            Node.Add(child);
        }
    }

    private static IEnumerable<Inline> Flatten(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Span span:
                    foreach (var nested in Flatten(span.Inlines))
                    {
                        yield return nested;
                    }

                    break;
                default:
                    yield return inline;
                    break;
            }
        }
    }

    // ------------------------------------------------------------ клавиатура

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.B:
                    WrapSelection("b");
                    e.Handled = true;
                    return;
                case Key.I:
                    WrapSelection("i");
                    e.Handled = true;
                    return;
                case Key.U when !shift:
                    WrapSelection("u");
                    e.Handled = true;
                    return;
                case Key.U when shift:
                    WrapSelection("uicontrol");
                    e.Handled = true;
                    return;
                case Key.OemTilde:
                    WrapSelection("codeph");
                    e.Handled = true;
                    return;
                case Key.Space when shift:
                    ClearFormatting();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Enter:
                Flush();
                e.Handled = Raise(StructureRequest.Split, CaretOffset);
                return;

            case Key.Back when CaretOffset == 0 && Selection.IsEmpty:
                Flush();
                e.Handled = Raise(StructureRequest.MergeWithPrevious, 0);
                return;

            case Key.Delete when Selection.IsEmpty && CaretOffset >= PlainTextLength:
                Flush();
                e.Handled = Raise(StructureRequest.DeleteForward, CaretOffset);
                return;

            case Key.Tab:
                Flush();
                e.Handled = Raise(shift ? StructureRequest.Outdent : StructureRequest.Indent, CaretOffset);
                return;

            case Key.Down when CaretOffset >= PlainTextLength:
                Flush();
                e.Handled = Raise(StructureRequest.NextBlock, CaretOffset);
                return;

            case Key.Up when CaretOffset == 0:
                Flush();
                e.Handled = Raise(StructureRequest.PreviousBlock, CaretOffset);
                return;
        }
    }

    private bool Raise(StructureRequest request, int caretOffset)
    {
        var args = new StructureRequestEventArgs(request, Node, caretOffset);
        StructureRequested?.Invoke(this, args);
        return args.Handled;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // Вставляем только простой текст, чтобы в документ не попадала чужая разметка.
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = (string)e.DataObject.GetData(DataFormats.UnicodeText)!;
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text.Replace("\r\n", " ").Replace('\n', ' '));
            e.DataObject = data;
            e.FormatToApply = DataFormats.UnicodeText;
        }
        else
        {
            e.CancelCommand();
        }
    }

    // ------------------------------------------------------------- операции

    public int PlainTextLength => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n').Length;

    public int CaretOffset => new TextRange(Document.ContentStart, CaretPosition).Text.Length;

    public void PlaceCaretAt(int offset)
    {
        var pointer = Document.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward) ?? Document.ContentStart;
        var current = pointer;
        var remaining = offset;
        while (remaining > 0)
        {
            var next = current.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is null)
            {
                break;
            }

            current = next;
            remaining--;
        }

        CaretPosition = current;
    }

    public void PlaceCaretAtEnd() => CaretPosition = Document.ContentEnd;

    /// <summary>Оборачивает выделение во фразовый элемент DITA.</summary>
    public void WrapSelection(string elementName)
    {
        if (Selection.IsEmpty)
        {
            return;
        }

        var def = DitaCatalog.Default.Get(elementName);
        if (def is null || !def.IsInline)
        {
            return;
        }

        var text = Selection.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var owner = DitaNode.Element(elementName);
        var run = CreateRun(text, new List<DitaNode> { owner });

        Selection.Text = string.Empty;
        var insertion = Selection.Start;

        var paragraph = insertion.Paragraph;
        if (paragraph is null)
        {
            return;
        }

        var target = insertion.GetAdjacentElement(LogicalDirection.Backward) as Inline;
        if (target is not null)
        {
            paragraph.Inlines.InsertAfter(target, run);
        }
        else
        {
            paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, run);
        }

        _dirty = true;
        Flush();
        Reload();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Снимает фразовое оформление с выделения.</summary>
    public void ClearFormatting()
    {
        if (Selection.IsEmpty)
        {
            return;
        }

        var text = Selection.Text;
        Selection.Text = string.Empty;
        var insertion = Selection.Start;
        var paragraph = insertion.Paragraph;
        if (paragraph is null)
        {
            return;
        }

        var run = new Run(text);
        var target = insertion.GetAdjacentElement(LogicalDirection.Backward) as Inline;
        if (target is not null)
        {
            paragraph.Inlines.InsertAfter(target, run);
        }
        else
        {
            paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, run);
        }

        _dirty = true;
        Flush();
        Reload();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Вставляет готовый элемент (ссылку, изображение, сноску) в позицию курсора.</summary>
    public void InsertInlineNode(DitaNode node)
    {
        var paragraph = CaretPosition.Paragraph ?? Document.Blocks.FirstBlock as Paragraph;
        if (paragraph is null)
        {
            return;
        }

        var chip = CreateChip(node, Array.Empty<DitaNode>(), null);
        var target = CaretPosition.GetAdjacentElement(LogicalDirection.Backward) as Inline;
        if (target is not null)
        {
            paragraph.Inlines.InsertAfter(target, chip);
        }
        else if (paragraph.Inlines.FirstInline is not null)
        {
            paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, chip);
        }
        else
        {
            paragraph.Inlines.Add(chip);
        }

        _dirty = true;
        Flush();
        Reload();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
