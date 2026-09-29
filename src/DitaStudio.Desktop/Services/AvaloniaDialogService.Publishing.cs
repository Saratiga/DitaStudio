using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.IO;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

// Диалоги публикации: условия сборки, .ditaval, колонтитулы PDF, пользовательский CSS,
// оформление DOCX.
public sealed partial class AvaloniaDialogService
{
    private static readonly string[] ConditionAttributes = { "props", "platform", "product", "audience", "otherprops", "deliveryTarget" };

    private static Dictionary<string, HashSet<string>> CollectConditionValues(DitaProject project)
    {
        var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in project.Files)
        {
            if (project.TryGetDocument(file.FullPath) is not { } doc)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                foreach (var attribute in ConditionAttributes)
                {
                    var value = node.GetAttribute(attribute);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (!values.TryGetValue(attribute, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        values[attribute] = set;
                    }

                    foreach (var token in value!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        set.Add(token);
                    }
                }
            }
        }

        return values;
    }

    /// <summary>Флажки «исключить значение» по условным атрибутам проекта.</summary>
    private static List<(string Attribute, string Value, CheckBox Box)> ConditionChecks(
        StackPanel panel, DitaProject project, Func<string, string, bool> isExcluded)
    {
        var checks = new List<(string, string, CheckBox)>();
        foreach (var (attribute, set) in CollectConditionValues(project).OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            panel.Children.Add(Label($"@{attribute}"));
            foreach (var value in set.OrderBy(v => v, StringComparer.Ordinal))
            {
                var box = new CheckBox { Content = value, Margin = new Thickness(8, 2, 0, 2), IsChecked = isExcluded(attribute, value) };
                checks.Add((attribute, value, box));
                panel.Children.Add(box);
            }
        }

        if (checks.Count == 0)
        {
            panel.Children.Add(Muted(new TextBlock { Text = "В проекте нет условных атрибутов.", Margin = new Thickness(0, 4, 0, 0) }));
        }

        return checks;
    }

    private static Dictionary<string, HashSet<string>> CheckedExclusions(IEnumerable<(string Attribute, string Value, CheckBox Box)> checks)
    {
        var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (attribute, value, box) in checks.Where(c => c.Box.IsChecked == true))
        {
            if (!exclude.TryGetValue(attribute, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                exclude[attribute] = set;
            }

            set.Add(value);
        }

        return exclude;
    }

    public async Task<ConditionsResult?> PublishConditionsAsync(DitaProject project, ConditionsResult? current)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped("Отметьте значения, которые нужно исключить из сборки."));
        var checks = ConditionChecks(panel, project, (attribute, value) =>
            current is not null && current.Exclude.TryGetValue(attribute, out var excluded) && excluded.Contains(value));

        var drafts = new CheckBox
        {
            Content = "Включать черновые комментарии (draft-comment)",
            Margin = new Thickness(0, 14, 0, 0),
            IsChecked = current?.ShowDraftComments ?? false
        };
        panel.Children.Add(drafts);

        ConditionsResult? result = null;
        var window = Shell("Условия сборки", new ScrollViewer { Content = panel }, 460, 560);
        panel.Children.Add(Buttons(window, () => result = new ConditionsResult(CheckedExclusions(checks), drafts.IsChecked == true)));
        return await ShowAsync(window) ? result : null;
    }

    /// <summary>Правит правила исключения связанного .ditaval; правила подсветки (flag) пишет
    /// обратно нетронутыми.</summary>
    public async Task<bool> EditDitavalAsync(DitaProject project)
    {
        if (project.DitavalPath is null)
        {
            await MessageAsync("Редактирование .ditaval", "Сначала подключите файл .ditaval через «Публикация → Подключить .ditaval…».");
            return false;
        }

        if (project.ResolveLinkedDitaval(out var error) is not { } current)
        {
            await MessageAsync("Редактирование .ditaval", error ?? $"Не удалось прочитать связанный файл: {project.DitavalPath}");
            return false;
        }

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped($"Файл: {project.DitavalPath}. Отметьте значения, которые нужно исключить из сборки — " +
            (current.Flags.Count > 0 ? $"правила подсветки ({current.Flags.Count}) в файле не тронутся." : "правил подсветки в файле нет.")));
        var checks = ConditionChecks(panel, project, (attribute, value) =>
            current.Exclude.TryGetValue(attribute, out var excluded) && excluded.Contains(value));

        var window = Shell("Редактирование .ditaval", new ScrollViewer { Content = panel }, 460, 560);
        Dictionary<string, HashSet<string>>? exclude = null;
        panel.Children.Add(Buttons(window, () => exclude = CheckedExclusions(checks)));
        if (!await ShowAsync(window) || exclude is null)
        {
            return false;
        }

        var fullPath = Path.Combine(project.RootPath, project.DitavalPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            DitavalWriter.Write(fullPath, new DitavalRules(exclude, current.Flags));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageAsync("Редактирование .ditaval", $"Не удалось записать файл: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------- колонтитулы PDF

    public async Task<PdfHeaderFooterResult?> PdfHeaderFooterAsync(DitaProject project)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Wrapped("Колонтитулы поддерживаются только при печати встроенным браузером редактора — это происходит " +
                                   "автоматически, если задан свой текст или если в системе не установлен Edge/Chrome. " +
                                   "Иначе PDF печатается без колонтитулов, как и раньше.", 12));

        var show = new CheckBox { Content = "Показывать колонтитулы", IsChecked = project.PdfShowHeaderFooter };
        panel.Children.Add(show);
        panel.Children.Add(Label("Текст в шапке"));
        var header = Input(project.PdfHeaderText ?? string.Empty);
        panel.Children.Add(header);
        panel.Children.Add(Label("Текст в подвале"));
        var footer = Input(project.PdfFooterText ?? string.Empty);
        panel.Children.Add(footer);
        panel.Children.Add(Muted(new TextBlock
        {
            Text = "Номер страницы и общее число страниц добавляются автоматически справа в подвале.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        }));

        PdfHeaderFooterResult? result = null;
        var window = Shell("Колонтитулы PDF", panel, 440, 340);
        panel.Children.Add(Buttons(window, () => result = new PdfHeaderFooterResult(show.IsChecked == true, header.Text ?? string.Empty, footer.Text ?? string.Empty)));
        return await ShowAsync(window) ? result : null;
    }

    // --------------------------------------------------- пользовательский CSS

    private const string DefaultCustomCss = """
/* Пользовательские стили публикации DITA Studio.
   Правила из этого файла подключаются последними и могут переопределять встроенные —
   так что достаточно переопределить только то, что нужно изменить.

   Готовые классы для управления печатью и PDF (ставятся через атрибут outputclass
   на нужном элементе — в панели «Атрибуты» или пунктами меню «Структура»):
   - outputclass="page-break-before" на заголовке (title) — начинает новую страницу;
   - outputclass="page-break-auto" на таблице (table) — разрешает перенос таблицы
     между страницами с повтором строки шапки (thead) на каждой странице.

   Этот же файл оформляет и экспорт в DOCX: правила для тегов и классов публикации
   (body, p, h1…h6, .note, .shortdesc, pre, table, th, td…) становятся стилями Word,
   правила для собственных классов — стилями элементов с таким outputclass,
   @page { size; margin } — размером и полями страницы. Правила только для Word
   можно положить в @media docx { … } — браузер их не применит.
   Титул, оглавление, колонтитулы и нумерация заголовков — «Публикация → Оформление DOCX…». */

""";

    public async Task CustomCssAsync(DitaProject project)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Файл стилей подключается к каждой публикации (HTML, PDF и DOCX) в дополнение к " +
                   "встроенным стилям — его правила применяются последними и могут их переопределять. " +
                   "В DOCX правила становятся стилями Word; правила только для Word — в @media docx { … }.",
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Label("Подключённый файл"));
        var status = new TextBlock { FontFamily = Mono, TextWrapping = TextWrapping.Wrap };
        void RefreshStatus() => status.Text = project.CustomCssPath ?? "не подключён";
        RefreshStatus();
        panel.Children.Add(status);

        var cssFilter = new[] { new FileFilter("Файлы CSS", "*.css") };

        var create = new Button { Content = "Создать новый файл…", Padding = new Thickness(12, 5, 12, 5) };
        create.Click += async (_, _) =>
        {
            var file = await _files.SaveFileAsync("Создать файл стилей", cssFilter, "custom.css", project.RootPath);
            if (file is null)
            {
                return;
            }

            try
            {
                AtomicFile.WriteAllText(file, DefaultCustomCss, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await MessageAsync("Пользовательский CSS", $"Не удалось создать файл: {ex.Message}");
                return;
            }

            project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'));
            RefreshStatus();
        };

        var attach = new Button { Content = "Подключить существующий…", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        attach.Click += async (_, _) =>
        {
            var file = await _files.OpenFileAsync("Выберите файл стилей", cssFilter, project.RootPath);
            if (file is not null)
            {
                project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'));
                RefreshStatus();
            }
        };

        var detach = new Button { Content = "Отключить", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        detach.Click += (_, _) =>
        {
            project.SetCustomCssPath(null);
            RefreshStatus();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(create);
        buttons.Children.Add(attach);
        buttons.Children.Add(detach);
        panel.Children.Add(buttons);

        var window = Shell("Пользовательский CSS", panel, 520, 300);
        panel.Children.Add(CloseButton(window));
        await ShowAsync(window);
    }

    // ------------------------------------------------------------ оформление DOCX

    private static readonly string[] Alignments = { "Слева", "По центру", "Справа" };

    private static readonly string[] Languages = { "ru-RU", "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "uk-UA", "be-BY", "kk-KZ" };

    public async Task<DocxLayout?> DocxLayoutSettingsAsync(DitaProject project)
    {
        var current = project.DocxLayout.Clone();
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(Muted(new TextBlock
        {
            Text = "Внешний вид текста — шрифты, цвета, отступы, рамки, таблицы — задаётся пользовательским CSS " +
                   "проекта («Публикация → Пользовательский CSS…») и одинаково действует на HTML, PDF и DOCX. Размер " +
                   "бумаги, ориентация и поля — «Публикация → Параметры страницы…». Здесь — то, чего CSS не умеет: " +
                   "устройство документа Word.",
            TextWrapping = TextWrapping.Wrap
        }));

        // ---- титул
        panel.Children.Add(Header("Титульная страница"));
        var titlePage = Check("Отдельная титульная страница с названием карты", current.TitlePage);
        panel.Children.Add(titlePage);
        panel.Children.Add(Label("Подзаголовок"));
        var subtitle = Named(Input(current.Subtitle), "Текст подзаголовка");
        panel.Children.Add(subtitle);
        panel.Children.Add(Label("Автор или организация (на титуле и в свойствах файла)"));
        var author = Named(Input(current.Author), "Автор или организация");
        panel.Children.Add(author);
        var titleDate = Check("Дата публикации на титуле", current.TitlePageDate, top: 6);
        panel.Children.Add(titleDate);
        Bind(titlePage, subtitle, titleDate);

        // ---- оглавление
        panel.Children.Add(Header("Оглавление"));
        var toc = Check("Оглавление в начале документа", current.TableOfContents);
        panel.Children.Add(toc);
        var tocDepth = Depth(current.TocDepth, "Уровней в оглавлении");
        panel.Children.Add(Row("Уровней заголовков в оглавлении:", tocDepth));
        var tocTitle = Named(new ComboBox
        {
            IsEditable = true,
            ItemsSource = new[] { "Содержание", "Оглавление" },
            Text = current.TocTitle.Length > 0 ? current.TocTitle : "Содержание",
            Width = 200,
            Padding = new Thickness(4, 3, 4, 3)
        }, "Заголовок оглавления");
        panel.Children.Add(Row("Заголовок оглавления (и в PDF):", tocTitle));
        Bind(toc, tocDepth, tocTitle);

        // ---- заголовки
        panel.Children.Add(Header("Заголовки и подписи"));
        var numberHeadings = Check("Нумеровать заголовки: 1, 1.1, 1.1.1…", current.NumberHeadings);
        panel.Children.Add(numberHeadings);
        var numberingDepth = Depth(current.NumberingDepth, "Нумеровать уровней");
        panel.Children.Add(Row("Нумеровать уровней:", numberingDepth));
        Bind(numberHeadings, numberingDepth);
        var pageBreak = Check("Каждый топик верхнего уровня — с новой страницы", current.PageBreakBeforeTopLevel, top: 6);
        panel.Children.Add(pageBreak);
        var numberFigures = Check("Нумеровать рисунки и таблицы в подписях", current.NumberFiguresAndTables, top: 6);
        panel.Children.Add(numberFigures);
        var captionFormat = Named(new ComboBox
        {
            ItemsSource = new[] { "Рисунок 1. Название", "Рисунок 1 — Название" },
            SelectedIndex = (int)current.CaptionSeparator,
            Width = 200,
            Padding = new Thickness(4, 3, 4, 3)
        }, "Формат подписи");
        panel.Children.Add(Row("Формат подписи:", captionFormat));
        Bind(numberFigures, captionFormat);

        // ---- колонтитулы
        panel.Children.Add(Header("Колонтитулы"));
        panel.Children.Add(Label("Верхний колонтитул"));
        var header = Named(Input(current.HeaderText), "Текст верхнего колонтитула");
        var headerAlign = Alignment(current.HeaderAlignment, "Выравнивание верхнего колонтитула");
        panel.Children.Add(WithAlignment(header, headerAlign));
        var headerImage = ImagePicker(project, current.HeaderImage, current.HeaderImageAlignment, current.HeaderImageHeightMm, "верхнего");
        panel.Children.Add(headerImage.Row);
        panel.Children.Add(Label("Нижний колонтитул"));
        var footer = Named(Input(current.FooterText), "Текст нижнего колонтитула");
        var footerAlign = Alignment(current.FooterAlignment, "Выравнивание нижнего колонтитула");
        panel.Children.Add(WithAlignment(footer, footerAlign));
        var footerImage = ImagePicker(project, current.FooterImage, current.FooterImageAlignment, current.FooterImageHeightMm, "нижнего");
        panel.Children.Add(footerImage.Row);
        panel.Children.Add(Muted(new TextBlock
        {
            Text = "Поля: {page} — номер страницы, {pages} — число страниц, {title} — название карты, " +
                   "{date} — дата публикации. Например: «Стр. {page} из {pages}». Пусто — колонтитула нет. " +
                   "Картинка (например, логотип; PNG, JPEG, GIF, BMP) ставится слева, по центру или справа, " +
                   "высота — в миллиметрах; она появится и в колонтитулах PDF.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        }));
        var noFirst = Check("Не показывать колонтитулы на первой странице", current.NoHeaderOnFirstPage, top: 6);
        panel.Children.Add(noFirst);

        // ---- печать
        panel.Children.Add(Header("Печать"));
        var mirror = Check("Зеркальные поля (двусторонняя печать)", current.MirrorMargins);
        panel.Children.Add(mirror);
        var gutter = Named(new TextBox
        {
            Text = current.GutterMm.ToString("0.#", CultureInfo.CurrentCulture),
            Width = 70,
            Padding = new Thickness(4, 3, 4, 3)
        }, "Поле переплёта, мм");
        panel.Children.Add(Row("Поле переплёта, мм:", gutter));

        // ---- язык
        panel.Children.Add(Header("Язык и переносы"));
        var language = Named(new ComboBox
        {
            IsEditable = true,
            ItemsSource = Languages,
            Text = current.Language,
            Width = 110,
            Padding = new Thickness(4, 3, 4, 3)
        }, "Язык текста");
        panel.Children.Add(Row("Язык текста (проверка правописания в Word):", language));
        var hyphenation = Check("Автоматическая расстановка переносов", current.AutoHyphenation, top: 6);
        panel.Children.Add(hyphenation);

        DocxLayout? result = null;
        var window = Shell("Оформление DOCX", new ScrollViewer { Content = panel }, 560, 640);
        panel.Children.Add(Buttons(window, () =>
        {
            var gutterMm = double.TryParse((gutter.Text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                ? g
                : current.GutterMm;

            result = new DocxLayout
            {
                TitlePage = titlePage.IsChecked == true,
                Subtitle = (subtitle.Text ?? string.Empty).Trim(),
                Author = (author.Text ?? string.Empty).Trim(),
                TitlePageDate = titleDate.IsChecked == true,
                TableOfContents = toc.IsChecked == true,
                TocDepth = tocDepth.SelectedItem as int? ?? current.TocDepth,
                TocTitle = (tocTitle.Text ?? string.Empty).Trim() is { Length: > 0 } name && name != "Содержание" ? name : string.Empty,
                NumberHeadings = numberHeadings.IsChecked == true,
                NumberingDepth = numberingDepth.SelectedItem as int? ?? current.NumberingDepth,
                PageBreakBeforeTopLevel = pageBreak.IsChecked == true,
                NumberFiguresAndTables = numberFigures.IsChecked == true,
                CaptionSeparator = (CaptionSeparator)Math.Max(0, captionFormat.SelectedIndex),
                HeaderText = header.Text ?? string.Empty,
                HeaderAlignment = (DocxHeaderAlignment)Math.Max(0, headerAlign.SelectedIndex),
                FooterText = footer.Text ?? string.Empty,
                FooterAlignment = (DocxHeaderAlignment)Math.Max(0, footerAlign.SelectedIndex),
                NoHeaderOnFirstPage = noFirst.IsChecked == true,
                HeaderImage = headerImage.Path(),
                HeaderImageAlignment = headerImage.Alignment(),
                HeaderImageHeightMm = headerImage.HeightMm() ?? current.HeaderImageHeightMm,
                FooterImage = footerImage.Path(),
                FooterImageAlignment = footerImage.Alignment(),
                FooterImageHeightMm = footerImage.HeightMm() ?? current.FooterImageHeightMm,
                MirrorMargins = mirror.IsChecked == true,
                GutterMm = gutterMm,
                Language = (language.Text ?? string.Empty).Trim(),
                AutoHyphenation = hyphenation.IsChecked == true,
                PaperSize = current.PaperSize,
                Landscape = current.Landscape,
                MarginTopMm = current.MarginTopMm,
                MarginBottomMm = current.MarginBottomMm,
                MarginLeftMm = current.MarginLeftMm,
                MarginRightMm = current.MarginRightMm
            };
            result.Normalize();
        }));

        return await ShowAsync(window) ? result : null;

        static TextBlock Header(string text) => new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 16, 0, 6)
        };

        static CheckBox Check(string text, bool value, double top = 0) =>
            new() { Content = text, IsChecked = value, Margin = new Thickness(0, top, 0, 0) };

        // Имена для экранного диктора и UI-автоматизации: подпись над полем — отдельный TextBlock.
        static T Named<T>(T control, string name) where T : Control
        {
            AutomationProperties.SetName(control, name);
            return control;
        }

        static ComboBox Depth(int value, string name) => Named(new ComboBox
        {
            ItemsSource = Enumerable.Range(1, 6).ToList(),
            SelectedItem = Math.Clamp(value, 1, 6),
            Width = 60,
            Padding = new Thickness(4, 3, 4, 3)
        }, name);

        static ComboBox Alignment(DocxHeaderAlignment value, string name) => Named(new ComboBox
        {
            ItemsSource = Alignments,
            SelectedIndex = (int)value,
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(4, 3, 4, 3)
        }, name);

        // Картинка колонтитула: путь от папки проекта, «Обзор…», «Убрать», место и высота.
        (Control Row, Func<string> Path, Func<DocxHeaderAlignment> Alignment, Func<double?> HeightMm) ImagePicker(
            DitaProject owner, string value, DocxHeaderAlignment alignment, double heightMm, string which)
        {
            var path = Named(new TextBox { Text = value, Watermark = "без картинки", Padding = new Thickness(4, 3, 4, 3) },
                $"Картинка {which} колонтитула");
            var browse = new Button { Content = "Обзор…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += async (_, _) =>
            {
                var file = await _files.OpenFileAsync("Картинка колонтитула",
                    new[] { new FileFilter("Изображения", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp") }, owner.RootPath);
                if (file is not null)
                {
                    path.Text = System.IO.Path.GetRelativePath(owner.RootPath, file).Replace('\\', '/');
                }
            };
            var clear = new Button { Content = "Убрать", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(4, 0, 0, 0) };
            clear.Click += (_, _) => path.Text = string.Empty;
            var place = Alignment(alignment, $"Место картинки {which} колонтитула");
            var height = Named(new TextBox
            {
                Text = heightMm.ToString("0.#", CultureInfo.CurrentCulture),
                Width = 50,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(8, 0, 0, 0)
            }, $"Высота картинки {which} колонтитула, мм");

            var tail = new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, clear, place, height, Muted(new TextBlock { Text = "мм", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }) } };
            var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var caption = new TextBlock { Text = "Картинка:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(caption, Dock.Left);
            DockPanel.SetDock(tail, Dock.Right);
            dock.Children.Add(caption);
            dock.Children.Add(tail);
            dock.Children.Add(path);

            return (dock,
                () => (path.Text ?? string.Empty).Trim(),
                () => (DocxHeaderAlignment)Math.Max(0, place.SelectedIndex),
                () => double.TryParse((height.Text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : null);
        }

        static DockPanel WithAlignment(TextBox text, ComboBox alignment)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(alignment, Dock.Right);
            dock.Children.Add(alignment);
            dock.Children.Add(text);
            return dock;
        }

        static StackPanel Row(string label, Control control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(control);
            return row;
        }
    }
}
