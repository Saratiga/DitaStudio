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
        _scroll = new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _root.Children.Add(_scroll);
        Content = _root;
        Themed(this, BackgroundProperty, "Surface");
        Surface = new AuthorViewSurface(this);
    }

    public DitaDocument? Document { get; private set; }

    /// <summary>Операции «Автора» для общих ViewModel'ей (IAuthorSurface).</summary>
    public AuthorSurfaceBase Surface { get; }

    public Labels Labels { get; set; } = Labels.Russian;

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
    public Control? ViewFor(DitaNode node) => _views.TryGetValue(node, out var view) ? view : null;

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
        if (focusNode is not null && _editors.TryGetValue(focusNode, out var editor))
        {
            CurrentNode = focusNode;
            Dispatcher.UIThread.Post(() => editor.FocusEditor(caretOffset), DispatcherPriority.Background);
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
        foreach (var editor in _order)
        {
            editor.Flush();
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

    private void ClearHighlight()
    {
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

            _view.BeforeStructuralEdit?.Invoke(_view, $"Оформление <{element}>");
            editor.WrapSelection(element);
            return true;
        }

        public override bool ApplyTextFormat(string prefix, string? token)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            var selection = editor.TextArea.Selection;
            var wholeBlock = !selection.IsEmpty && selection.SurroundingSegment.Offset == 0 &&
                             selection.SurroundingSegment.Length == editor.Document.TextLength;
            if (wholeBlock)
            {
                // Весь текст блока — класс на самом абзаце, без лишней обёртки; фразы внутри с
                // классом той же группы больше не нужны.
                _view.BeforeStructuralEdit?.Invoke(_view, "Оформление текста");
                editor.ApplyInlineClass(prefix, null);
                return SetCurrentBlockFormat(prefix, token);
            }

            _view.BeforeStructuralEdit?.Invoke(_view, "Оформление текста");
            if (token is null && selection.IsEmpty && TextFormatting.Token(CurrentNode, prefix) is not null)
            {
                return SetCurrentBlockFormat(prefix, null);
            }

            editor.ApplyInlineClass(prefix, token);
            editor.TextArea.Focus();
            return true;
        }

        public override bool InsertInlineElement(DitaNode element, string placeholder)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            _view.BeforeStructuralEdit?.Invoke(_view, $"Вставка <{element.Name}>");
            editor.InsertInlineElement(element, placeholder);
            return true;
        }

        public override bool InsertInlineNode(DitaNode node)
        {
            if (CurrentNode is null || _view.ActiveEditorFor(CurrentNode) is not { } editor)
            {
                return false;
            }

            _view.BeforeStructuralEdit?.Invoke(_view, $"Вставка <{node.Name}>");
            editor.InsertInlineNode(node);
            return true;
        }
    }
}
