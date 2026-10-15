using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Desktop.Views;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.ViewModels;
using SkiaSharp;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Снимки окон для руководств пользователя (samples/DitaStudioBeginnerGuide и samples/DitaStudioGuide): настоящее главное окно на копии
/// samples/GuideSample, светлая тема, пометки номерами поверх снимка. Обычный прогон тестов их не трогает — снимки пересобираются
/// только с переменной окружения <c>DITASTUDIO_GUIDE_SHOTS=1</c>:
/// <code>$env:DITASTUDIO_GUIDE_SHOTS=1; dotnet test tests/DitaStudio.Desktop.Tests --filter GuideScreenshots</code>
/// </summary>
public sealed class GuideScreenshots : IDisposable
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DITASTUDIO_GUIDE_SHOTS") == "1";

    /// <summary>В какое руководство кладётся снимок.</summary>
    [Flags]
    private enum Guide
    {
        Beginner = 1,
        Expert = 2,
        Both = Beginner | Expert,
        Plugins = 4
    }

    private readonly string _project;
    private readonly string? _recentBackup;
    private readonly string _recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public GuideScreenshots()
    {
        _recentBackup = File.Exists(_recentPath) ? File.ReadAllText(_recentPath) : null;
        // Короткий понятный путь — он виден в окнах (папка нового документа); не получилось — временная папка.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var parent = Path.Combine(root, "Документация");
        if (Directory.Exists(parent))
        {
            parent = Path.Combine(root, "Документация-" + Guid.NewGuid().ToString("N")[..6]);
        }

        _project = Path.Combine(parent, "Пример");
        try
        {
            Directory.CreateDirectory(_project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _project = Path.Combine(Path.GetTempPath(), "DitaStudioGuideShots", Guid.NewGuid().ToString("N"), "Пример");
        }

        CopyDirectory(Path.Combine(RepositoryRoot(), "samples", "GuideSample"), _project);
    }

    public void Dispose()
    {
        try
        {
            if (_recentBackup is null)
            {
                File.Delete(_recentPath);
            }
            else
            {
                File.WriteAllText(_recentPath, _recentBackup);
            }

            Directory.Delete(Path.GetDirectoryName(_project)!, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // временные файлы удалятся системой
        }
    }

    // ------------------------------------------------------------------ помощники

    private async Task<(MainWindow Window, MainViewModel Vm)> OpenAsync(double width = 1280, double height = 900)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new MainWindow { Width = width, Height = height };
        window.Show();
        await window.ViewModel.ProjectPanel.LoadProjectAsync(_project);
        Dispatcher.UIThread.RunJobs();
        return (window, window.ViewModel);
    }

    private static void Close(Window window)
    {
        foreach (var owned in window.OwnedWindows.ToList())
        {
            owned.Close();
        }

        window.Close();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    private DocumentView Open(MainViewModel vm, string relative)
    {
        var pane = (DocumentView)vm.OpenDocument!(Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar)))!;
        Dispatcher.UIThread.RunJobs();
        return pane;
    }

    /// <summary>Прямоугольник элемента в координатах окна (для пометок и обрезки).</summary>
    private static Rect Bounds(Visual visual, Visual window, double inflate = 0)
    {
        var origin = visual.TranslatePoint(new Point(0, 0), window) ?? default;
        return new Rect(origin, visual.Bounds.Size).Inflate(inflate);
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

    private static TabItem Tab(Window window, string header) =>
        window.GetLogicalDescendants().OfType<TabItem>().First(t => t.Header as string == header);

    private static void SelectTab(Window window, string header)
    {
        var tab = Tab(window, header);
        ((TabControl)tab.Parent!).SelectedItem = tab;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Сохраняет снимок окна в руководства: пометки — красные рамки с номером в кружке, <paramref name="crop"/> — обрезка (в координатах окна).
    /// </summary>
    private static void Shot(TopLevel window, string name, Guide guide, IEnumerable<(Rect Area, string Label)>? marks = null, Rect? crop = null)
    {
        if (!Enabled)
        {
            return;
        }

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Кадр окна не отрисован");
        using var raw = new MemoryStream();
        frame.Save(raw);
        raw.Position = 0;
        using var source = SKBitmap.Decode(raw);
        var scale = source.Width / window.ClientSize.Width;

        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(source, 0, 0);
        using var stroke = new SKPaint { Color = new SKColor(0xD7, 0x26, 0x3D), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(3 * scale), IsAntialias = true };
        using var fill = new SKPaint { Color = new SKColor(0xD7, 0x26, 0x3D), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var text = new SKPaint
        {
            Color = SKColors.White, IsAntialias = true, TextSize = (float)(13 * scale), TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default
        };
        foreach (var (area, label) in marks ?? Array.Empty<(Rect, string)>())
        {
            var r = new SKRect((float)(area.X * scale), (float)(area.Y * scale), (float)(area.Right * scale), (float)(area.Bottom * scale));
            canvas.DrawRoundRect(r, (float)(6 * scale), (float)(6 * scale), stroke);
            // Номер — в правом верхнем углу области: там обычно пусто, заголовки вкладок и меню остаются видны.
            var radius = (float)(11 * scale);
            var small = r.Width < 70 * scale || r.Height < 40 * scale;
            // У маленькой области (кнопка, маркер) номер — снаружи, справа сверху, чтобы не закрыть её саму.
            var cx = small ? r.Right + radius : r.Right - radius - (float)(4 * scale);
            var cy = small ? r.Top - radius + (float)(6 * scale) : Math.Min(r.Top + radius + (float)(3 * scale), r.MidY);
            cx = Math.Clamp(cx, radius + 2, source.Width - radius - 2);
            cy = Math.Clamp(cy, radius + 2, source.Height - radius - 2);
            canvas.DrawCircle(cx, cy, radius, fill);
            canvas.DrawText(label, cx, cy + text.TextSize * 0.35f, text);
        }

        using var image = surface.Snapshot();
        using var cropped = crop is { } c
            ? image.Subset(SKRectI.Round(new SKRect((float)(c.X * scale), (float)(c.Y * scale), (float)(c.Right * scale), (float)(c.Bottom * scale))))
            : image;
        using var data = cropped.Encode(SKEncodedImageFormat.Png, 100);
        foreach (var folder in Folders(guide))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), data.ToArray());
        }
    }

    private static IEnumerable<string> Folders(Guide guide)
    {
        var samples = Path.Combine(RepositoryRoot(), "samples");
        if (guide.HasFlag(Guide.Beginner))
        {
            yield return Path.Combine(samples, "DitaStudioBeginnerGuide", "images", "screens");
        }

        if (guide.HasFlag(Guide.Expert))
        {
            yield return Path.Combine(samples, "DitaStudioGuide", "images", "screens");
        }

        if (guide.HasFlag(Guide.Plugins))
        {
            yield return Path.Combine(samples, "DitaStudioPluginGuide", "images", "screens");
        }
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonIn(Visual root, string content) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == content);

    // ------------------------------------------------------------------ снимки

    /// <summary>Главное окно с открытым проектом и топиком, области пронумерованы.</summary>
    [AvaloniaFact]
    public async Task MainWindowOverview()
    {
        if (!Enabled)
        {
            return;
        }

        var (window, vm) = await OpenAsync();
        var pane = Open(vm, "concepts/about.dita");
        var p = pane.Document.Root.Descendants().First(n => n.Name == "p");
        pane.AuthorEditor.EditorFor(p)!.FocusEditor(4);
        Dispatcher.UIThread.RunJobs();

        var menu = window.GetLogicalDescendants().OfType<Menu>().First();
        var toolbar = Named<WrapPanel>(window, "MainToolbar");
        var leftTabs = window.GetLogicalDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => i.Header as string == "Проект"));
        var docTabs = pane.GetLogicalAncestors().OfType<TabControl>().FirstOrDefault() as Control ?? pane;
        var modeTabs = pane.GetLogicalDescendants().OfType<TabControl>().First();
        var rightTabs = Named<TabControl>(window, "RightTabs");
        var bottomTabs = window.GetLogicalDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => i.Header as string == "Проверка"));
        var status = window.GetLogicalDescendants().OfType<Border>().First(b => b.Classes.Contains("statusbar"));
        Shot(window, "main-window", Guide.Both, new[]
        {
            (Bounds(menu, window, -1), "1"),
            (Bounds(toolbar, window, -1), "2"),
            (Bounds(leftTabs, window, -2), "3"),
            (Bounds(modeTabs, window, -2), "4"),
            (Bounds(rightTabs, window, -2), "5"),
            (Bounds(bottomTabs, window, -2), "6"),
            (Bounds(status, window, -1), "7")
        });
        _ = docTabs;
        Close(window);
    }

    /// <summary>Создание проекта: новая папка и новый документ.</summary>
    [AvaloniaFact]
    public async Task ProjectDialogs()
    {
        if (!Enabled)
        {
            return;
        }

        var (window, vm) = await OpenAsync();
        var panel = vm.ProjectPanel;
        panel.SelectedTreeNode = panel.Tree.First();
        var folder = panel.NewFolderCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        dialog.GetLogicalDescendants().OfType<TextBox>().First().Text = "tasks/setup";
        Shot(dialog, "new-folder-dialog", Guide.Both);
        dialog.Close();
        await folder;

        var creating = panel.NewDocumentCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        dialog = Assert.Single(window.OwnedWindows);
        var title = dialog.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
        if (title is not null)
        {
            title.Text = "Подключение датчика";
        }

        Shot(dialog, "new-document-dialog", Guide.Both);
        dialog.Close();
        await creating;

        // Кнопки над деревом проекта.
        var tree = Named<TreeView>(window, "ProjectTree");
        var buttons = tree.Parent!.GetLogicalDescendants().OfType<StackPanel>().First(sp => sp.Children.OfType<Button>().Count() >= 3);
        // Рамка — по самим кнопкам, а не по всей строке: номер встаёт справа от них и не уходит за край снимка.
        var buttonArea = buttons.Children.OfType<Button>().Select(b => Bounds(b, window)).Aggregate((a, b) => a.Union(b)).Inflate(3);
        Shot(window, "project-tree", Guide.Both, new[] { (buttonArea, "1"), (Bounds(tree, window, -2), "2") },
            crop: new Rect(0, 110, 300, 420));
        Close(window);
    }

    /// <summary>Режим «Автор»: концепция и задача, подсказка Enter, кнопки оформления, исходный код.</summary>
    [AvaloniaFact]
    public async Task AuthorMode()
    {
        if (!Enabled)
        {
            return;
        }

        var (window, vm) = await OpenAsync();
        var about = Open(vm, "concepts/about.dita");
        Shot(window, "author-concept", Guide.Both, crop: Bounds(about, window));

        // Кнопки оформления: курсор в полужирном тексте — «Ж» нажата.
        var li = about.Document.Root.Descendants().First(n => n.Name == "li");
        about.AuthorEditor.EditorFor(li)!.FocusEditor(3);
        Dispatcher.UIThread.RunJobs();
        var toolbar = Named<WrapPanel>(window, "MainToolbar");
        var bold = Named<Button>(window, "BoldButton");
        var code = Named<Button>(window, "CodeButton");
        Shot(window, "format-buttons", Guide.Both, new[] { (Bounds(bold, window, 2), "1"), (Bounds(code, window, 2), "2") },
            crop: new Rect(0, 28, Math.Min(760, Bounds(toolbar, window).Width), 62));

        // Задача: шаги, подсказка по Enter в конце шага.
        var install = Open(vm, "tasks/install.dita");
        var lastCmd = install.Document.Root.Descendants().Last(n => n.Name == "cmd");
        var cmdEditor = install.AuthorEditor.EditorFor(lastCmd)!;
        cmdEditor.FocusEditor(cmdEditor.Document.TextLength);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "enter-suggestions", Guide.Both);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        var firstStep = install.Document.Root.Descendants().First(n => n.Name == "cmd");
        install.AuthorEditor.EditorFor(firstStep)!.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "author-task", Guide.Both, crop: Bounds(install, window));

        // Исходный код того же топика.
        install.Mode = Presentation.Services.EditorMode.Source;
        Dispatcher.UIThread.RunJobs();
        Shot(window, "source-mode", Guide.Both, crop: Bounds(install, window));
        install.Mode = Presentation.Services.EditorMode.Author;
        Dispatcher.UIThread.RunJobs();
        Close(window);
    }

    /// <summary>Таблицы и картинки: окно вставки таблицы, меню ячейки, выделенные ячейки, свойства изображения.</summary>
    [AvaloniaFact]
    public async Task TablesAndImages()
    {
        if (!Enabled)
        {
            return;
        }

        var (window, vm) = await OpenAsync();
        var inserting = vm.Dialogs.InsertTableAsync();
        Dispatcher.UIThread.RunJobs();
        Shot(Assert.Single(window.OwnedWindows), "insert-table-dialog", Guide.Both);
        window.OwnedWindows.Single().Close();
        await inserting;

        var settings = Open(vm, "reference/settings.dita");
        var entry = settings.Document.Root.Descendants().First(n => n.Name == "entry" && n.Parent?.Parent?.Name == "tbody");
        var cell = settings.AuthorEditor.EditorFor(entry)!;
        cell.FocusEditor(0);
        Dispatcher.UIThread.RunJobs();
        cell.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        var at = cell.TranslatePoint(new Point(12, 8), window)!.Value;
        window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "cell-menu", Guide.Both);
        AuthorMenus.Close();
        Dispatcher.UIThread.RunJobs();

        var table = settings.Document.Root.Descendants().First(n => n.Name == "table");
        settings.AuthorEditor.SelectCellRange(table, new Core.Editing.CellRange(1, 2, 0, 1));
        Dispatcher.UIThread.RunJobs();
        // Таблица — в верхней части экрана: прокрутка так, чтобы над ней оставалось немного текста.
        var scroller = cell.GetVisualAncestors().OfType<ScrollViewer>().First();
        var cellTop = cell.TranslatePoint(new Point(0, 0), (Visual)scroller.Content!)!.Value.Y;
        scroller.Offset = new Vector(0, Math.Max(0, cellTop - 160));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Shot(window, "cells-selected", Guide.Both, crop: Bounds(settings, window).WithHeight(420));
        settings.AuthorEditor.ClearCellSelection();

        // Картинка в абзаце и окно её свойств.
        var picture = Path.Combine(_project, "concepts", "scheme.png");
        using (var bitmap = new SKBitmap(240, 110))
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(0x1F, 0x5F, 0xA9), IsAntialias = true })
        using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) })
        {
            canvas.Clear(new SKColor(0xE7, 0xEF, 0xF9));
            canvas.DrawRoundRect(new SKRect(12, 18, 228, 92), 12, 12, paint);
            canvas.DrawText("Схема", 78, 66, white);
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(picture, data.ToArray());
        }

        var about = Open(vm, "concepts/about.dita");
        var p = about.Document.Root.Descendants().Where(n => n.Name == "p").ElementAt(1);
        about.AuthorEditor.EditorFor(p)!.FocusEditor(p.InnerText.Length);
        Dispatcher.UIThread.RunJobs();
        vm.Insert.InsertImageFile(picture, numbered: false);
        Dispatcher.UIThread.RunJobs();
        var image = about.AuthorEditor.GetVisualDescendants().OfType<ResizableImage>().First();
        image.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        window.MouseMove(image.TranslatePoint(new Point(10, 10), window)!.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "image-resize", Guide.Both, new[] { (Bounds(image.Children.OfType<Border>().Single(), window, 4), "1") }, crop: new Rect(300, 110, 660, 500));
        var host = image.GetVisualAncestors().OfType<BlockEditor>().First();
        var chip = host.Content.ChipAt(host.Document.Text.IndexOf(Presentation.Authoring.InlineContent.ChipChar))!;
        about.AuthorEditor.EditChip(chip, image, host);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "image-properties", Guide.Both, crop: new Rect(300, 110, 660, 600));
        about.AuthorEditor.ChipEditor?.Cancel();
        Close(window);
    }

    /// <summary>Карта, ключи, проверка, поиск, правая панель (атрибуты, вставка, структура, правки).</summary>
    [AvaloniaFact]
    public async Task PanelsAndChecks()
    {
        if (!Enabled)
        {
            return;
        }

        // Ошибка для панели «Проверка» и правки для панели «Правки».
        var noStart = Path.Combine(_project, "troubleshooting", "no-start.dita");
        var text = File.ReadAllText(noStart);
        var at = text.IndexOf("<p>", StringComparison.Ordinal);
        File.WriteAllText(noStart, text[..at]
            + "<p status=\"new\">Если программа всё равно не запускается, переустановите её.</p>"
            + "<p status=\"changed\">Подробности — в журнале <filepath>logs/app.log</filepath>.</p>"
            + "<p>Удаление программы описано в <xref href=\"../tasks/uninstall.dita\"/>.</p>" + text[at..]);
        var aboutPath = Path.Combine(_project, "concepts", "about.dita");
        var aboutText = File.ReadAllText(aboutPath);
        var pAt = aboutText.LastIndexOf("<p>", StringComparison.Ordinal);
        File.WriteAllText(aboutPath, aboutText[..pAt] + "<p status=\"deleted\">Устаревший абзац о старой версии.</p>" + aboutText[pAt..]);

        var (window, vm) = await OpenAsync();
        var about = Open(vm, "concepts/about.dita");

        SelectTab(window, "Карта");
        Shot(window, "map-tab", Guide.Both, crop: new Rect(0, 110, 300, 420));
        SelectTab(window, "Ключи");
        Shot(window, "keys-tab", Guide.Both, crop: new Rect(0, 110, 300, 400));
        SelectTab(window, "Проект");

        // Нижняя панель повыше — в списках проверки и поиска видно больше строк.
        var bottom = Named<TabControl>(window, "BottomTabs");
        ((Grid)bottom.Parent!).RowDefinitions[2].Height = new GridLength(260);
        vm.Validation.ValidateProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "validation", Guide.Both, crop: Bounds(bottom, window));

        vm.Search.SearchText = "установ";
        vm.Search.RunCommand.Execute(null);
        SelectTab(window, "Поиск");
        Shot(window, "search", Guide.Both, crop: Bounds(bottom, window));
        SelectTab(window, "Проверка");

        var p = about.Document.Root.Descendants().First(n => n.Name == "p");
        about.AuthorEditor.EditorFor(p)!.FocusEditor(3);
        Dispatcher.UIThread.RunJobs();
        var right = Named<TabControl>(window, "RightTabs");
        var rightArea = Bounds(right, window);
        Shot(window, "attributes-panel", Guide.Both, crop: rightArea.WithHeight(200));
        SelectTab(window, "Вставка");
        Shot(window, "insert-palette", Guide.Both, crop: rightArea.WithHeight(520));
        SelectTab(window, "Структура");
        Shot(window, "structure-panel", Guide.Both, crop: rightArea.WithHeight(450));
        SelectTab(window, "Правки");
        Shot(window, "changes-panel", Guide.Both, crop: rightArea.WithHeight(260));

        // Цветные пометки правок в «Авторе».
        var deleted = about.Document.Root.Descendants().First(n => n.GetAttribute("status") == "deleted");
        var deletedEditor = about.AuthorEditor.EditorFor(deleted)!;
        deletedEditor.FocusEditor(0);
        deletedEditor.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Shot(window, "status-notes", Guide.Expert, crop: Bounds(about, window));
        Close(window);
    }

    /// <summary>
    /// Руководство по плагинам: окно «Справка → Плагины…» (без ошибок и с ошибкой загрузки), замечания правил из плагина на вкладке
    /// «Проверка», выбор команды «Автора» и формата публикации. Плагины — пример samples/plugins/DitaStudio.SamplePlugins.
    /// </summary>
    [AvaloniaFact]
    public async Task PluginDialogs()
    {
        if (!Enabled)
        {
            return;
        }

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var built = Path.Combine(RepositoryRoot(), "samples", "plugins", "DitaStudio.SamplePlugins", "bin", configuration, "net8.0", "DitaStudio.SamplePlugins.dll");
        var plugins = Path.Combine(Path.GetTempPath(), "DitaStudioGuideShots", Guid.NewGuid().ToString("N"), "plugins");
        Directory.CreateDirectory(Path.Combine(plugins, "DitaStudio.SamplePlugins"));
        File.Copy(built, Path.Combine(plugins, "DitaStudio.SamplePlugins", "DitaStudio.SamplePlugins.dll"));
        const string shownFolder = @"C:\Programs\DitaStudio\plugins"; // в окне — папка установленной программы, а не временная

        // Текст для правил примера: длинная команда шага и «просто».
        var install = Path.Combine(_project, "tasks", "install.dita");
        File.WriteAllText(install, File.ReadAllText(install)
            .Replace("<cmd>Запустите", "<cmd>Это просто: запустите")
            .Replace("<p>Продукт установлен", "<p>" + string.Join(' ', Enumerable.Repeat("Длинный текст абзаца,", 22)) + " продукт установлен"));

        PluginRegistry.Load(plugins);
        try
        {
            var (window, vm) = await OpenAsync();

            async Task DialogShot(Func<Task> show, string name)
            {
                var task = show();
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(window.OwnedWindows);
                Dispatcher.UIThread.RunJobs();
                var replaced = 0;
                foreach (var text in dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text?.Contains(plugins, StringComparison.OrdinalIgnoreCase) == true))
                {
                    text.Text = text.Text!.Replace(plugins, shownFolder, StringComparison.OrdinalIgnoreCase);
                    replaced++;
                }

                Assert.True(replaced > 0 || name.Contains("picker"), "временный путь к плагинам в окне подменён");
                // Окно подбирает высоту под текст: после подмены пути — заново.
                dialog.SizeToContent = SizeToContent.Manual;
                dialog.SizeToContent = SizeToContent.Height;
                Dispatcher.UIThread.RunJobs();
                Shot(dialog, name, Guide.Plugins);
                dialog.Close();
                Dispatcher.UIThread.RunJobs();
                await task;
            }

            await DialogShot(() => vm.Help.ShowPluginsCommand.ExecuteAsync(null), "plugins-dialog");

            // Проверка проекта: замечания правил из плагина вместе со встроенными.
            var bottom = Named<TabControl>(window, "BottomTabs");
            ((Grid)bottom.Parent!).RowDefinitions[2].Height = new GridLength(260);
            vm.Validation.ValidateProjectCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Shot(window, "plugin-validation", Guide.Plugins, crop: Bounds(bottom, window));

            Open(vm, "tasks/install.dita");
            await DialogShot(() => vm.Insert.RunAuthorCommandPluginCommand.ExecuteAsync(null), "plugin-command-picker");
            await DialogShot(() => vm.Publish.PublishWithPluginCommand.ExecuteAsync(null), "plugin-format-picker");

            // Ошибка загрузки: в папке — .dll, которая не является сборкой .NET.
            File.WriteAllText(Path.Combine(plugins, "OldPlugin.dll"), "не сборка");
            PluginRegistry.Load(plugins);
            await DialogShot(() => vm.Help.ShowPluginsCommand.ExecuteAsync(null), "plugins-dialog-error");

            foreach (var pane in vm.Documents.Panes.Values)
            {
                pane.Document.IsDirty = false;
            }

            Close(window);
        }
        finally
        {
            PluginRegistry.Load(Path.Combine(plugins, "..", "none")); // остальные тесты — без плагинов
        }
    }

    /// <summary>Публикация: меню, условия, CSS, оформление DOCX, параметры страницы, XLIFF.</summary>
    [AvaloniaFact]
    public async Task PublishingDialogs()
    {
        if (!Enabled)
        {
            return;
        }

        // Продукты и условные атрибуты — чтобы окно «Условия сборки» было не пустым.
        File.WriteAllText(Path.Combine(_project, ".ditastudio-products"),
            Core.Project.ProductList.ToJson(new[] { new Core.Project.ProductInfo("Стандарт"), new Core.Project.ProductInfo("Профи") }));
        var install = Path.Combine(_project, "tasks", "install.dita");
        var text = File.ReadAllText(install);
        text = text.Replace("<p>Понадобятся права администратора", "<p audience=\"администратор\" platform=\"windows\">Понадобятся права администратора");
        var lastStep = text.LastIndexOf("<step>", StringComparison.Ordinal);
        text = text[..lastStep] + "<step product=\"Профи\">" + text[(lastStep + "<step>".Length)..];
        File.WriteAllText(install, text);

        var (window, vm) = await OpenAsync();
        var publish =window.GetLogicalDescendants().OfType<MenuItem>().First(m => (m.Header as string)?.Replace("_", string.Empty) == "Публикация");
        publish.Open();
        Dispatcher.UIThread.RunJobs();
        Shot(window, "publish-menu", Guide.Both, crop: new Rect(0, 0, 760, 640));
        publish.Close();
        Dispatcher.UIThread.RunJobs();

        async Task DialogShot(Task task, string name, Guide guide)
        {
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            Shot(dialog, name, guide);
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            await task;
        }

        await DialogShot(vm.Dialogs.PublishConditionsAsync(vm.Project!, null), "conditions-dialog", Guide.Both);
        await DialogShot(vm.Dialogs.CustomCssAsync(vm.Project!), "css-dialog", Guide.Expert);
        await DialogShot(vm.Dialogs.DocxLayoutSettingsAsync(vm.Project!), "docx-layout-dialog", Guide.Both);
        await DialogShot(vm.Dialogs.PageSetupAsync(vm.Project!), "page-setup-dialog", Guide.Both);
        await DialogShot(vm.Dialogs.PickXliffLanguagesAsync("ru-RU", "en"), "xliff-languages-dialog", Guide.Expert);
        Close(window);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

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
