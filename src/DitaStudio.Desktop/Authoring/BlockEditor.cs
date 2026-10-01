using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

public enum StructureRequest
{
    Split,
    MergeWithPrevious,
    Indent,
    Outdent,
    DeleteForward,
    NextBlock,
    PreviousBlock,

    /// <summary>Ctrl+Enter: меню допустимых элементов после текущего блока, в том числе после внешних (таблицы, div).</summary>
    InsertAfterBlock
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
/// Редактор смешанного содержимого одного элемента DITA (абзац, заголовок, ячейка, cmd…) —
/// замена WPF InlineEditor (RichTextBox) на AvaloniaEdit. Текст блока — одна строка документа
/// редактора; фразовые элементы живут в <see cref="InlineContent"/> (цепочка на символ) и
/// рисуются раскраской, плашки — встроенными элементами на месте символа-заместителя.
/// Клавиши и структурные запросы — как в WPF-версии.
/// </summary>
public sealed class BlockEditor : TextEditor
{
    private readonly InlineContent _content;
    private bool _syncing;
    private bool _dirty;
    private List<Misspelling>? _misspellings;

    // TextEditor стилизуется темой AvaloniaEdit по своему типу — наследник берёт ту же тему.
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public BlockEditor(InlineContent content)
    {
        _content = content;

        WordWrap = true;
        ShowLineNumbers = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        FontSize = 14.5;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = false;
        Options.ShowBoxForControlCharacters = false;
        Options.EnableRectangularSelection = false;
        Options.AllowScrollBelowDocument = false;
        Options.CutCopyWholeLine = false;
        Options.EnableImeSupport = true;

        this.Bind(FontFamilyProperty, this.GetResourceObservable("UiFont"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("TextPrimary"));

        Document = new TextDocument(_content.Text);
        Document.UndoStack.ClearAll();
        Document.Changed += OnDocumentChanged;

        TextArea.TextView.LineTransformers.Add(new InlineColorizer(this));
        TextArea.TextView.ElementGenerators.Add(new ChipGenerator(this));
        TextArea.TextView.BackgroundRenderers.Add(new SpellingRenderer(this));
        TextArea.AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) =>
        {
            SpellChecker.Default.Changed += OnSpellingChanged;
            _ = SpellChecker.Default.EnsureLoadedAsync();
        };
        DetachedFromVisualTree += (_, _) => SpellChecker.Default.Changed -= OnSpellingChanged;
        TextArea.AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        TextArea.GotFocus += (_, _) =>
        {
            GotFocusRecorded?.Invoke(this, EventArgs.Empty);
            Focused?.Invoke(this, EventArgs.Empty);
        };
        TextArea.LostFocus += (_, _) =>
        {
            var edited = _dirty || _commitPending;
            _commitPending = false;
            Flush();
            if (edited)
            {
                EditCommitted?.Invoke(this, EventArgs.Empty);
            }
        };
        // Курсор, поставленный мышью, полосу прокрутки не двигает: место, куда щёлкнули, и так на экране, а прыжок из-за отступа
        // от края (особенно в таблице, где блоки идут вплотную) мешает ставить курсор. Флаг держится, пока кнопка нажата.
        TextArea.AddHandler(PointerPressedEvent, (_, _) => _pointerPlacesCaret = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        void EndPointerCaret() => Dispatcher.UIThread.Post(() => _pointerPlacesCaret = false, DispatcherPriority.Background);
        TextArea.AddHandler(PointerReleasedEvent, (_, _) => EndPointerCaret(), RoutingStrategies.Tunnel, handledEventsToo: true);
        TextArea.PointerCaptureLost += (_, _) => EndPointerCaret();
        // При получении фокуса TextArea просит внешнюю область показать её целиком (весь блок, иногда выше экрана) — щелчок мышью
        // из-за этого прокручивал полосу. Когда курсор ставят мышью, запрос гасится; с клавиатуры (Tab, стрелки) работает как раньше.
        TextArea.AddHandler(RequestBringIntoViewEvent, (_, e) =>
        {
            if (_pointerPlacesCaret)
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);

        // Внешняя полоса прокрутки следует за курсором (клавиши, правка, переход в блок): длинный абзац и блоки далеко за экраном.
        TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (TextArea.IsFocused && !_pointerPlacesCaret)
            {
                // После раскладки: сразу после правки текста размеры строк ещё прежние.
                Dispatcher.UIThread.Post(ScrollCaretIntoView, DispatcherPriority.Loaded);
            }
        };
        ActualThemeVariantChanged += (_, _) => TextArea.TextView.Redraw();
    }

    private bool _pointerPlacesCaret;

    /// <summary>Элемент DITA, который редактируется (участок его содержимого — <see cref="Content"/>).</summary>
    public DitaNode Node => _content.Node;

    /// <summary>Плоское содержимое блока или участка между вложенными блоками.</summary>
    public InlineContent Content => _content;

    public event EventHandler<StructureRequestEventArgs>? StructureRequested;

    /// <summary>Текст изменён пользователем (для отметки «не сохранено»).</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Меню по правой кнопке строится: подписчик добавляет свои пункты (например, «Вставить»).</summary>
    public event Action<BlockEditor, List<Control>>? ContextMenuBuilding;

    public event EventHandler? Focused;

    public int PlainTextLength => Document.TextLength;

    // ------------------------------------------------------------ синхронизация с моделью

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _content.Replace(e.Offset, e.RemovalLength, e.InsertedText.Text);
        _misspellings = null;
        _dirty = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // Правка уже записана в модель для предпросмотра (FlushForPreview), но «закончена» она будет только с уходом фокуса.
    private bool _commitPending;

    /// <summary>Записывает правки в модель, не считая их законченными: уход фокуса всё равно сообщит <see cref="EditCommitted"/>
    /// (вопрос о смене имени файла при правке заголовка не пропадает из-за «живого» предпросмотра).</summary>
    public void FlushForPreview()
    {
        if (_dirty)
        {
            _commitPending = true;
            Flush();
        }
    }

    /// <summary>Записывает правки в модель, если они есть.</summary>
    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _content.WriteBack();
        Written?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Фокус пришёл в редактор (до остальных обработчиков) — здесь запоминается исходный текст.</summary>
    public event EventHandler? GotFocusRecorded;

    /// <summary>Фокус ушёл из редактора, в котором текст был изменён, — правка записана в модель.</summary>
    public event EventHandler? EditCommitted;

    /// <summary>Содержимое записано в модель (например, по уходу фокуса).</summary>
    public event EventHandler? Written;

    /// <summary>Перечитывает содержимое из модели (после отмены или правки исходного кода).</summary>
    public void Reload()
    {
        _dirty = false;
        _content.Load();
        _misspellings = null;
        SetDocumentText(_content.Text);
    }

    private void SetDocumentText(string text)
    {
        _syncing = true;
        try
        {
            var caret = CaretOffset;
            Document.Text = text;
            Document.UndoStack.ClearAll();
            CaretOffset = Math.Min(caret, Document.TextLength);
        }
        finally
        {
            _syncing = false;
        }

        TextArea.TextView.Redraw();
    }

    // ------------------------------------------------------------ операции

    public void PlaceCaretAt(int offset)
    {
        CaretOffset = Math.Clamp(offset, 0, Document.TextLength);
        TextArea.Caret.BringCaretToView();
    }

    public void PlaceCaretAtEnd() => PlaceCaretAt(Document.TextLength);

    /// <summary>Прокручивает окружающую область так, чтобы строка с курсором была видна (с запасом сверху и снизу).</summary>
    public void ScrollCaretIntoView()
    {
        var view = TextArea.TextView;
        if (view.Bounds.Height <= 0 || this.FindAncestorOfType<ScrollViewer>() is not { } outer)
        {
            return;
        }

        // Своя прокрутка вместо BringIntoView: внутренняя область AvaloniaEdit запрос не пропускает наружу.
        var caret = TextArea.Caret.CalculateCaretRectangle();
        if (view.TranslatePoint(new Point(0, caret.Y - view.ScrollOffset.Y), outer) is not { } top)
        {
            return;
        }

        const double margin = 28;
        var bottom = top.Y + caret.Height;
        var offset = outer.Offset;
        if (top.Y < margin)
        {
            outer.Offset = new Vector(offset.X, Math.Max(0, offset.Y + top.Y - margin));
        }
        else if (bottom > outer.Viewport.Height - margin)
        {
            outer.Offset = new Vector(offset.X, offset.Y + bottom - (outer.Viewport.Height - margin));
        }
    }

    public void FocusEditor(int? caretOffset = null)
    {
        TextArea.Focus();
        if (caretOffset is { } offset)
        {
            PlaceCaretAt(offset);
        }
    }

    /// <summary>Оборачивает выделение во фразовый элемент DITA. False — нечего оборачивать.</summary>
    public bool WrapSelection(string elementName)
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty || DitaCatalog.Default.Get(elementName) is not { IsInline: true })
        {
            return false;
        }

