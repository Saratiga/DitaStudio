using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Views;

/// <summary>
/// Вкладка документа Avalonia-оболочки: «Автор», «Исходный код», «Предпросмотр» — как WPF
/// DocumentPane. Промежуточная версия этапа 3 переноса: «Автор» показывает структуру документа
/// (выбор элемента, структурные команды, атрибуты, палитра), текст правится в «Исходном коде»;
/// «Исходный код» — AvaloniaEdit с автодополнением по каталогу; полноценный визуальный
/// редактор — этап 5, встроенный предпросмотр — этап 6 (пока — во внешнем браузере).
/// </summary>
public sealed class DocumentView : UserControl, IDocumentView
{
    private readonly DitaProject _project;
    private readonly TabControl _tabs = new();
    private readonly XmlSourceEditor _source = new();
    private readonly TreeView _structure = new();
    private readonly StructureAuthorSurface _author;
    private bool _syncing;

    public DocumentView(DitaProject project, DitaDocument document)
    {
        _project = project;
        Document = document;
        _author = new StructureAuthorSurface(this);

        _source.TextEdited += (_, _) =>
        {
            if (!_syncing)
            {
                Document.IsDirty = true;
                RaiseDirty();
            }
        };

        // Структура документа раскрыта целиком — как текст в режиме «Автор».
        _structure.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, true) }
        });
        _structure.ItemTemplate = new FuncTreeDataTemplate<StructureItem>(
            (item, _) => BuildStructureRow(item), item => item.Children);
        _structure.SelectionChanged += (_, _) =>
        {
            if (_structure.SelectedItem is StructureItem item)
            {
                _author.Select(item.Node);
            }
        };

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Автор", Content = BuildAuthorPane() },
            new TabItem { Header = "Исходный код", Content = _source },
            new TabItem { Header = "Предпросмотр", Content = BuildPreviewPane() }
        };
        _tabs.SelectionChanged += OnTabChanged;
        Content = _tabs;

        RebuildStructure();
    }

    public DitaDocument Document { get; }

    public UndoStack Undo { get; } = new();

    public string Title => Document.Title;

    public string? FilePath => Document.FilePath;

    public bool IsDirty => Document.IsDirty;

    public IAuthorSurface Author => _author;

    public event EventHandler? DirtyChanged;

    public event EventHandler? Saved;

    public event EventHandler? SelectionChanged;

    public EditorMode Mode
    {
        get => _tabs.SelectedIndex switch { 1 => EditorMode.Source, 2 => EditorMode.Preview, _ => EditorMode.Author };
        set => _tabs.SelectedIndex = value switch { EditorMode.Source => 1, EditorMode.Preview => 2, _ => 0 };
    }

    // ------------------------------------------------------------ «Автор» (структура)

    private Control BuildAuthorPane()
    {
        var hint = new TextBlock
        {
            Text = "Промежуточный режим: выберите элемент — доступны палитра вставки, атрибуты и команды меню " +
                   "«Структура». Текст правится на вкладке «Исходный код»; визуальный редактор переносится.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Margin = new Thickness(10, 8, 10, 6)
        };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("TextMuted"));
        DockPanel.SetDock(hint, Dock.Top);

        var dock = new DockPanel();
        dock.Children.Add(hint);
        dock.Children.Add(_structure);
        return dock;
    }

    private static Control BuildStructureRow(StructureItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = item.Node.Name, FontSize = 12 };
        name.Bind(TextBlock.FontFamilyProperty, name.GetResourceObservable("MonoFont"));
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("Accent"));
        row.Children.Add(name);

        if (item.Text.Length > 0)
        {
            var text = new TextBlock { Text = "  " + item.Text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 720 };
            row.Children.Add(text);
        }

        return row;
    }

    internal void RebuildStructure(DitaNode? focus = null)
    {
        var root = new StructureItem(Document.Root, 0);
        _structure.ItemsSource = new[] { root };
        if (focus is not null && root.Find(focus) is { } selected)
        {
            _structure.SelectedItem = selected;
        }
    }

    internal void RaiseSelection() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    internal void RaiseDirty() => DirtyChanged?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------ исходный код

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != _tabs)
        {
            return;
        }

        if (e.RemovedItems.Count > 0 && ReferenceEquals(e.RemovedItems[0], ((TabItem[])_tabs.ItemsSource!)[1]))
        {
            // Уход из исходного кода: правки переносятся в модель (если XML разбирается).
            ApplySourceToModel();
        }

        if (Mode == EditorMode.Source)
        {
            LoadSourceFromModel();
        }
    }

    private void LoadSourceFromModel()
    {
        _syncing = true;
        try
        {
            _source.Text = Document.ToXmlString();
        }
        finally
        {
            _syncing = false;
        }
    }

    private string? ApplySourceToModel()
    {
        var text = _source.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var parsed = DitaDocument.Parse(text);
            if (Document.ToXmlString().Trim() == text.Trim())
            {
                return null;
            }

            Undo.Push(Document, "Правка исходного кода");
            Document.Root = parsed.Root;
            Document.DoctypeName = parsed.DoctypeName;
            Document.DoctypePublicId = parsed.DoctypePublicId;
            Document.DoctypeSystemId = parsed.DoctypeSystemId;
            Document.IsDirty = true;
            RebuildStructure();
            RaiseDirty();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public string? CommitPendingEdits() => Mode == EditorMode.Source ? ApplySourceToModel() : null;

    public void GoToSourceLine(int line)
    {
        Mode = EditorMode.Source;
        _source.GoToLine(line);
    }

    // ------------------------------------------------------------ предпросмотр

    private Control BuildPreviewPane()
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        var note = new TextBlock
        {
            Text = "Встроенный предпросмотр (HTML, PDF, DOCX-приближённо) переносится на Chromium (CefGlue). " +
                   "Пока документ можно посмотреть в браузере по умолчанию.",
            TextWrapping = TextWrapping.Wrap
        };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("TextMuted"));
        var open = new Button { Content = "Открыть предпросмотр в браузере", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => OpenPreviewInBrowser();
        panel.Children.Add(note);
        panel.Children.Add(open);
        return panel;
    }

    private void OpenPreviewInBrowser()
    {
        CommitPendingEdits();
        string html;
        try
        {
            html = new HtmlPublisher(_project).RenderPreview(Document);
        }
        catch (Exception ex)
        {
            html = "<html><body><p>Не удалось построить предпросмотр:</p><pre>" +
                   System.Net.WebUtility.HtmlEncode(ex.Message) + "</pre></body></html>";
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "DitaStudioPreview");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, PreviewFileBaseName(Document.FilePath) + ".html");
            File.WriteAllText(path, html, Encoding.UTF8);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // без браузера предпросмотр просто не откроется — редактор продолжает работать
        }
    }

    private static string PreviewFileBaseName(string? filePath)
    {
        if (filePath is null)
        {
            return "preview";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath).ToUpperInvariant()));
        return Path.GetFileNameWithoutExtension(filePath) + "-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    // ------------------------------------------------------------ сохранение и отмена

    public bool Save(out string? error)
    {
        error = CommitPendingEdits();
        if (error is not null)
        {
            return false;
        }

        try
        {
            Document.Save();
            _project.Register(Document);
            if (Document.FilePath is not null)
            {
                _project.RefreshFileInfo(Document.FilePath);
            }

            RaiseDirty();
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public string SnapshotXml() => Mode == EditorMode.Source ? _source.Text ?? string.Empty : Document.ToXmlString();

    public void ReloadFromDisk()
    {
        CommitPendingEdits();
        if (Document.IsDirty)
        {
            Undo.Push(Document, "Перезагрузка с диска");
        }

        Document.Reload();
        ReloadViews();
    }

    public void MarkMissingOnDisk()
    {
        Document.DiskStamp = null;
        Document.IsDirty = true;
        RaiseDirty();
    }

    public void RestoreFromRecovery(string content)
    {
        DitaDocument parsed;
        try
        {
            parsed = DitaDocument.Parse(content);
        }
        catch (Exception)
        {
            Mode = EditorMode.Source;
            _source.Text = content;
            Document.IsDirty = true;
            RaiseDirty();
            return;
        }

        Undo.Push(Document, "Восстановление после сбоя");
        Document.Root = parsed.Root;
        Document.DoctypeName = parsed.DoctypeName;
        Document.DoctypePublicId = parsed.DoctypePublicId;
        Document.DoctypeSystemId = parsed.DoctypeSystemId;
        Document.IsDirty = true;
        ReloadViews();
    }

    public void ReloadViews()
    {
        RebuildStructure(_author.CurrentNode);
        if (Mode == EditorMode.Source)
        {
            LoadSourceFromModel();
        }

        RaiseDirty();
    }

    public void PerformUndo()
    {
        CommitPendingEdits();
        if (Undo.Undo(Document))
        {
            ReloadViews();
        }
    }

    public void PerformRedo()
    {
        CommitPendingEdits();
        if (Undo.Redo(Document))
        {
            ReloadViews();
        }
    }

    public void PushUndo(string description)
    {
        CommitPendingEdits();
        Undo.Push(Document, description);
    }

    public bool FocusNode(DitaNode node)
    {
        Mode = EditorMode.Author;
        RebuildStructure(node);
        _author.Select(node);
        return true;
    }

    /// <summary>Строка дерева структуры.</summary>
    private sealed class StructureItem
    {
        public StructureItem(DitaNode node, int depth)
        {
            Node = node;
            var own = string.Concat(node.Children.Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();
            var text = own.Length > 0 ? own : node.ElementChildren().Any() ? string.Empty : node.InnerText.Trim();
            Text = text.Length > 90 ? text[..90] + "…" : text;
            Children = node.ElementChildren().Select(c => new StructureItem(c, depth + 1)).ToList();
        }

        public DitaNode Node { get; }

        public string Text { get; }

        public List<StructureItem> Children { get; }

        public StructureItem? Find(DitaNode node) =>
            ReferenceEquals(Node, node) ? this : Children.Select(c => c.Find(node)).FirstOrDefault(f => f is not null);
    }
}

/// <summary>Операции «Автора» над элементом, выбранным в дереве структуры (промежуточный режим).</summary>
internal sealed class StructureAuthorSurface : AuthorSurfaceBase
{
    private readonly DocumentView _view;
    private DitaNode? _current;

    public StructureAuthorSurface(DocumentView view)
    {
        _view = view;
    }

    public override DitaDocument? Document => _view.Document;

    public override DitaNode? CurrentNode
    {
        get => _current;
        protected set => _current = value;
    }

    public void Select(DitaNode node)
    {
        if (ReferenceEquals(_current, node))
        {
            return;
        }

        _current = node;
        _view.RaiseSelection();
    }

    protected override void BeforeStructuralEdit(string description) => _view.PushUndo(description);

    protected override void AfterStructuralEdit(DitaNode? focus)
    {
        _current = focus;
        _view.RebuildStructure(focus);
        _view.RaiseDirty();
        _view.RaiseSelection();
    }

    public override void Rebuild() => _view.RebuildStructure(_current);

    // Оформление выделенного текста и вставка внутрь строки требуют текстового редактора —
    // появятся вместе с визуальным режимом «Автор».
    public override bool WrapCurrentInline(string element) => false;

    public override bool InsertInlineNode(DitaNode node) => false;
}
