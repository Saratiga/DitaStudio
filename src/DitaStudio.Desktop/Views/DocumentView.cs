using Avalonia;
using Avalonia.Controls;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Desktop.Preview;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Views;

/// <summary>
/// Вкладка документа Avalonia-оболочки: «Автор», «Исходный код», «Предпросмотр» — как WPF
/// DocumentPane. «Автор» — визуальный редактор (<see cref="AuthorView"/>), «Исходный код» —
/// AvaloniaEdit с автодополнением по каталогу, «Предпросмотр» — встроенный Chromium
/// (<see cref="PreviewPane"/>).
/// </summary>
public sealed class DocumentView : UserControl, IDocumentView, IDisposable
{
    private readonly DitaProject _project;
    private readonly IPdfPrinter _pdfPrinter;
    private readonly TabControl _tabs = new();
    private readonly XmlSourceEditor _source = new();
    private readonly AuthorView _author = new();
    private readonly PreviewPane _preview;
    private readonly MapOutlineView? _outline;
    private bool _syncing;
    private PreviewPane? _live;
    private GridSplitter? _liveSplitter;
    private readonly Grid _layout = new();

    public DocumentView(DitaProject project, DitaDocument document, IPdfPrinter pdfPrinter)
    {
        _project = project;
        _pdfPrinter = pdfPrinter;
        Document = document;
        _preview = new PreviewPane(project, document, CommitPendingEdits, pdfPrinter);
        _author.Load(document);
        _author.DocumentModified += (_, _) => RaiseDirty();
        _author.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _author.RootTitleCommitted += (_, _) => RootTitleCommitted?.Invoke(this, EventArgs.Empty);
        _author.BeforeStructuralEdit += (_, description) => Undo.Push(Document, description);

        _source.TextEdited += (_, _) =>
        {
            if (!_syncing)
            {
                Document.IsDirty = true;
                RaiseDirty();
            }
        };

        // У карты (map, bookmap) «Автор» — список её топиков по иерархии, а не пустое окно без текстовых блоков.
        if (document.Root.Name is "map" or "bookmap" && document.FilePath is not null)
        {
            _outline = new MapOutlineView(project, document);
            _outline.OpenRequested += path => OpenFileRequested?.Invoke(this, path);
        }

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Автор", Content = (Control?)_outline ?? _author },
            new TabItem { Header = "Исходный код", Content = _source },
            new TabItem { Header = "Предпросмотр", Content = _preview }
        };
        _tabs.SelectionChanged += OnTabChanged;
        _layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _layout.Children.Add(_tabs);
        Content = _layout;
    }

    /// <summary>Постраничный предпросмотр «рядом с текстом» (null, пока не включён) — для тестов.</summary>
    public PreviewPane? LivePreview => _live;

    /// <summary>
    /// «Предпросмотр рядом с текстом»: справа от редактора показываются листы размера из «Параметров страницы»; текст разбивается на
    /// листы при каждом нажатии клавиши (через <see cref="PreviewPane.LiveDelay"/>). Работает в «Авторе» и в исходном коде.
    /// Не для карты: у неё «Автор» — список топиков, а текст издания — вкладка «Предпросмотр».
    /// </summary>
    public bool ShowLivePreview
    {
        get => _live is not null;
        set
        {
            if (value == (_live is not null) || (value && Document.Root.Name is "map" or "bookmap"))
            {
                return;
            }

            if (value)
            {
                _live = new PreviewPane(_project, Document, CommitPendingEdits, _pdfPrinter)
                {
                    LiveCommit = () =>
                    {
                        if (Mode == EditorMode.Author)
                        {
                            _author.FlushForPreview();
                        }
                    },
                    LiveDocument = ParsedSourceOrNull
                };
                _live.MakeLive();
                _liveSplitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
                _layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                _layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                Grid.SetColumn(_liveSplitter, 1);
                Grid.SetColumn(_live, 2);
                _layout.Children.Add(_liveSplitter);
                _layout.Children.Add(_live);
                _ = _live.RefreshAsync();
            }
            else
            {
                _layout.Children.Remove(_live!);
                _layout.Children.Remove(_liveSplitter!);
                _layout.ColumnDefinitions.RemoveRange(1, 2);
                _live!.DisposeBrowser();
                _live = null;
                _liveSplitter = null;
            }
        }
    }

    // Исходный код, ещё не перенесённый в модель: разбирается для предпросмотра (модель и отмена не трогаются).
    private DitaDocument? ParsedSourceOrNull()
    {
        if (Mode != EditorMode.Source || string.IsNullOrWhiteSpace(_source.Text))
        {
            return null;
        }

        try
        {
            var parsed = DitaDocument.Parse(_source.Text);
            parsed.FilePath = Document.FilePath;
            return parsed;
        }
        catch (Exception)
        {
            return null; // пока XML не разбирается — остаётся прежний вид
        }
    }

    public DitaDocument Document { get; }

    public UndoStack Undo { get; } = new();

    public string Title => Document.Title;

    public string? FilePath => Document.FilePath;

    public bool IsDirty => Document.IsDirty;

    public IAuthorSurface Author => _author.Surface;

    /// <summary>Визуальный редактор (для тестов).</summary>
    public AuthorView AuthorEditor => _author;

    public event EventHandler? DirtyChanged;

    public event EventHandler? Saved;

    public event EventHandler? SelectionChanged;

    public event EventHandler? RootTitleCommitted;

    public event EventHandler<string>? OpenFileRequested;

    /// <summary>Список топиков карты вместо «Автора» (null у обычных топиков) — для тестов.</summary>
    public MapOutlineView? MapOutline => _outline;

    public EditorMode Mode
    {
        get => _tabs.SelectedIndex switch { 1 => EditorMode.Source, 2 => EditorMode.Preview, _ => EditorMode.Author };
        set => _tabs.SelectedIndex = value switch { EditorMode.Source => 1, EditorMode.Preview => 2, _ => 0 };
    }

    internal void RaiseDirty()
    {
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        _live?.NotifyChanged();
    }

    // ------------------------------------------------------------ исходный код

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != _tabs)
        {
            return;
        }

        var tabs = (TabItem[])_tabs.ItemsSource!;
        foreach (var removed in e.RemovedItems)
        {
            if (ReferenceEquals(removed, tabs[0]))
            {
                _author.FlushPendingEdits();
            }
            else if (ReferenceEquals(removed, tabs[1]))
            {
                // Уход из исходного кода: правки переносятся в модель (если XML разбирается).
                ApplySourceToModel();
            }
        }

        if (Mode == EditorMode.Source)
        {
            LoadSourceFromModel();
        }
        else if (Mode == EditorMode.Author)
        {
            _author.Rebuild(_author.CurrentNode);
            _outline?.Refresh();
        }
        else if (Mode == EditorMode.Preview)
        {
            _ = _preview.RefreshAsync();
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
            _author.Rebuild();
            RaiseDirty();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public string? CommitPendingEdits()
    {
        switch (Mode)
        {
            case EditorMode.Author:
                _author.FlushPendingEdits();
                return null;
            case EditorMode.Source:
                return ApplySourceToModel();
            default:
                return null;
        }
    }

    public void GoToSourceLine(int line)
    {
        Mode = EditorMode.Source;
        _source.GoToLine(line);
    }

    // ------------------------------------------------------------ предпросмотр

    /// <summary>Вкладка предпросмотра (для тестов).</summary>
    public PreviewPane Preview => _preview;

    /// <summary>Закрытие вкладки документа: освобождаем встроенный браузер предпросмотра.</summary>
    public void Dispose()
    {
        _preview.DisposeBrowser();
        _live?.DisposeBrowser();
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

    public string SnapshotXml()
    {
        if (Mode == EditorMode.Source)
        {
            return _source.Text ?? string.Empty;
        }

        _author.FlushPendingEdits();
        return Document.ToXmlString();
    }

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
        _author.Rebuild(_author.CurrentNode);
        _outline?.Refresh();
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
        if (_author.EditorFor(node) is null)
        {
            return false;
        }

        Mode = EditorMode.Author;
        return _author.FocusNode(node);
    }
}
