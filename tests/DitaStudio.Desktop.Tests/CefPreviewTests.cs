using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Desktop.Preview;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Настоящий Chromium (CefGlue): печать в PDF с колонтитулами. Нужен X-сервер, поэтому
/// раздел идёт только при DITASTUDIO_CEF_TESTS=1 (в CI — под xvfb-run); в обычном прогоне
/// тесты отмечаются как пройденные без действия.
/// </summary>
public sealed class CefPreviewTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DITASTUDIO_CEF_TESTS") == "1";

    [AvaloniaFact]
    public async Task PrintToPdf_WithHeaderAndFooter()
    {
        if (!Enabled)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "page.html");
        var pdf = Path.Combine(dir, "page.pdf");
        await File.WriteAllTextAsync(html,
            "<html><body><h1>Проверка печати</h1>" + string.Concat(Enumerable.Repeat("<p>Абзац текста для второй страницы.</p>", 120)) + "</body></html>");

        var error = await new CefPdfPrinter().ExportAsync(html, pdf, showHeaderFooter: true, headerText: "Шапка", footerText: "Подвал");

        Assert.Null(error);
        var bytes = await File.ReadAllBytesAsync(pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var pages = Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count;
        Assert.True(pages >= 2, $"ожидалось несколько страниц, получено {pages}");
    }

    [AvaloniaFact]
    public async Task PrintToPdf_WithLogoInHeader()
    {
        if (!Enabled)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var html = Path.Combine(dir, "page.html");
        var pdf = Path.Combine(dir, "page.pdf");
        await File.WriteAllTextAsync(html, "<html><body><h1>Логотип в шапке</h1><p>Текст без картинок.</p></body></html>");
        const string logo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        var error = await new CefPdfPrinter().ExportAsync(html, pdf,
            new DitaStudio.Presentation.Services.PdfPageDecoration(true, "Шапка", "Подвал", logo, Core.Publishing.DocxHeaderAlignment.Left, 10));

        Assert.Null(error);
        var text = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf));
        Assert.Matches(@"/Subtype\s*/Image", text);
    }

    [AvaloniaFact]
    public async Task PrintToPdf_PlacedBlockGetsItsOwnPage()
    {
        if (!Enabled)
        {
            return;
        }

        // Блок «внизу листа» — своя страница между текстом, блок в конце — последний лист без
        // пустого за ним: 4 листа вместо одного.
        var root = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.dita"),
            "<concept id=\"a\"><title>Глава</title><conbody><p>До</p><p outputclass=\"place-bottom-right\">Гриф</p><p>После</p><p outputclass=\"place-top-left\">Конец</p></conbody></concept>");
        await File.WriteAllTextAsync(Path.Combine(root, "guide.ditamap"), "<map><title>Книга</title><topicref href=\"a.dita\"/></map>");
        var project = new DitaProject(root);
        project.Scan();
        var single = new Core.Publishing.HtmlPublisher(project).Publish(Path.Combine(root, "guide.ditamap"),
            new Core.Publishing.PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
        var pdf = Path.Combine(root, "book.pdf");

        var error = await new CefPdfPrinter().ExportAsync(single.EntryFile, pdf, showHeaderFooter: false, headerText: null, footerText: null);

        Assert.Null(error);
        var pages = Regex.Matches(System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf)), @"/Type\s*/Page[^s]").Count;
        Assert.Equal(4, pages);
    }

    [AvaloniaTheory]
    [InlineData(PreviewFormat.Html)]
    [InlineData(PreviewFormat.Pdf)]
    [InlineData(PreviewFormat.DocxApprox)]
    public async Task PreviewPane_ShowsDocumentInEmbeddedBrowser(PreviewFormat format)
    {
        if (!Enabled)
        {
            return;
        }

        var root = Path.Combine(RepositoryRoot(), "samples", "GuideSample");
        var project = new DitaProject(root);
        project.Scan();
        var document = DitaDocument.Load(Path.Combine(root, "tasks", "install.dita"));
        var pane = new PreviewPane(project, document, () => null, new CefPdfPrinter());
        var window = new Window { Width = 1000, Height = 800, Content = pane };
        window.Show();

        pane.Format = format;
        var shown = await pane.RefreshAsync();

        Assert.Equal(string.Empty, pane.StatusText);
        Assert.EndsWith(format == PreviewFormat.Pdf ? ".pdf" : ".html", shown);

        // Даём Chromium загрузить и отрисовать страницу (PDF-просмотрщику — чуть дольше).
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, $"preview-{format.ToString().ToLowerInvariant()}.png"));
        pane.DisposeBrowser();
        window.Close();
    }

    /// <summary>Г6: настоящая раскладка на листы в Chromium — всё содержимое на месте один раз, листы не переполнены,
    /// заголовок не остаётся внизу листа одиноко, правка текста пересчитывает число листов без перезагрузки.</summary>
    [AvaloniaFact]
    public async Task LivePagedPreview_SplitsTextIntoSheets_AndRecalculatesOnChange()
    {
        if (!Enabled)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paragraphs = string.Concat(Enumerable.Range(1, 60).Select(i => $"<p>АБЗАЦ{i:000} " + string.Concat(Enumerable.Repeat("слово ", 40)) + "</p>"));
        var items = string.Concat(Enumerable.Range(1, 30).Select(i => $"<li>ПУНКТ{i:000}</li>"));
        var rows = string.Concat(Enumerable.Range(1, 40).Select(i => $"<row><entry>СТРОКА{i:000}</entry><entry>значение</entry></row>"));
        var file = Path.Combine(root, "long.dita");
        await File.WriteAllTextAsync(file,
            "<topic id=\"t\"><title>Длинный топик</title><body>" + paragraphs + "<ul>" + items + "</ul>" +
            "<table><tgroup cols=\"2\"><thead><row><entry>ШАПКА</entry><entry>Значение</entry></row></thead><tbody>" + rows + "</tbody></tgroup></table></body></topic>");
        var project = new DitaProject(root);
        project.Scan();
        project.DocxLayout.PaperSize = "A5";
        project.DocxLayout.FooterText = "{page} из {pages}";
        var document = DitaDocument.Load(file);
        var pane = new PreviewPane(project, document, () => null, new CefPdfPrinter());
        var window = new Window { Width = 1000, Height = 800, Content = pane };
        window.Show();
        pane.Format = PreviewFormat.Pages;
        await pane.RefreshAsync();

        string? _lastValue = null;
        async Task<int> WaitPagesAsync(Func<int, bool> ready)
        {
            for (var i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                var value = await pane.EvaluateAsync("document.documentElement.getAttribute('data-pages') || '0'");
                _lastValue = value;
                if (int.TryParse(value, out var pages) && ready(pages))
                {
                    return pages;
                }
            }

            return 0;
        }

        var before = await WaitPagesAsync(n => n > 1);
        Assert.True(before > 1, "текст должен лечь на несколько листов; data-pages=" + _lastValue + "; файл " + pane.CurrentFile + "; " + pane.StatusText);

        // Всё на месте ровно один раз: абзацы, пункты списка, строки таблицы.
        foreach (var marker in new[] { "АБЗАЦ001", "АБЗАЦ060", "ПУНКТ001", "ПУНКТ030", "СТРОКА001", "СТРОКА040" })
        {
            var count = await pane.EvaluateAsync($"document.getElementById('pages').innerText.split('{marker}').length - 1");
            Assert.Equal("1", count);
        }

        // Ни один лист не переполнен (каждый блок меньше листа).
        var overflowing = await pane.EvaluateAsync("Array.from(document.querySelectorAll('.sheet .content')).filter(c => c.scrollHeight > c.clientHeight + 1).length");
        Assert.Equal("0", overflowing);

        // Список и таблица режутся между пунктами/строками: на листах, где они продолжаются, оболочка ul/table повторяется.
        var tableSheets = await pane.EvaluateAsync("document.querySelectorAll('.sheet table').length");
        Assert.True(int.Parse(tableSheets!) >= 2, "таблица должна лечь на несколько листов");
        var headers = await pane.EvaluateAsync("document.getElementById('pages').innerText.split('ШАПКА').length - 1");
        Assert.Equal(tableSheets, headers); // шапка таблицы повторяется на каждом листе, как при печати

        // Колонтитул: поля {page} и {pages}.
        var footer = await pane.EvaluateAsync("document.querySelectorAll('.sheet .foot')[1].textContent");
        Assert.Equal($"2 из {before}", footer);

        // Правка текста — пересчёт без перезагрузки страницы: больше текста — больше листов, меньше — меньше.
        var longer = string.Concat(Enumerable.Repeat("<p>Дополнительный абзац. " + string.Concat(Enumerable.Repeat("текст ", 120)) + "</p>", 60));
        Assert.True(pane.UpdateLiveWith(longer));
        var more = await WaitPagesAsync(n => n > before + 1);
        Assert.True(more > before, $"после добавления текста листов должно стать больше ({before} → {more})");
        Assert.True(pane.UpdateLiveWith("<p>Коротко</p>"));
        var one = await WaitPagesAsync(n => n == 1);
        Assert.Equal(1, one);

        // Бюджет: топик из 500 абзацев раскладывается на листы быстро — пересчёт после каждой правки не мешает печатать.
        var big = PagedPreview_Html(500);
        var elapsed = await pane.EvaluateAsync("(function(){var t=performance.now();ditaSetContent(" + Core.Publishing.PagedPreview.JsString(big) + ");return Math.round(performance.now()-t);})()");
        Assert.True(int.Parse(elapsed!) < 1500, $"раскладка 500 абзацев заняла {elapsed} мс");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        pane.DisposeBrowser();
        window.Close();
    }

    /// <summary>Г6: «Предпросмотр рядом с текстом» в окне — редактор слева, листы справа; набор в «Авторе» доходит до листов.</summary>
    [AvaloniaFact]
    public async Task LivePagedPreview_SideBySide_TypingReachesSheets()
    {
        if (!Enabled)
        {
            return;
        }

        var root = Path.Combine(RepositoryRoot(), "samples", "GuideSample");
        var project = new DitaProject(root);
        project.Scan();
        var document = DitaDocument.Load(Path.Combine(root, "concepts", "about.dita"));
        var view = new Desktop.Views.DocumentView(project, document, new CefPdfPrinter());
        var window = new Window { Width = 1400, Height = 850, Content = view };
        window.Show();
        view.ShowLivePreview = true;
        var live = view.LivePreview!;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }

        // Правка текста абзаца в «Авторе» → через задержку новый текст в листах.
        var paragraph = document.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.InnerText.Length > 10);
        var editor = view.AuthorEditor.EditorFor(paragraph)!;
        editor.FocusEditor(0);
        editor.Text = "НАБРАННЫЙ_ТЕКСТ " + editor.Text;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }

        var found = await live.EvaluateAsync("document.getElementById('pages').innerText.indexOf('НАБРАННЫЙ_ТЕКСТ') >= 0");
        Assert.Equal("true", found);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, "live-preview-side-by-side.png"));
        view.Dispose();
        window.Close();
    }

    /// <summary>Образцы таблиц (подписи, границы) в опубликованных HTML, DOCX и PDF — складываются в screenshots для просмотра глазами.</summary>
    [AvaloniaFact]
    public async Task TableSamples_PublishedToHtmlDocxPdf()
    {
        if (!Enabled)
        {
            return;
        }

        static string Table(string title, string attrs = "", int rows = 2) =>
            "<table" + attrs + ">" + title + "<tgroup cols=\"3\"><thead><row><entry>Параметр</entry><entry>Тип</entry><entry>Описание</entry></row></thead><tbody>" +
            string.Concat(Enumerable.Range(1, rows).Select(i => $"<row><entry>поле{i}</entry><entry>число</entry><entry>Описание {i}</entry></row>")) + "</tbody></tgroup></table>";
        var root = Path.Combine(Path.GetTempPath(), "DitaStudioCefTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "t.dita"),
            "<concept id=\"t\"><title>Таблицы</title><conbody><p>Все границы, с названием:</p>" + Table("<title>Параметры изображения</title>") +
            "<p>Без границ, пустое название:</p>" + Table("<title/>", " frame=\"none\" rowsep=\"0\" colsep=\"0\"") +
            "<p>Только горизонтальные линии, без названия:</p>" + Table("", " frame=\"topbot\" colsep=\"0\"") +
            "<p>Убрана линия под строкой и справа от столбца:</p>" + Table("<title>Выборочные линии</title>", " rowsep=\"0\"") +
            "<p>Маркер (Д3): <ph outputclass=\"mark-yellow\">жёлтый</ph>, <ph outputclass=\"mark-green\">зелёный</ph>, <ph outputclass=\"mark-red\">красный</ph>, " +
            "<ph outputclass=\"mark-blue\">синий</ph>, свой <ph outputclass=\"mark-ff8800\">оранжевый</ph> и <ph outputclass=\"mark-000080\">тёмно-синий</ph>; " +
            "<ph outputclass=\"mark-yellow size-18\">крупный</ph> и <ph outputclass=\"mark-yellow color-red\">красный по жёлтому</ph>.</p>" +
            "<p>Границы по сторонам (Д11): без верха, внутренние вертикальные убраны:</p>" +
            "<table frame=\"sides\" outputclass=\"frame-bottom frame-left frame-right\" colsep=\"0\"><title>Границы по сторонам</title><tgroup cols=\"3\">" +
            "<colspec colname=\"c1\" colnum=\"1\"/><colspec colname=\"c2\" colnum=\"2\"/><colspec colname=\"c3\" colnum=\"3\"/><tbody>" +
            "<row><entry>А1</entry><entry>Б1</entry><entry>В1</entry></row><row><entry>А2</entry><entry rowsep=\"0\">Б2</entry><entry>В2</entry></row>" +
            "<row><entry>А3</entry><entry>Б3</entry><entry>В3</entry></row></tbody></tgroup></table>" +
            "<p>Блоки внутри ячеек (Д9):</p><table><tgroup cols=\"2\"><thead><row><entry>Что</entry><entry>Подробно</entry></row></thead><tbody>" +
            "<row><entry>Два абзаца</entry><entry><p>Первый абзац ячейки.</p><p>Второй абзац ячейки.</p></entry></row>" +
            "<row><entry>Абзац и список</entry><entry><p>Вводная фраза.</p><ul><li>Пункт один</li><li>Пункт два</li></ul></entry></row>" +
            "<row><entry>Текст и абзац</entry><entry>Текст прямо в ячейке<p>Абзац после текста.</p></entry></row>" +
            "<row><entry>Примечание</entry><entry><note>Заметка внутри ячейки.</note></entry></row></tbody></tgroup></table>" +
            "<p>Длинная таблица:</p>" + Table("<title>Длинная</title>", "", 50) + "</conbody></concept>");
        await File.WriteAllTextAsync(Path.Combine(root, "m.ditamap"), "<map><title>Образцы</title><topicref href=\"t.dita\"/></map>");
        var project = new DitaProject(root);
        project.Scan();
        var map = Path.Combine(root, "m.ditamap");
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);

        var html = new Core.Publishing.HtmlPublisher(project).Publish(map,
            new Core.Publishing.PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true });
        File.Copy(html.EntryFile, Path.Combine(dir, "tables.html"), true);
        var docx = Path.Combine(dir, "tables.docx");
        new Docx.DocxPublisher(project).Publish(map, new Core.Publishing.PublishOptions { Language = "ru" }, docx);
        var error = await new CefPdfPrinter().ExportAsync(html.EntryFile, Path.Combine(dir, "tables.pdf"), showHeaderFooter: false, headerText: null, footerText: null);
        Assert.Null(error);
    }

    private static string PagedPreview_Html(int paragraphs) =>
        string.Concat(Enumerable.Range(1, paragraphs).Select(i => $"<p>Абзац {i}. " + string.Concat(Enumerable.Repeat("слово ", 30)) + "</p>"));

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln).");
    }
}
