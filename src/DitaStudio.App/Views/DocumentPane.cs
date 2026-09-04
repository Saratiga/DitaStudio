using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DitaStudio.App.Authoring;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using Microsoft.Web.WebView2.Wpf;

namespace DitaStudio.App.Views;

public enum EditorMode
{
    Author,
    Source,
    Preview
}

/// <summary>
/// Вкладка одного документа: режимы «Автор», «Исходный код» и «Предпросмотр»
/// с общей моделью и общей историей отмены.
/// </summary>
public sealed class DocumentPane : Grid
{
    private readonly TabControl _tabs = new();
    private readonly TabItem _authorTab;
    private readonly TabItem _sourceTab;
    private readonly TabItem _previewTab;
    private readonly WebView2 _browser = new();
    private readonly DitaProject _project;

    private bool _syncing;
    private string? _previewFile;

    public DocumentPane(DitaProject project, DitaDocument document)
    {
        _project = project;
        Document = document;

        Author = new AuthorView { Project = project };
        Author.Load(document);
        Author.DocumentModified += (_, _) => RaiseDirty();
        Author.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Author.BeforeStructuralEdit += (_, description) => Undo.Push(Document, description);

        Source = new XmlSourceEditor();
        Source.TextEdited += (_, _) =>
        {
            if (!_syncing)
            {
                Document.IsDirty = true;
                RaiseDirty();
            }
        };

        _authorTab = new TabItem { Header = "Автор", Content = Author };
        _sourceTab = new TabItem { Header = "Исходный код", Content = Source };
        _previewTab = new TabItem { Header = "Предпросмотр", Content = BuildPreviewPane() };

        _tabs.Items.Add(_authorTab);
        _tabs.Items.Add(_sourceTab);
        _tabs.Items.Add(_previewTab);
        _tabs.SelectionChanged += OnTabChanged;

        Children.Add(_tabs);
    }

    public DitaDocument Document { get; }

    public AuthorView Author { get; }

    public XmlSourceEditor Source { get; }

    public UndoStack Undo { get; } = new();

    public string Title => Document.Title;

    public string? FilePath => Document.FilePath;

    public bool IsDirty => Document.IsDirty;

    public event EventHandler? DirtyChanged;

    public event EventHandler? SelectionChanged;

    public EditorMode Mode
    {
        get => _tabs.SelectedIndex switch { 1 => EditorMode.Source, 2 => EditorMode.Preview, _ => EditorMode.Author };
        set => _tabs.SelectedIndex = value switch { EditorMode.Source => 1, EditorMode.Preview => 2, _ => 0 };
    }

    private FrameworkElement BuildPreviewPane()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
            Margin = new Thickness(0)
        };

        var refresh = new Button { Content = "Обновить", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 4, 4, 4) };
        refresh.Click += (_, _) => RefreshPreview();

        var openInBrowser = new Button { Content = "Открыть в браузере", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 4, 4, 4) };
        openInBrowser.Click += (_, _) => OpenPreviewInBrowser();

        bar.Children.Add(refresh);
        bar.Children.Add(openInBrowser);

        Grid.SetRow(bar, 0);
        Grid.SetRow(_browser, 1);
        grid.Children.Add(bar);
        grid.Children.Add(_browser);
        return grid;
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _tabs))
        {
            return;
        }

        foreach (var removed in e.RemovedItems)
        {
            if (ReferenceEquals(removed, _authorTab))
            {
                Author.FlushPendingEdits();
            }
            else if (ReferenceEquals(removed, _sourceTab))
            {
                ApplySourceToModel();
            }
        }

        foreach (var added in e.AddedItems)
        {
            if (ReferenceEquals(added, _sourceTab))
            {
                LoadSourceFromModel();
            }
            else if (ReferenceEquals(added, _authorTab))
            {
                Author.Rebuild();
            }
            else if (ReferenceEquals(added, _previewTab))
            {
                RefreshPreview();
            }
        }
    }

    public void LoadSourceFromModel()
    {
        _syncing = true;
        try
        {
            Source.Text = Document.ToXmlString();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Переносит правки из режима исходного кода в модель. Возвращает текст ошибки.</summary>
    public string? ApplySourceToModel()
    {
        var text = Source.Text;
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
            Author.Load(Document);
            RaiseDirty();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Синхронизирует активный режим с моделью перед сохранением или проверкой.</summary>
    public string? CommitPendingEdits()
    {
        switch (Mode)
        {
            case EditorMode.Author:
                Author.FlushPendingEdits();
                return null;
            case EditorMode.Source:
                return ApplySourceToModel();
            default:
                return null;
        }
    }

    public void RefreshPreview()
    {
        CommitPendingEdits();

        string html;
        try
        {
            html = new HtmlPublisher(_project).RenderPreview(Document);
        }
        catch (Exception ex)
        {
            html = "<html><body style='font-family:Segoe UI'><p>Не удалось построить предпросмотр:</p><pre>" +
                   System.Net.WebUtility.HtmlEncode(ex.Message) + "</pre></body></html>";
        }

        try
        {
            _previewFile = Path.Combine(Path.GetTempPath(), "DitaStudioPreview",
                (Document.FilePath is null ? "preview" : Path.GetFileNameWithoutExtension(Document.FilePath)) + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(_previewFile)!);
            File.WriteAllText(_previewFile, html, System.Text.Encoding.UTF8);

            var uri = new Uri(_previewFile);
            if (_browser.Source == uri)
            {
                _browser.CoreWebView2?.Reload();
            }
            else
            {
                _browser.Source = uri;
            }
        }
        catch
        {
            // без предпросмотра редактор остаётся работоспособным
        }
    }

    private void OpenPreviewInBrowser()
    {
        RefreshPreview();
        if (_previewFile is null || !File.Exists(_previewFile))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_previewFile) { UseShellExecute = true });
    }

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
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void ReloadViews()
    {
        Author.Load(Document);
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

    private void RaiseDirty() => DirtyChanged?.Invoke(this, EventArgs.Empty);
}