        var segment = selection.SurroundingSegment;
        if (!_content.Wrap(segment.Offset, segment.Length, elementName))
        {
            return false;
        }

        CommitFormatting();
        return true;
    }

    /// <summary>Снимает фразовое оформление с выделения.</summary>
    public bool ClearFormatting()
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty)
        {
            return false;
        }

        var segment = selection.SurroundingSegment;
        _content.ClearFormatting(segment.Offset, segment.Length);
        CommitFormatting();
        return true;
    }

    /// <summary>Вставляет готовый элемент (ссылку, изображение, сноску) плашкой в позицию курсора.</summary>
    public void InsertInlineNode(DitaNode node)
    {
        var offset = TextArea.Selection.IsEmpty ? CaretOffset : TextArea.Selection.SurroundingSegment.Offset;
        _content.InsertChip(offset, node);
        _syncing = true;
        try
        {
            Document.Insert(offset, InlineContent.ChipChar.ToString());
        }
        finally
        {
            _syncing = false;
        }

        CaretOffset = offset + 1;
        CommitFormatting();
    }

    /// <summary>
    /// Вставляет у курсора фразовый элемент с текстом (сноска, термин): выделение оборачивается,
    /// иначе внутрь ставится выделенная заготовка — набор сразу её заменяет.
    /// </summary>
    public void InsertInlineElement(DitaNode element, string placeholder)
    {
        if (!TextArea.Selection.IsEmpty && WrapSelection(element.Name))
        {
            return;
        }

        var offset = CaretOffset;
        _content.InsertElementText(offset, element, placeholder);
        _syncing = true;
        try
        {
            Document.Insert(offset, placeholder);
        }
        finally
        {
            _syncing = false;
        }

        Select(offset, placeholder.Length);
        CommitFormatting();
    }

    /// <summary>
    /// Класс оформления (размер, цвет) для выделения: уже оформленный ровно этим участком ph
    /// получает новый класс, иначе выделение оборачивается в ph с классом. Без выделения у курсора
    /// появляется выделенная заготовка с классом — дальше текст печатается уже так. null — снять
    /// класс группы с выделения (или с фрагмента у курсора).
    /// </summary>
    public void ApplyInlineClass(string prefix, string? token)
    {
        var selection = TextArea.Selection;
        if (token is null)
        {
            var (start, length) = selection.IsEmpty
                ? (Math.Max(0, CaretOffset - 1), 1)
                : (selection.SurroundingSegment.Offset, selection.SurroundingSegment.Length);
            if (_content.ClearClass(start, length, prefix))
            {
                CommitFormatting();
            }

            return;
        }

        if (selection.IsEmpty)
        {
            var ph = DitaNode.Element("ph");
            ph.SetAttribute("outputclass", token);
            InsertInlineElement(ph, "текст");
            return;
        }

        var segment = selection.SurroundingSegment;
        if (_content.ExactWrapper(segment.Offset, segment.Length, "ph") is { } existing)
        {
            TextFormatting.SetToken(existing, prefix, token);
        }
        else if (_content.WrapNode(segment.Offset, segment.Length, "ph") is { } wrapper)
        {
            wrapper.SetAttribute("outputclass", token);
        }

        CommitFormatting();
    }

    private void CommitFormatting()
    {
        _misspellings = null;
        _dirty = true;
        Flush();
        TextArea.TextView.Redraw();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ клавиатура

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl)
        {
            var wrap = e.Key switch
            {
                Key.B => "b",
                Key.I => "i",
                Key.U when !shift => "u",
                Key.U => "uicontrol",
                Key.OemTilde => "codeph",
                _ => null
            };

            if (wrap is not null)
            {
                e.Handled = true;
                RaiseWrap(wrap);
                return;
            }

            if (e.Key == Key.Space && shift)
            {
                e.Handled = true;
                ClearFormatting();
                return;
            }

            if (e.Key == Key.V)
            {
                e.Handled = true;
                _ = PastePlainTextAsync();
                return;
            }
        }

        if (e.Key == Key.Insert && shift)
        {
            e.Handled = true;
            _ = PastePlainTextAsync();
            return;
        }

        var selectionEmpty = TextArea.Selection.IsEmpty;
        switch (e.Key)
        {
            case Key.Enter when ctrl:
                e.Handled = true;
                Flush();
                Raise(StructureRequest.InsertAfterBlock, CaretOffset);
                return;

            case Key.Enter:
                // Перевода строки внутри блока не бывает — Enter всегда структурная операция.
                e.Handled = true;
                Flush();
                Raise(StructureRequest.Split, CaretOffset);
                return;

            case Key.Back when CaretOffset == 0 && selectionEmpty && _content.IsFirstSegment:
                Flush();
                e.Handled = Raise(StructureRequest.MergeWithPrevious, 0);
                return;

            case Key.Delete when selectionEmpty && CaretOffset >= PlainTextLength && _content.IsLastSegment:
                Flush();
                e.Handled = Raise(StructureRequest.DeleteForward, CaretOffset);
                return;

            case Key.Tab:
                e.Handled = true;
                Flush();
                Raise(shift ? StructureRequest.Outdent : StructureRequest.Indent, CaretOffset);
                return;

            case Key.Down when !shift && CaretOnEdgeRow(last: true):
                Flush();
                e.Handled = Raise(StructureRequest.NextBlock, CaretOffset);
                return;

            case Key.Up when !shift && CaretOnEdgeRow(last: false):
                Flush();
                e.Handled = Raise(StructureRequest.PreviousBlock, CaretOffset);
                return;
        }
    }

    /// <summary>Просьба обернуть выделение — через AuthorView, чтобы до правки сохранить точку отмены.</summary>
    public event EventHandler<string>? WrapRequested;

    private void RaiseWrap(string element)
    {
        if (WrapRequested is null)
        {
            WrapSelection(element);
        }
        else
        {
            WrapRequested(this, element);
        }
    }

    private bool Raise(StructureRequest request, int caretOffset)
    {
        var args = new StructureRequestEventArgs(request, Node, caretOffset);
        StructureRequested?.Invoke(this, args);
        return args.Handled;
    }

    /// <summary>Курсор в первой (или последней) визуальной строке блока с учётом переноса.</summary>
    private bool CaretOnEdgeRow(bool last)
    {
        try
        {
            var caret = TextArea.Caret;
            var visualLine = TextArea.TextView.GetOrConstructVisualLine(Document.GetLineByNumber(caret.Line));
            var row = visualLine.GetTextLine(caret.VisualColumn, caret.IsInVirtualSpace);
            return ReferenceEquals(row, last ? visualLine.TextLines[^1] : visualLine.TextLines[0]);
        }
        catch (InvalidOperationException)
        {
            // Строка ещё не размечена (редактор не показан) — судим по смещению.
            return last ? CaretOffset >= PlainTextLength : CaretOffset == 0;
        }
    }

    /// <summary>Вставка — только простой текст одной строкой, чтобы в документ не попадала чужая разметка.</summary>
    private async Task PastePlainTextAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ')
            .Replace(InlineContent.ChipChar.ToString(), string.Empty);
        TextArea.Selection.ReplaceSelectionWithText(text);
    }

    // ------------------------------------------------------------ орфография

    // Код, имена файлов, команды и параметры — не слова языка, их не проверяем.
    private static readonly HashSet<string> NoSpellElements = new(StringComparer.Ordinal)
    {
        "codeph", "codeblock", "tt", "synph", "filepath", "systemoutput", "msgph", "msgnum", "parmname",
        "apiname", "option", "cmdname", "markupname", "xmlatt", "xmlelement", "xmlnsname", "xmlpi",
        "numcharref", "parameterentity", "textentity", "userinput", "varname", "keyword", "xref", "tm"
    };

    /// <summary>Слова с ошибками в тексте блока (пусто, пока словари не загружены).</summary>
    public IReadOnlyList<Misspelling> Misspellings
    {
        get
        {
            if (_misspellings is null)
            {
                _misspellings = _content.Length == Document.TextLength
                    ? SpellChecker.Default.Find(_content.Text, SkipSpelling)
                    : new List<Misspelling>();
            }

            return _misspellings;
        }
    }

    private bool SkipSpelling(int offset)
    {
        var chain = _content.ChainAt(offset);
        return chain.IsChip || chain.Nodes.Any(n => NoSpellElements.Contains(n.Name));
    }

    private void OnSpellingChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _misspellings = null;
            TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        });

    /// <summary>Волнистое подчёркивание слов с ошибками — как проверка орфографии WPF.</summary>
    private sealed class SpellingRenderer : IBackgroundRenderer
    {
        private readonly BlockEditor _editor;

        public SpellingRenderer(BlockEditor editor)
        {
            _editor = editor;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || _editor.Misspellings.Count == 0)
            {
                return;
            }

            var pen = new Pen(_editor.ThemeBrush("Danger") ?? Brushes.Red, 1);
            foreach (var word in _editor.Misspellings)
            {
                var segment = new TextSegment { StartOffset = word.Start, Length = word.Length };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        var y = rect.Bottom - 1.5;
                        context.BeginFigure(new Point(rect.Left, y), false);
                        var up = true;
                        for (var x = rect.Left + 2; x <= rect.Right; x += 2)
                        {
                            context.LineTo(new Point(x, up ? y - 1.5 : y));
                            up = !up;
                        }

                        context.EndFigure(false);
                    }

                    drawingContext.DrawGeometry(null, pen, geometry);
                }
            }
        }
    }

    // ------------------------------------------------------------ контекстное меню

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        var position = GetPositionFromPoint(e.GetPosition(this));
        int? offset = position is { } p ? Document.GetOffset(p.Location) : null;
        var selection = TextArea.Selection.SurroundingSegment;
        if (offset is { } o && (TextArea.Selection.IsEmpty || o < selection.Offset || o > selection.EndOffset))
        {
            TextArea.ClearSelection();
            CaretOffset = o;
        }

        TextArea.Focus();
        BuildContextMenu(offset).Open(this);
        e.Handled = true;
    }

    /// <summary>Меню правки: варианты исправления слова под курсором, «Пропустить все», буфер обмена.</summary>
    public ContextMenu BuildContextMenu(int? offset)
    {
        var items = new List<Control>();
        if (offset is { } o && Misspellings.FirstOrDefault(m => o >= m.Start && o <= m.Start + m.Length) is { Length: > 0 } word)
        {
            var text = Document.GetText(word.Start, word.Length);
            var suggestions = SpellChecker.Default.Suggest(text);
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = suggestion, FontWeight = FontWeight.SemiBold };
                item.Click += (_, _) =>
                {
                    Document.Replace(word.Start, word.Length, suggestion);
                    CaretOffset = word.Start + suggestion.Length;
                };
                items.Add(item);
            }

            if (suggestions.Count == 0)
            {
                items.Add(new MenuItem { Header = "(нет вариантов)", IsEnabled = false });
            }

            var ignore = new MenuItem { Header = "Пропустить все" };
            ignore.Click += (_, _) => SpellChecker.Default.Ignore(text);
            items.Add(ignore);
            items.Add(new Separator());
        }

        var hasSelection = !TextArea.Selection.IsEmpty;
        var cut = new MenuItem { Header = "Вырезать", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control), IsEnabled = hasSelection };
        cut.Click += (_, _) => Cut();
        var copy = new MenuItem { Header = "Копировать", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsEnabled = hasSelection };
        copy.Click += (_, _) => Copy();
        var paste = new MenuItem { Header = "Вставить", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        paste.Click += (_, _) => _ = PastePlainTextAsync();
        items.Add(cut);
        items.Add(copy);
        items.Add(paste);

        var extra = new List<Control>();
        ContextMenuBuilding?.Invoke(this, extra);
        if (extra.Count > 0)
        {
            items.Add(new Separator());
            items.AddRange(extra);
        }

        return new ContextMenu { ItemsSource = items };
    }

    // ------------------------------------------------------------ оформление

    private IBrush? ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private FontFamily MonoFont =>
        this.TryFindResource("MonoFont", ActualThemeVariant, out var value) && value is FontFamily family
            ? family
            : new FontFamily("monospace");

    /// <summary>Оформление фразовых элементов по цепочкам символов.</summary>
    private sealed class InlineColorizer : DocumentColorizingTransformer
    {
        private readonly BlockEditor _editor;

        public InlineColorizer(BlockEditor editor)
        {
            _editor = editor;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var content = _editor._content;
            if (content.Length != CurrentContext.Document.TextLength)
            {
                return;
            }

            var mono = _editor.MonoFont;
            foreach (var span in content.Spans())
            {
                if (span.Chain.IsChip || span.Chain.Nodes.Count == 0)
                {
                    continue;
                }

                var start = Math.Max(span.Start, line.Offset);
                var end = Math.Min(span.End, line.EndOffset);
                if (start >= end)
                {
                    continue;
                }

                var nodes = span.Chain.Nodes;
                ChangeLinePart(start, end, element =>
                {
                    foreach (var node in nodes)
                    {
                        InlineStyles.Apply(element.TextRunProperties, node.Name, _editor.ThemeBrush, mono);
                        InlineStyles.ApplyFormatting(element.TextRunProperties, node);
                    }
                });
            }
        }
    }

    /// <summary>Плашки на месте символа-заместителя: картинка, ссылка, сноска, комментарий…</summary>
    private sealed class ChipGenerator : VisualLineElementGenerator
    {
        private readonly BlockEditor _editor;

        public ChipGenerator(BlockEditor editor)
        {
            _editor = editor;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var document = CurrentContext.Document;
            var end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
            return startOffset >= end ? -1 : document.IndexOf(InlineContent.ChipChar, startOffset, end - startOffset);
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            if (_editor._content.Length != CurrentContext.Document.TextLength)
            {
                return null;
            }

            var node = _editor._content.ChipAt(offset);
            return new InlineObjectElement(1, _editor.BuildChip(node));
        }
    }

    /// <summary>Свой вид плашки (например, картинка вместо подписи); null — обычная плашка.</summary>
    public Func<DitaNode, Control?>? ChipFactory { get; set; }

    private Control BuildChip(DitaNode? node)
    {
        if (node is not null && ChipFactory?.Invoke(node) is { } custom)
        {
            return custom;
        }

        var label = node is null ? "?" : InlineContent.DescribeChip(node);
        var chip = new Border
        {
            Background = ThemeBrush("EditorChipBackground"),
            BorderBrush = ThemeBrush("EditorChipBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontFamily = MonoFont,
                Foreground = ThemeBrush("EditorChipText")
            }
        };

        if (node is not null)
        {
            ToolTip.SetTip(chip, Core.Model.XmlSerializer.ToXml(node));
        }

        return chip;
    }
}
