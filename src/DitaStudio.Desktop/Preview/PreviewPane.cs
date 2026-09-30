using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Services;
using Xilium.CefGlue.Avalonia;

namespace DitaStudio.Desktop.Preview;

/// <summary>Как показывать вкладку «Предпросмотр» — под какой из форматов публикации.</summary>
public enum PreviewFormat
{
    Html,
    Pdf,

    /// <summary>Приближённая имитация вида DOCX через CSS (<see cref="Assets.WordPreviewCss"/>) —
    /// не настоящий .docx: реальную пагинацию, сноски и разрывы страниц показывает только экспорт.</summary>
    DocxApprox,

    /// <summary>«Живой» постраничный вид: листы размера из «Параметров страницы», текст разбивается на листы
    /// прямо в браузере (<see cref="PagedPreview"/>) и пересчитывается при каждой правке. Для карты — обычный HTML.</summary>
    Pages
}

/// <summary>
/// Вкладка «Предпросмотр» — как в WPF DocumentPane: HTML как есть, PDF — настоящая печать
/// встроенным Chromium (<see cref="CefPdfPrinter"/>, с колонтитулами проекта) во временный
/// файл, который показывает встроенный просмотрщик PDF того же Chromium, DOCX — тот же HTML
/// со скином Word. Без Chromium (не запустился, headless-тесты) — файл открывается внешним
/// приложением по кнопке.
/// </summary>
public sealed class PreviewPane : UserControl
{
    private readonly DitaProject _project;
    private readonly DitaDocument _document;
    private readonly Func<string?> _commitPendingEdits;
    private readonly IPdfPrinter _pdfPrinter;
    private readonly ComboBox _format;
    private readonly Button _external;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4, 4, 4), FontSize = 12 };
    private readonly Border _host = new();
    private HostedCefBrowser? _browser;
    private int _requestId;
    private DispatcherTimer? _liveTimer;
    private string? _pagedFile;

    public PreviewPane(DitaProject project, DitaDocument document, Func<string?> commitPendingEdits, IPdfPrinter pdfPrinter)
    {
        _project = project;
        _document = document;
        _commitPendingEdits = commitPendingEdits;
        _pdfPrinter = pdfPrinter;
        _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("TextMuted"));

        _format = new ComboBox
        {
            ItemsSource = new[] { "HTML", "PDF", "DOCX (приближённо)", "Страницы (живой)" },
            SelectedIndex = 0,
            Margin = new Thickness(6, 4, 4, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        _format.SelectionChanged += (_, _) => _ = RefreshAsync();

        var refresh = new Button { Content = "Обновить", Margin = new Thickness(4) };
        refresh.Click += (_, _) => _ = RefreshAsync();

        var external = _external = new Button { Content = "Открыть внешним приложением", Margin = new Thickness(0, 4, 4, 4) };
        external.Click += async (_, _) =>
        {
            if (await RefreshAsync() is { } file)
            {
                OpenExternally(file);
            }
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Children = { _format, refresh, external, _status } };
        var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        barBorder.Bind(Border.BackgroundProperty, barBorder.GetResourceObservable("SurfaceAlt"));
        barBorder.Bind(Border.BorderBrushProperty, barBorder.GetResourceObservable("Line"));
        DockPanel.SetDock(barBorder, Dock.Top);

        Content = new DockPanel { Children = { barBorder, _host } };
    }

    /// <summary>Окно «рядом с текстом»: только «живые» страницы, без выбора формата и внешнего приложения.</summary>
    public void MakeLive()
    {
        Format = PreviewFormat.Pages;
        _format.IsVisible = false;
        _external.IsVisible = false;
    }

    /// <summary>Что показывать в «живом» режиме (например, разобранный исходный код, ещё не перенесённый в модель); null — сам документ.</summary>
    public Func<DitaDocument?>? LiveDocument { get; set; }

    /// <summary>Записать несохранённый текст «Автора» в модель на время пересчёта (без закрытия правки); null — ничего.</summary>
    public Action? LiveCommit { get; set; }

    private bool IsMap => _document.Root.Name is "map" or "bookmap" && _document.FilePath is not null;

    public PreviewFormat Format
    {
        get => (PreviewFormat)Math.Max(0, _format.SelectedIndex);
        set => _format.SelectedIndex = (int)value;
    }

    /// <summary>Текст строки состояния (ошибки печати, «Печать в PDF…»).</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>Файл, показанный последним (для «Открыть внешним приложением» и тестов).</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>
    /// Строит предпросмотр в выбранном формате и показывает его. Возвращает показанный файл.
    /// Номер запроса защищает от гонки: если формат сменили, пока PDF ещё печатался,
    /// устаревший результат не затирает более новый.
    /// </summary>
    public async Task<string?> RefreshAsync()
    {
        var requestId = ++_requestId;
        var format = Format;
        _commitPendingEdits();

        if (format == PreviewFormat.Pages && !IsMap)
        {
            return await ShowPagesAsync();
        }

        _pagedFile = null;
        string html;
        string? mapEntry = null;
        var extraCss = format == PreviewFormat.DocxApprox ? Assets.WordPreviewCss : null;
        try
        {
            if (IsMap)
            {
                // Карта: текст всего издания (а не список ссылок) — собранный так же, как единый HTML.
                var mapDir = Path.Combine(Path.GetTempPath(), "DitaStudioPreview", PreviewFileBaseName(_document.FilePath) + "-map");
                try
                {
                    if (Directory.Exists(mapDir))
                    {
                        Directory.Delete(mapDir, true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // прежние файлы заняты просмотрщиком — новая сборка перезапишет их сверху
                }

                mapEntry = new HtmlPublisher(_project).RenderMapPreview(_document.FilePath!, mapDir);
                html = File.ReadAllText(mapEntry);
                if (extraCss is not null)
                {
                    html = html.Replace("</head>", "<style>\n" + extraCss + "\n</style>\n</head>");
                }
            }
            else
            {
                html = new HtmlPublisher(_project).RenderPreview(_document, extraCss: extraCss);
            }
        }
        catch (Exception ex)
        {
            html = "<html><body style='font-family:sans-serif'><p>Не удалось построить предпросмотр:</p><pre>" +
                   System.Net.WebUtility.HtmlEncode(ex.Message) + "</pre></body></html>";
            mapEntry = null;
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "DitaStudioPreview");
            Directory.CreateDirectory(dir);
            var baseName = PreviewFileBaseName(_document.FilePath);
            // Предпросмотр карты лежит рядом со своей папкой media — картинки подхватываются относительными путями.
            var htmlPath = mapEntry ?? Path.Combine(dir, baseName + ".html");
            await File.WriteAllTextAsync(htmlPath, html, Encoding.UTF8);

            var shown = htmlPath;
            if (format == PreviewFormat.Pdf)
            {
                _status.Text = "Печать в PDF…";
                var pdfPath = Path.Combine(dir, baseName + ".pdf");
                var error = await _pdfPrinter.ExportAsync(htmlPath, pdfPath, PdfPageDecoration.For(_project));
                if (requestId != _requestId)
                {
                    return CurrentFile;
                }

                if (error is null)
                {
                    shown = pdfPath;
                    _status.Text = string.Empty;
                }
                else
                {
                    _status.Text = "Ошибка печати в PDF: " + error;
                }
            }
            else
            {
                _status.Text = string.Empty;
            }

            CurrentFile = shown;
            Show(shown);
            return shown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Не удалось записать временный файл предпросмотра: " + ex.Message;
            return null;
        }
    }

    /// <summary>Интервал между правкой и пересчётом листов в «живом» режиме.</summary>
    public static readonly TimeSpan LiveDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Показать «живой» постраничный вид: страница с листами публикуется один раз, дальше текст подменяется скриптом.</summary>
    private async Task<string?> ShowPagesAsync()
    {
        try
        {
            var (body, css) = new HtmlPublisher(_project).RenderPagedParts(_document);
            var layout = _project.DocxLayout;
            var header = _project.PdfShowHeaderFooter ? _project.PdfHeaderText : layout.HeaderText;
            var footer = _project.PdfShowHeaderFooter ? _project.PdfFooterText : layout.FooterText;
            var html = PagedPreview.Build(PageGeometry.For(layout), css, _document.Title, header ?? string.Empty, footer ?? string.Empty, body);

            var dir = Path.Combine(Path.GetTempPath(), "DitaStudioPreview");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, PreviewFileBaseName(_document.FilePath) + "-pages.html");
            await File.WriteAllTextAsync(path, html, Encoding.UTF8);
            _status.Text = string.Empty;
            _pagedFile = path;
            CurrentFile = path;
            Show(path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Не удалось записать временный файл предпросмотра: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Документ изменился: в режиме «Страницы» через <see cref="LiveDelay"/> пересобирает текст и перекладывает его на листы
    /// без перезагрузки страницы (серия нажатий клавиш даёт один пересчёт). В других режимах ничего не делает.
    /// </summary>
    public void NotifyChanged()
    {
        if (_pagedFile is null || Format != PreviewFormat.Pages)
        {
            return;
        }

        _liveTimer ??= new DispatcherTimer { Interval = LiveDelay };
        _liveTimer.Tick -= OnLiveTick;
        _liveTimer.Tick += OnLiveTick;
        _liveTimer.Stop();
        _liveTimer.Start();
    }

    private void OnLiveTick(object? sender, EventArgs e)
    {
        _liveTimer?.Stop();
        UpdateLive();
    }

    /// <summary>Немедленный пересчёт «живого» вида (обновление текста в уже открытой странице).</summary>
    public bool UpdateLive()
    {
        if (_pagedFile is null || _browser is null)
        {
            return false;
        }

        try
        {
            LiveCommit?.Invoke();
            var (body, _) = new HtmlPublisher(_project).RenderPagedParts(LiveDocument?.Invoke() ?? _document);
            return _browser.RunScript(PagedPreview.SetContentCall(body));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _status.Text = "Не удалось обновить предпросмотр: " + ex.Message;
            return false;
        }
    }

    /// <summary>Подменяет текст листов готовым HTML (для проверок раскладки без модели документа).</summary>
    public bool UpdateLiveWith(string bodyHtml) => _browser?.RunScript(PagedPreview.SetContentCall(bodyHtml)) ?? false;

    /// <summary>Значение выражения JavaScript на показанной странице (для проверок); null — браузера нет.</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        if (_browser is null)
        {
            return null;
        }

        // Результат возвращается через заголовок страницы: EvaluateJavaScript у CefGlue.Avalonia значения не отдаёт.
        const string marker = "@@eval:";
        if (!_browser.RunScript("document.title = '" + marker + "' + String(" + expression + ");"))
        {
            return null;
        }

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
            if (_browser.Title is { } title && title.StartsWith(marker, StringComparison.Ordinal))
            {
                return title[marker.Length..];
            }
        }

        return null;
    }

    private void Show(string path)
    {
        if (EnsureBrowser() is { } browser)
        {
            var uri = new Uri(path).AbsoluteUri;
            if (browser.Address == uri)
            {
                browser.Reload(ignoreCache: true);
            }
            else
            {
                browser.Address = uri;
            }

            return;
        }

        // Без встроенного браузера — подсказка и кнопка открыть файл снаружи.
        var open = new Button { Content = "Открыть " + Path.GetFileName(path), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => OpenExternally(path);
        var note = new TextBlock
        {
            Text = "Встроенный браузер недоступен (" + (CefHost.EnsureStarted() ?? "неизвестная причина") +
                   "). Предпросмотр построен — его можно открыть внешним приложением.",
            TextWrapping = TextWrapping.Wrap
        };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("TextMuted"));
        _host.Child = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children = { note, open } };
    }

    private HostedCefBrowser? EnsureBrowser()
    {
        if (_browser is not null)
        {
            return _browser;
        }

        if (CefHost.EnsureStarted() is not null)
        {
            return null;
        }

        try
        {
            _browser = new HostedCefBrowser();
            _host.Child = _browser;
            return _browser;
        }
        catch (Exception)
        {
            _browser = null;
            return null;
        }
    }

    private static void OpenExternally(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // нет приложения для этого типа файла — предпросмотр остаётся во вкладке
        }
    }

    /// <summary>Имя временного файла: имя документа + короткий хэш полного пути — одноимённые
    /// документы из разных папок (или двух запущенных редакторов) не затирают друг друга.</summary>
    public static string PreviewFileBaseName(string? filePath)
    {
        if (filePath is null)
        {
            return "preview";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath).ToUpperInvariant()));
        return Path.GetFileNameWithoutExtension(filePath) + "-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Закрытие вкладки документа — браузер больше не нужен.</summary>
    public void DisposeBrowser()
    {
        _browser?.Dispose();
        _browser = null;
    }
}
