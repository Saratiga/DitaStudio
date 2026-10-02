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
using DitaStudio.Core.Localization;

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
        StackPanel panel, DitaProject project, Func<string, string, bool> isExcluded, string? skipAttribute = null)
    {
        var checks = new List<(string, string, CheckBox)>();
        foreach (var (attribute, set) in CollectConditionValues(project).Where(v => v.Key != skipAttribute).OrderBy(v => v.Key, StringComparer.Ordinal))
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
            panel.Children.Add(Muted(new TextBlock { Text = Loc.T("Dlg_TheProjectHasNoConditionalAttributes"), Margin = new Thickness(0, 4, 0, 0) }));
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

    /// <summary>Правит правила исключения связанного .ditaval; правила подсветки (flag) пишет
    /// обратно нетронутыми.</summary>
    public async Task<bool> EditDitavalAsync(DitaProject project)
    {
        if (project.DitavalPath is null)
        {
            await MessageAsync(Loc.T("Dlg_EditDitaval"), Loc.T("Dlg_FirstConnectADitavalFileVia"));
            return false;
        }

        if (project.ResolveLinkedDitaval(out var error) is not { } current)
        {
            await MessageAsync(Loc.T("Dlg_EditDitaval"), error ?? Loc.T("Dlg_CouldNotReadTheLinkedFile", project.DitavalPath));
            return false;
        }

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped(Loc.T("Dlg_File0TickTheValuesTo", project.DitavalPath) +
            (current.Flags.Count > 0 ? Loc.T("Dlg_TheFlaggingRules0InThe", current.Flags.Count) : Loc.T("Dlg_ThereAreNoFlaggingRulesIn"))));
        var checks = ConditionChecks(panel, project, (attribute, value) =>
            current.Exclude.TryGetValue(attribute, out var excluded) && excluded.Contains(value));

        var window = Shell(Loc.T("Dlg_EditDitaval"), new ScrollViewer { Content = panel }, 460, 560);
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
            await MessageAsync(Loc.T("Dlg_EditDitaval"), Loc.T("Dlg_CouldNotWriteTheFile0", ex.Message));
            return false;
        }
    }

    // ------------------------------------------------------- колонтитулы PDF

    public async Task<PdfHeaderFooterResult?> PdfHeaderFooterAsync(DitaProject project)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Wrapped(Loc.T("Dlg_HeadersAndFootersAreSupportedOnly") +
                                   Loc.T("Dlg_AutomaticallyIfYouSetYourOwn") +
                                   Loc.T("Dlg_OtherwiseThePDFIsPrintedWithout"), 12));

        var show = new CheckBox { Content = Loc.T("Dlg_ShowHeadersAndFooters"), IsChecked = project.PdfShowHeaderFooter };
        panel.Children.Add(show);
        panel.Children.Add(Label(Loc.T("Dlg_HeaderText")));
        var header = Input(project.PdfHeaderText ?? string.Empty);
        panel.Children.Add(header);
        panel.Children.Add(Label(Loc.T("Dlg_FooterText")));
        var footer = Input(project.PdfFooterText ?? string.Empty);
        panel.Children.Add(footer);
        panel.Children.Add(Muted(new TextBlock
        {
            Text = Loc.T("Dlg_ThePageNumberAndTotalPage"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        }));

        PdfHeaderFooterResult? result = null;
        var window = Shell(Loc.T("Dlg_PDFHeadersAndFooters"), panel, 440, 340);
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
            Text = Loc.T("Dlg_TheStyleFileIsAttachedTo") +
                   Loc.T("Dlg_TheBuiltInStylesItsRules") +
                   Loc.T("Dlg_InDOCXTheRulesBecomeWord"),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Label(Loc.T("Dlg_AttachedFile")));
        var status = new TextBlock { FontFamily = Mono, TextWrapping = TextWrapping.Wrap };
        void RefreshStatus() => status.Text = project.CustomCssPath ?? Loc.T("Dlg_NotAttached");
        RefreshStatus();
        panel.Children.Add(status);

        var cssFilter = new[] { new FileFilter(Loc.T("Dlg_CSSFiles"), "*.css") };

        var create = new Button { Content = Loc.T("Dlg_CreateNewFile"), Padding = new Thickness(12, 5, 12, 5) };
        create.Click += async (_, _) =>
        {
            var file = await _files.SaveFileAsync(Loc.T("Dlg_CreateStyleFile"), cssFilter, "custom.css", project.RootPath);
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
                await MessageAsync(Loc.T("Dlg_CustomCSS"), Loc.T("Dlg_CouldNotCreateTheFile0", ex.Message));
                return;
            }

            project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'));
            RefreshStatus();
        };

        var attach = new Button { Content = Loc.T("Dlg_AttachExisting"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        attach.Click += async (_, _) =>
        {
            var file = await _files.OpenFileAsync(Loc.T("Dlg_ChooseAStyleFile"), cssFilter, project.RootPath);
            if (file is not null)
            {
                project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'));
                RefreshStatus();
            }
        };

        var detach = new Button { Content = Loc.T("Dlg_Detach"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
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

        var window = Shell(Loc.T("Dlg_CustomCSS"), panel, 520, 300);
        panel.Children.Add(CloseButton(window));
        await ShowAsync(window);
    }

    // ------------------------------------------------------------ оформление DOCX

    private static string[] Alignments => new[] { Loc.T("Dlg_Left"), Loc.T("Tip_AlignCenter"), Loc.T("Dlg_Right") };

    private static readonly string[] Languages = { "ru-RU", "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "uk-UA", "be-BY", "kk-KZ" };

    public Task<DocxLayout?> DocxLayoutSettingsAsync(DitaProject project) => LayoutDialogAsync(project, 0);

    /// <summary>Вкладка «Оформление» общего окна «Оформление DOCX»: титул, оглавление, нумерация, колонтитулы, печать, язык.</summary>
    private (Control Content, Action<DocxLayout> Apply) BuildFormatTab(DitaProject project, DocxLayout current)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(Muted(new TextBlock
        {
            Text = Loc.T("Dlg_TheLookOfTheTextFonts") +
                   Loc.T("Dlg_PublishCustomCSSAndWorksThe") +
                   Loc.T("Dlg_OrientationAndMarginsAreInPublish") +
                   Loc.T("Dlg_TheStructureOfTheWordDocument"),
            TextWrapping = TextWrapping.Wrap
        }));

        // ---- титул
        panel.Children.Add(Header(Loc.T("Dlg_TitlePage")));
        var titlePage = Check(Loc.T("Dlg_ASeparateTitlePageWithThe"), current.TitlePage);
        panel.Children.Add(titlePage);
        panel.Children.Add(Label(Loc.T("Dlg_Subtitle")));
        var subtitle = Named(Input(current.Subtitle), Loc.T("Dlg_SubtitleText"));
        panel.Children.Add(subtitle);
        panel.Children.Add(Label(Loc.T("Dlg_AuthorOrOrganizationOnTheTitle")));
        var author = Named(Input(current.Author), Loc.T("Dlg_AuthorOrOrganization"));
        panel.Children.Add(author);
        var titleDate = Check(Loc.T("Dlg_PublicationDateOnTheTitlePage"), current.TitlePageDate, top: 6);
        panel.Children.Add(titleDate);
        panel.Children.Add(Label(Loc.T("Dlg_TitlePageImageAboveTheTitle")));
        var titleImage = ImagePicker(project, current.TitleImage, current.TitleImageAlignment, current.TitleImageHeightMm, Loc.T("Dlg_OfTheTitlePage"));
        panel.Children.Add(titleImage.Row);
        Bind(titlePage, subtitle, titleDate, titleImage.Row);

        // ---- оглавление
        panel.Children.Add(Header(Loc.T("Dlg_TableOfContents")));
        var toc = Check(Loc.T("Dlg_TableOfContentsAtTheBeginning"), current.TableOfContents);
        panel.Children.Add(toc);
        var tocDepth = Depth(current.TocDepth, Loc.T("Dlg_LevelsInTheTableOfContents"));
        panel.Children.Add(Row(Loc.T("Dlg_HeadingLevelsInTheTableOf"), tocDepth));
        var tocTitle = Named(new ComboBox
        {
            IsEditable = true,
            ItemsSource = new[] { Labels.For(Loc.Instance.Language).Contents, Loc.T("Dlg_TableOfContents") },
            Text = current.TocTitle.Length > 0 ? current.TocTitle : Labels.For(Loc.Instance.Language).Contents,
            Width = 200,
            Padding = new Thickness(4, 3, 4, 3)
        }, Loc.T("Dlg_TableOfContentsTitle"));
        panel.Children.Add(Row(Loc.T("Dlg_TableOfContentsTitleAndIn"), tocTitle));
        Bind(toc, tocDepth, tocTitle);

        // ---- заголовки
        panel.Children.Add(Header(Loc.T("Dlg_HeadingsAndCaptions")));
        var numberHeadings = Check(Loc.T("Dlg_NumberHeadings1111"), current.NumberHeadings);
        panel.Children.Add(numberHeadings);
        var numberingDepth = Depth(current.NumberingDepth, Loc.T("Dlg_LevelsToNumber"));
        panel.Children.Add(Row(Loc.T("Dlg_LevelsToNumber2"), numberingDepth));
        Bind(numberHeadings, numberingDepth);
        var pageBreak = Check(Loc.T("Dlg_EveryTopLevelTopicOnA"), current.PageBreakBeforeTopLevel, top: 6);
        panel.Children.Add(pageBreak);
        var numberFigures = Check(Loc.T("Dlg_NumberFiguresAndTablesInCaptions"), current.NumberFiguresAndTables, top: 6);
        panel.Children.Add(numberFigures);
        var captionFormat = Named(new ComboBox
        {
            ItemsSource = new[]
            {
                $"{Labels.For(Loc.Instance.Language).Figure} 1. {Loc.T("Dlg_CaptionSampleTitle")}",
                $"{Labels.For(Loc.Instance.Language).Figure} 1 — {Loc.T("Dlg_CaptionSampleTitle")}"
            },
            SelectedIndex = (int)current.CaptionSeparator,
            Width = 200,
            Padding = new Thickness(4, 3, 4, 3)
        }, Loc.T("Dlg_CaptionFormat"));
        panel.Children.Add(Row(Loc.T("Dlg_CaptionFormat2"), captionFormat));
        Bind(numberFigures, captionFormat);

        // ---- колонтитулы
        panel.Children.Add(Header(Loc.T("Dlg_HeadersAndFooters")));
        panel.Children.Add(Label(Loc.T("Dlg_Header")));
        var header = Named(Input(current.HeaderText), Loc.T("Dlg_HeaderText2"));
        var headerAlign = Alignment(current.HeaderAlignment, Loc.T("Dlg_HeaderAlignment"));
        panel.Children.Add(WithAlignment(header, headerAlign));
        var headerImage = ImagePicker(project, current.HeaderImage, current.HeaderImageAlignment, current.HeaderImageHeightMm, Loc.T("Dlg_Header2"));
        panel.Children.Add(headerImage.Row);
        panel.Children.Add(Label(Loc.T("Dlg_Footer")));
        var footer = Named(Input(current.FooterText), Loc.T("Dlg_FooterText2"));
        var footerAlign = Alignment(current.FooterAlignment, Loc.T("Dlg_FooterAlignment"));
        panel.Children.Add(WithAlignment(footer, footerAlign));
        var footerImage = ImagePicker(project, current.FooterImage, current.FooterImageAlignment, current.FooterImageHeightMm, Loc.T("Dlg_Footer2"));
        panel.Children.Add(footerImage.Row);
        panel.Children.Add(Muted(new TextBlock
        {
            Text = Loc.T("Dlg_FieldsPagePageNumberPagesPage") +
                   Loc.T("Dlg_DatePublicationDateForExamplePage") +
                   Loc.T("Dlg_AnImageForExampleALogo") +
                   Loc.T("Dlg_TheHeightIsInMillimetersIt") +
                   Loc.T("Dlg_CSSWithPageMarginBoxesPage"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        }));
        var fitImages = Check(Loc.T("Dlg_FitImagesToTheHeaderOr"),
            current.FitHeaderFooterImages, top: 6);
        panel.Children.Add(fitImages);
        var noFirst = Check(Loc.T("Dlg_DoNotShowHeadersAndFooters"), current.NoHeaderOnFirstPage, top: 6);
        panel.Children.Add(noFirst);

        // ---- печать
        panel.Children.Add(Header(Loc.T("Dlg_Printing")));
        var mirror = Check(Loc.T("Dlg_MirrorMarginsDoubleSidedPrinting"), current.MirrorMargins);
        panel.Children.Add(mirror);
        var gutter = Named(new TextBox
        {
            Text = current.GutterMm.ToString("0.#", CultureInfo.CurrentCulture),
            Width = 70,
            Padding = new Thickness(4, 3, 4, 3)
        }, Loc.T("Dlg_BindingMarginMm"));
        panel.Children.Add(Row(Loc.T("Dlg_BindingMarginMm2"), gutter));

        // ---- язык
        panel.Children.Add(Header(Loc.T("Dlg_LanguageAndHyphenation")));
        var language = Named(new ComboBox
        {
            IsEditable = true,
            ItemsSource = Languages,
            Text = current.Language,
            Width = 110,
            Padding = new Thickness(4, 3, 4, 3)
        }, Loc.T("Dlg_TextLanguage"));
        panel.Children.Add(Row(Loc.T("Dlg_TextLanguageSpellCheckingInWord"), language));
        var hyphenation = Check(Loc.T("Dlg_AutomaticHyphenation"), current.AutoHyphenation, top: 6);
        panel.Children.Add(hyphenation);

        return (new ScrollViewer { Content = panel }, target =>
        {
            var gutterMm = double.TryParse((gutter.Text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                ? g
                : current.GutterMm;

            var fresh = new DocxLayout
            {
                TitlePage = titlePage.IsChecked == true,
                Subtitle = (subtitle.Text ?? string.Empty).Trim(),
                Author = (author.Text ?? string.Empty).Trim(),
                TitlePageDate = titleDate.IsChecked == true,
                TitleImage = titleImage.Path(),
                TitleImageAlignment = titleImage.Alignment(),
                TitleImageHeightMm = titleImage.HeightMm() ?? current.TitleImageHeightMm,
                TableOfContents = toc.IsChecked == true,
                TocDepth = tocDepth.SelectedItem as int? ?? current.TocDepth,
                TocTitle = (tocTitle.Text ?? string.Empty).Trim() is { Length: > 0 } name && name != Labels.Russian.Contents && name != Labels.English.Contents ? name : string.Empty,
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
                FitHeaderFooterImages = fitImages.IsChecked == true,
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
                // Страница — не в этом диалоге, но принадлежит тому же объекту: переносится целиком, в том числе свой размер листа.
                PaperSize = current.PaperSize,
                PaperWidthMm = current.PaperWidthMm,
                PaperHeightMm = current.PaperHeightMm,
                Landscape = current.Landscape,
                MarginTopMm = current.MarginTopMm,
                MarginBottomMm = current.MarginBottomMm,
                MarginLeftMm = current.MarginLeftMm,
                MarginRightMm = current.MarginRightMm
            };
            // Поля этой вкладки переносятся в общий объект; страница (бумага, ориентация, поля) — у вкладки «Страница».
            CopyLayoutFields(fresh, target, PageFieldNames);
        });

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
            var path = Named(new TextBox { Text = value, Watermark = Loc.T("Dlg_NoImage"), Padding = new Thickness(4, 3, 4, 3) },
                Loc.T("Dlg_Image0OfTheHeaderOr", which));
            var browse = new Button { Content = Loc.T("Dlg_Browse"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += async (_, _) =>
            {
                var file = await _files.OpenFileAsync(Loc.T("Dlg_HeaderOrFooterImage"),
                    new[] { new FileFilter(Loc.T("Dlg_Images"), "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp") }, owner.RootPath);
                if (file is not null)
                {
                    path.Text = System.IO.Path.GetRelativePath(owner.RootPath, file).Replace('\\', '/');
                }
            };
            var clear = new Button { Content = Loc.T("Dlg_Remove"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(4, 0, 0, 0) };
            clear.Click += (_, _) => path.Text = string.Empty;
            var place = Alignment(alignment, Loc.T("Dlg_PositionOfImage0OfThe", which));
            var height = Named(new TextBox
            {
                Text = heightMm.ToString("0.#", CultureInfo.CurrentCulture),
                Width = 50,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(8, 0, 0, 0)
            }, Loc.T("Dlg_HeightOfImage0OfThe", which));

            var tail = new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, clear, place, height, Muted(new TextBlock { Text = Loc.T("Dlg_Mm"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }) } };
            var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var caption = new TextBlock { Text = Loc.T("Dlg_Image"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
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
