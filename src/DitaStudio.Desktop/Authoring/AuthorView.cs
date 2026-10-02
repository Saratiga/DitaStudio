using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// Режим «Автор» Avalonia-оболочки: документ показывается как оформленный текст, каждая
/// правка сразу попадает в дерево DITA, структурные операции проверяются по контент-модели.
/// Порт WPF AuthorView: блоки — <see cref="BlockEditor"/>, контейнеры, списки, таблицы и
/// плашки строятся так же; модельные операции — общие (<see cref="AuthorSurfaceBase"/>).
/// Цвета берутся из ресурсов темы привязкой — переключение темы не требует перестройки.
/// </summary>
public sealed partial class AuthorView : UserControl
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(24, 18, 24, 120) };
    private readonly ScrollViewer _scroll;

    // Область «Сноски» внизу топика: текст каждой сноски правится здесь, в строке — плашка с номером.
    private readonly StackPanel _footnotes = new() { Margin = new Thickness(0, 28, 0, 0) };
    private List<DitaNode> _shownFootnotes = new();

    // Корень: прокрутка с блоками и всплывающие подсказки (им нужно место в дереве — ресурсы темы).
    private readonly Panel _root = new();
    private readonly Dictionary<DitaNode, BlockEditor> _editors = new();
    private readonly List<BlockEditor> _order = new();
    private readonly Dictionary<DitaNode, Control> _views = new();
    private readonly Dictionary<Control, DitaNode> _viewNodes = new();

    // Панели детей простых контейнеров (body, section…): их можно сверять поэлементно.
    private readonly Dictionary<DitaNode, StackPanel> _childPanels = new();

    private DitaNode? _current;
    private BlockEditor? _activeEditor;
    private Border? _currentBorder;
    private IDisposable? _currentBorderBinding;

    public AuthorView()
    {
        InitializeBlockSelection();
        // Esc выключает кисть маркера раньше редактора блока (тот ловит Esc сам).
        AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, e) =>
        {
            if (_markerPen && e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                StopMarkerPen();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scroll = new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _root.Children.Add(_scroll);
        _scroll.AddHandler(PointerPressedEvent, OnScrollPointerPressed, RoutingStrategies.Bubble, handledEventsToo: false);
        Content = _root;
        Focusable = true;
        Themed(this, BackgroundProperty, "Surface");
        Surface = new AuthorViewSurface(this);
    }

    public DitaDocument? Document { get; private set; }

    /// <summary>Операции «Автора» для общих ViewModel'ей (IAuthorSurface).</summary>
    public AuthorSurfaceBase Surface { get; }

    public Labels Labels { get; set; } = Labels.For(Loc.Instance.Language);

    /// <summary>Элемент, в котором сейчас находится курсор.</summary>
    public DitaNode? CurrentNode
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value))
            {
                return;
            }

            _current = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    /// <summary>Документ изменён (для отметки «не сохранено»).</summary>
    public event EventHandler? DocumentModified;

    /// <summary>Фокус ушёл из заголовка (title) корневого топика, и текст заголовка был изменён.</summary>
    public event EventHandler? RootTitleCommitted;

    /// <summary>Сообщение для строки состояния (скопировано, вставлено, вставить нельзя).</summary>
    public event EventHandler<string>? StatusRequested;

    /// <summary>Просьба сохранить состояние для отмены перед структурной операцией.</summary>
    public event EventHandler<string>? BeforeStructuralEdit;

    /// <summary>Меню правой кнопки у блока строится — можно добавить свои пункты.</summary>
    public event Action<BlockEditor, List<Control>>? ContextMenuBuilding;

    /// <summary>Редакторы блоков в порядке документа (для тестов и переходов).</summary>
    public IReadOnlyList<BlockEditor> Editors => _order;

    /// <summary>Редактор узла (первый участок, если текст узла разбит вложенными блоками).</summary>
    public BlockEditor? EditorFor(DitaNode node) => _editors.TryGetValue(node, out var editor) ? editor : null;

    /// <summary>Редактор, в котором последним был курсор, если он относится к узлу, иначе — первый редактор узла.</summary>
    /// <summary>Отрисованный блок узла (рамка, строка текста) или null.</summary>
    public Control? ViewFor(DitaNode node)
    {
        if (!_views.TryGetValue(node, out var view))
        {
            return null;
        }

        // Пометки над блоком (отдельный лист, атрибуты) — обёртка вокруг самого блока: наружу отдаётся блок.
        while (view is StackPanel { Tag: ViewWrapperTag } wrapper && wrapper.Children.Count > 0)
        {
            view = wrapper.Children[^1];
        }

        return view;
    }

    private const string ViewWrapperTag = "view-wrapper";

    public BlockEditor? ActiveEditorFor(DitaNode node) =>
        _activeEditor is { } active && ReferenceEquals(active.Node, node) && _order.Contains(active) ? active : EditorFor(node);

    // ---------------------------------------------------------------- загрузка

    public void Load(DitaDocument document)
    {
        Document = document;
        Rebuild();
    }

    public void Rebuild(DitaNode? focusNode = null, int caretOffset = 0)
    {
        _editors.Clear();
        _order.Clear();
        _views.Clear();
        _viewNodes.Clear();
        _childPanels.Clear();
        _activeEditor = null;
        _panel.Children.Clear();
        _shownFootnotes = new List<DitaNode>();
        ClearHighlight();

        if (Document is null)
        {
            return;
        }

        if (BuildNode(Document.Root, 0) is { } root)
        {
            _panel.Children.Add(root);
        }

        FocusAfterRebuild(focusNode, caretOffset);
    }

    private void FocusAfterRebuild(DitaNode? focusNode, int caretOffset)
    {
        RefreshFootnotes();
        RefreshCaptionBadges();
        if (focusNode is not null && _editors.TryGetValue(focusNode, out var editor))
        {
            CurrentNode = focusNode;
            // Если за это время выделили блоки целиком (вставка, «Обернуть в…»), фокус в текст не забирается — иначе выделение сразу бы снялось.
            Dispatcher.UIThread.Post(() =>
            {
                if (_outlines.Count == 0 && _cellTable is null)
                {
                    editor.FocusEditor(caretOffset);
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// После правки детей <paramref name="parent"/>: пересобираются только блоки из
    /// <paramref name="changed"/> и новые, остальные представления остаются как есть (Enter в
    /// топике из сотен абзацев не перерисовывает их все). Списки, смешанное содержимое и
    /// таблицы перестраиваются целиком — см. <see cref="RebuildAround"/>.
    /// </summary>
    public void RefreshChildren(DitaNode parent, IEnumerable<DitaNode> changed, DitaNode? focusNode, int caretOffset = 0)
    {
        if (!_childPanels.TryGetValue(parent, out var stack) || stack.Parent is null)
        {
            RebuildAround(parent, focusNode, caretOffset);
            return;
        }

        // Убираются представления изменённых детей и детей, которых больше нет у родителя.
        var rebuild = new HashSet<DitaNode>(changed, ReferenceEqualityComparer.Instance);
        foreach (var control in stack.Children.ToList())
        {
            if (_viewNodes.TryGetValue(control, out var node) && (rebuild.Contains(node) || !ReferenceEquals(node.Parent, parent)))
            {
                stack.Children.Remove(control);
                ForgetViews(node);
            }
        }

        var index = stack.Children.TakeWhile(c => !_viewNodes.ContainsKey(c)).Count();
        foreach (var child in parent.Children)
        {
            var view = _views.TryGetValue(child, out var existing) ? existing : BuildNode(child, 0);
            if (view is null)
            {
                continue;
            }

            var at = stack.Children.IndexOf(view);
            if (at < 0)
            {
                stack.Children.Insert(index, view);
            }
            else if (at != index)
            {
                stack.Children.Move(at, index);
            }

            index++;
        }

        AfterPartialRebuild(focusNode, caretOffset);
    }

    /// <summary>
    /// Область «Сноски»: перестраивается, если сноски документа (набор или порядок) изменились —
    /// после записи абзаца, структурной правки, отмены.
    /// </summary>
    public void RefreshFootnotes()
    {
        if (Document is null)
        {
            return;
        }

        var notes = Document.Root.Descendants().Where(n => n.Kind == NodeKind.Element && n.Name == "fn").ToList();
        if (notes.Count == _shownFootnotes.Count && notes.Zip(_shownFootnotes).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        foreach (var old in _shownFootnotes)
        {
            if (_editors.Remove(old, out var editor))
            {
                _order.Remove(editor);
            }

            ForgetViews(old);
        }

        _footnotes.Children.Clear();
        _panel.Children.Remove(_footnotes);
        _shownFootnotes = notes;
        if (notes.Count > 0)
        {
            var rule = new Border { Height = 1, Width = 180, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            Themed(rule, Border.BackgroundProperty, "Line");
            var header = new TextBlock { Text = Loc.T("Author_Footnotes"), FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            Themed(header, TextBlock.ForegroundProperty, "TextMuted");
            _footnotes.Children.Add(rule);
            _footnotes.Children.Add(header);
            for (var i = 0; i < notes.Count; i++)
            {
                var number = new TextBlock { Text = $"{i + 1}.", Margin = new Thickness(0, 5, 6, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                Themed(number, TextBlock.ForegroundProperty, "EditorChipText");
                var view = BuildNode(notes[i], 0) ?? new Panel();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*") };
                Grid.SetColumn(view, 1);
                row.Children.Add(number);
                row.Children.Add(view);
                _footnotes.Children.Add(row);
            }

            _panel.Children.Add(_footnotes);
        }

        // Номера на плашках сносок в тексте — по новому порядку.
        foreach (var editor in _order)
        {
            editor.TextArea.TextView.Redraw();
        }
    }

    /// <summary>Курсор — в текст сноски в области «Сноски» (весь текст выделен, если нужно).</summary>
    public void FocusFootnote(DitaNode fn, bool selectAll)
    {
        RefreshFootnotes();
        if (!_editors.TryGetValue(fn, out var editor))
        {
            return;
        }

        CurrentNode = fn;
        Dispatcher.UIThread.Post(() =>
        {
            editor.FocusEditor(0);
            if (selectAll)
            {
                editor.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    private void ForgetViews(DitaNode root)
    {
        foreach (var node in root.DescendantsAndSelf())
        {
            if (_views.Remove(node, out var view))
            {
                _viewNodes.Remove(view);
            }

            _childPanels.Remove(node);
        }
    }

    private void AfterPartialRebuild(DitaNode? focusNode, int caretOffset)
    {
        if (_currentBorder is not null && _currentBorder.GetVisualRoot() is null)
        {
            ClearHighlight();
        }

        // Порядок редакторов — по документу; редакторы убранных блоков ушли вместе с ними.
        _order.Clear();
        _order.AddRange(_panel.GetLogicalDescendants().OfType<BlockEditor>());
        _editors.Clear();
        foreach (var editor in _order)
        {
            _editors.TryAdd(editor.Node, editor);
        }

        if (_activeEditor is not null && !_order.Contains(_activeEditor))
        {
            _activeEditor = null;
        }

        FocusAfterRebuild(focusNode, caretOffset);
    }

    /// <summary>
    /// Перестраивает только ближайший к <paramref name="changed"/> показанный контейнер —
    /// Enter, Backspace и Tab в большом документе не перерисовывают весь топик. Если такого
    /// контейнера нет, перестраивается всё.
    /// </summary>
    public void RebuildAround(DitaNode changed, DitaNode? focusNode, int caretOffset = 0)
    {
        var target = changed;
        while (target is not null && !(_views.TryGetValue(target, out var shown) && shown.Parent is not null && target.Parent is not null))
        {
            target = target.Parent;
        }

        if (target is null || !_views.TryGetValue(target, out var old) || !Detach(old))
        {
            Rebuild(focusNode, caretOffset);
            return;
        }

        ForgetViews(target);
        var fresh = BuildNode(target, 0) ?? new Panel();
        _replaceSlot!(fresh);
        _replaceSlot = null;
        AfterPartialRebuild(focusNode, caretOffset);
    }

    private Action<Control>? _replaceSlot;

    /// <summary>Вынимает представление из родителя и запоминает, как вставить замену на то же место.</summary>
    private bool Detach(Control old)
    {
        switch (old.Parent)
        {
            case Panel panel:
            {
                var index = panel.Children.IndexOf(old);
                var (row, column, rowSpan, columnSpan) = (Grid.GetRow(old), Grid.GetColumn(old), Grid.GetRowSpan(old), Grid.GetColumnSpan(old));
                panel.Children.RemoveAt(index);
                _replaceSlot = fresh =>
                {
                    Grid.SetRow(fresh, row);
                    Grid.SetColumn(fresh, column);
                    Grid.SetRowSpan(fresh, rowSpan);
                    Grid.SetColumnSpan(fresh, columnSpan);
                    panel.Children.Insert(index, fresh);
                };
                return true;
            }

            case Decorator decorator:
                decorator.Child = null;
                _replaceSlot = fresh => decorator.Child = fresh;
                return true;

            case ContentControl content:
                content.Content = null;
                _replaceSlot = fresh => content.Content = fresh;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Сохраняет незаписанные правки всех редакторов блоков в модель.</summary>
    public void FlushPendingEdits()
    {
        // Копия: запись абзаца может перестроить область «Сноски», а с ней и список редакторов.
        foreach (var editor in _order.ToList())
        {
            editor.Flush();
        }
    }

    /// <summary>Записывает несохранённый текст блоков в модель для «живого» предпросмотра, не закрывая правку (см. <see cref="BlockEditor.FlushForPreview"/>).</summary>
    public void FlushForPreview()
    {
        foreach (var editor in _order.ToList())
        {
            editor.FlushForPreview();
        }
    }

    /// <summary>Ставит курсор в редактор узла; false — у узла нет своего редактора.</summary>
    public bool FocusNode(DitaNode node)
    {
        if (!_editors.TryGetValue(node, out var editor))
        {
            return false;
        }

        CurrentNode = node;
        editor.FocusEditor();
        editor.BringIntoView();
        return true;
    }

    private void Modified()
    {
        if (Document is not null)
        {
            Document.IsDirty = true;
        }

        DocumentModified?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- тема

    /// <summary>Привязывает свойство к ресурсу темы (обновляется при переключении темы).</summary>
    private static IDisposable Themed(StyledElement target, AvaloniaProperty property, string key) =>
        target.Bind(property, target.GetResourceObservable(key));

    // ---------------------------------------------------------------- выделение блока

    private void AttachSelection(Border border, DitaNode node)
    {
        border.Tag = node;
        border.AddHandler(PointerPressedEvent, (_, e) =>
        {
            // Щелчок по самой рамке, подписи или плашке (не внутри редактора — тот помечает
            // событие обработанным) выбирает элемент, как щелчок по рамке в WPF-версии.
            var owner = (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true);
            while (owner is not null && owner.Tag is not DitaNode)
            {
                owner = owner.FindAncestorOfType<Border>();
            }

            if (ReferenceEquals(owner, border))
            {
                CurrentNode = node;
                HighlightBorder(border);
                // Блок выделяется рамкой целиком и забирает клавиатуру: Delete и Backspace удаляют его, Esc снимает выделение.
                SelectOutline(border);
                Focus();
                e.Handled = true;
            }
        });
    }

    private void HighlightEditor(BlockEditor editor)
    {
        var border = editor.FindAncestorOfType<Border>();
        while (border is not null && border.Tag is not DitaNode)
        {
            border = border.FindAncestorOfType<Border>();
        }

        if (border is not null)
        {
            HighlightBorder(border);
        }
    }

    private void HighlightBorder(Border border)
    {
        if (ReferenceEquals(_currentBorder, border))
        {
            return;
        }

        // Подсвечиваются только строки текстовых блоков (левая полоса), как в WPF-версии.
        if (border.BorderThickness.Left < 2 || border.BorderThickness.Top != 0)
        {
            return;
        }

        ClearHighlight();
        _currentBorderBinding = Themed(border, Border.BorderBrushProperty, "Accent");
        _currentBorder = border;
    }

    // ---------------------------------------------------------------- кисть маркера

    private bool _markerPen;
    private string? _markerPenToken;

    /// <summary>Режим кисти: выделение текста мышью сразу закрашивается выбранным цветом (null — «ластик»), пока не нажат Esc.</summary>
    public bool MarkerPenActive => _markerPen;

    public string? MarkerPenToken => _markerPenToken;

    /// <summary>Есть ли выделенный текст в блоке, где стоит курсор.</summary>
    public bool HasTextSelection => _activeEditor is { } editor && _order.Contains(editor) && !editor.TextArea.Selection.IsEmpty;

    public bool StartMarkerPen(string? token)
    {
        _markerPen = true;
        _markerPenToken = token;
        SelectionChanged?.Invoke(this, EventArgs.Empty); // панель инструментов обновляет вид кнопки
        return true;
    }

    public void StopMarkerPen()
    {
        if (!_markerPen)
        {
            return;
        }

        _markerPen = false;
        _markerPenToken = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseSelectionFinished(BlockEditor editor)
    {
        if (!_markerPen || editor.TextArea.Selection.IsEmpty || !_order.Contains(editor))
        {
            return;
        }

        _activeEditor = editor;
        CurrentNode = editor.Node;
        Surface.ApplyTextFormat(TextFormatting.MarkPrefix, _markerPenToken);
    }

    private void ClearHighlight()
    {
        ClearOutline();
        ClearCellSelection();
        _currentBorderBinding?.Dispose();
        _currentBorderBinding = null;
        if (_currentBorder is not null)
        {
            _currentBorder.BorderBrush = Brushes.Transparent;
        }

        _currentBorder = null;
    }

    /// <summary>Модельные операции «Автора» поверх этого представления.</summary>
    private sealed class AuthorViewSurface : AuthorSurfaceBase
    {
        private readonly AuthorView _view;

        public AuthorViewSurface(AuthorView view)
        {
            _view = view;
        }

        public override DitaDocument? Document => _view.Document;

        public override DitaNode? CurrentNode
        {
            get => _view.CurrentNode;
            protected set => _view.CurrentNode = value;
        }

        public override bool SelectCurrentTable()
        {
            if (CurrentTable() is not { } table || _view.ViewFor(table) is not { } view ||
                (view as Border ?? view.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, table))) is not { } border)
            {
                return false;
            }

            _view.SelectTableOutline(border, table);
            return true;
        }

        protected override void BeforeStructuralEdit(string description) => _view.BeforeStructuralEdit?.Invoke(_view, description);

        protected override void AfterStructuralEdit(DitaNode? focus, DitaNode? parent, IReadOnlyList<DitaNode> changed)
        {
            _view.DocumentModified?.Invoke(_view, EventArgs.Empty);
            var top = parent;
            while (top?.Parent is not null)
            {
                top = top.Parent;
            }

            if (parent is null || !ReferenceEquals(top, _view.Document?.Root))
            {
                _view.Rebuild(focus, 0);
            }
            else
            {
                _view.RefreshChildren(parent, changed, focus);
            }
        }

        public override void FlushPendingEdits() => _view.FlushPendingEdits();

        public override void Rebuild()
        {
            _view.FlushPendingEdits();
            _view.Rebuild(_view.CurrentNode);
        }

        public override bool WrapCurrentInline(string element)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            _view.BeforeStructuralEdit?.Invoke(_view, Loc.T("Author_Formatting0", element));
            editor.WrapSelection(element);
            return true;
        }

        public override bool ApplyTextFormat(string prefix, string? token)
        {
            if (CurrentNode is null)
            {
                return false;
            }

            // Маркер — всегда фразовый элемент вокруг выделенного текста (в DOCX это выделение знаков, а не заливка абзаца), поэтому
            // у контейнера без текста и без выделения он недоступен.
            var marker = prefix == TextFormatting.MarkPrefix;

            // Контейнер (note, section, div…), выбранный щелчком по рамке: у него нет своего текста —
            // класс ставится на сам контейнер и действует на всё внутри.
            if (_view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return !marker && SetCurrentBlockFormat(prefix, token);
            }

            var selection = editor.TextArea.Selection;
            var wholeBlock = !marker && !selection.IsEmpty && selection.SurroundingSegment.Offset == 0 &&
                             selection.SurroundingSegment.Length == editor.Document.TextLength;
            if (wholeBlock)
            {
                // Весь текст блока — класс на самом абзаце, без лишней обёртки; фразы внутри с
                // классом той же группы больше не нужны.
                _view.BeforeStructuralEdit?.Invoke(_view, Loc.T("Author_TextFormatting"));
                editor.ApplyInlineClass(prefix, null);
                return SetCurrentBlockFormat(prefix, token);
            }

            _view.BeforeStructuralEdit?.Invoke(_view, Loc.T("Author_TextFormatting"));
            if (!marker && token is null && selection.IsEmpty && TextFormatting.Token(CurrentNode, prefix) is not null)
            {
                return SetCurrentBlockFormat(prefix, null);
            }

            editor.ApplyInlineClass(prefix, token);
            editor.TextArea.Focus();
            return true;
        }

        public override (DitaNode Table, DitaStudio.Core.Editing.CellRange Range)? CellSelection => _view.SelectedCells;

        protected override void ReselectCells(DitaNode table, DitaStudio.Core.Editing.CellRange? range)
        {
            if (range is null)
            {
                _view.ClearCellSelection();
            }
            else
            {
                _view.SelectCellRange(table, range.Value, focus: true);
            }
        }

        public override bool HasTextSelection => _view.HasTextSelection;

        public override bool MarkerPenActive => _view.MarkerPenActive;

        public override string? MarkerPenToken => _view.MarkerPenToken;

        public override bool StartMarkerPen(string? token) => _view.StartMarkerPen(token);

        public override void StopMarkerPen() => _view.StopMarkerPen();

        public override bool InsertInlineElement(DitaNode element, string placeholder)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            _view.BeforeStructuralEdit?.Invoke(_view, Loc.T("Author_Insert0", element.Name));
            if (element.Name == "fn")
            {
                // Сноска — плашкой у курсора, её текст (выделенный или заготовка) — в области
                // «Сноски» внизу, куда и переходит курсор.
                var selection = editor.TextArea.Selection;
                string? selected = null;
                if (!selection.IsEmpty)
                {
                    var segment = selection.SurroundingSegment;
                    selected = editor.Document.GetText(segment).Replace(InlineContent.ChipChar.ToString(), string.Empty).Trim();
                    editor.Document.Remove(segment.Offset, segment.Length);
                }

                element.Add(DitaNode.Text(string.IsNullOrEmpty(selected) ? placeholder : selected));
                editor.InsertInlineNode(element);
                _view.FocusFootnote(element, selectAll: string.IsNullOrEmpty(selected));
                return true;
            }

            editor.InsertInlineElement(element, placeholder);
            return true;
        }

        public override bool InsertInlineNode(DitaNode node)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            _view.BeforeStructuralEdit?.Invoke(_view, Loc.T("Author_Insert0", node.Name));
            editor.InsertInlineNode(node);
            return true;
        }
    }
}
