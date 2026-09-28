using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using DitaStudio.Desktop.Authoring;
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
/// AvaloniaEdit с автодополнением по каталогу; встроенный предпросмотр — этап 6 переноса
/// (пока — во внешнем браузере).
/// </summary>
public sealed class DocumentView : UserControl, IDocumentView
{
    private readonly DitaProject _project;
    private readonly TabControl _tabs = new();
    private readonly XmlSourceEditor _source = new();
    private readonly AuthorView _author = new();
    private bool _syncing;

    public DocumentView(DitaProject project, DitaDocument document)
    {
        _project = project;
        Document = document;
        _author.Load(document);
        _author.DocumentModified += (_, _) => RaiseDirty();
        _author.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _author.BeforeStructuralEdit += (_, description) => Undo.Push(Document, description);

        _source.TextEdited += (_, _) =>
        {
            if (!_syncing)
            {
                Document.IsDirty = true;
                RaiseDirty();
            }
        };

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Автор", Content = _author },
            new TabItem { Header = "Исходный код", Content = _source },
            new TabItem { Header = "Предпросмотр", Content = BuildPreviewPane() }
        };
        _tabs.SelectionChanged += OnTabChanged;
        Content = _tabs;
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

    public EditorMode Mode
    {
        get => _tabs.SelectedIndex switch { 1 => EditorMode.Source, 2 => EditorMode.Preview, _ => EditorMode.Author };
        set => _tabs.SelectedIndex = value switch { EditorMode.Source => 1, EditorMode.Preview => 2, _ => 0 };
    }

    internal void RaiseDirty() => DirtyChanged?.Invoke(this, EventArgs.Empty);

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
