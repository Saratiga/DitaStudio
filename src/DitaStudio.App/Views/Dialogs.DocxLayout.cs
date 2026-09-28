using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.App.Views;

// «Публикация → Оформление DOCX…» — то, что пользовательский CSS выразить не может.
public static partial class Dialogs
{
    private static readonly string[] Alignments = { "Слева", "По центру", "Справа" };

    private static readonly string[] Languages = { "ru-RU", "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "uk-UA", "be-BY", "kk-KZ" };

    public static DocxLayout? DocxLayoutSettings(DitaProject project)
    {
        var current = project.DocxLayout.Clone();
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "Внешний вид текста — шрифты, цвета, отступы, рамки, таблицы, а также размер и поля " +
                   "страницы (@page) — задаётся пользовательским CSS проекта («Публикация → Пользовательский CSS…») " +
                   "и одинаково действует на HTML, PDF и DOCX. Здесь — то, чего CSS не умеет: устройство документа Word.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeManager.Brush("TextMuted")
        });

        // ---- титул
        panel.Children.Add(Header("Титульная страница"));
        var titlePage = Check("Отдельная титульная страница с названием карты", current.TitlePage);
        panel.Children.Add(titlePage);
        panel.Children.Add(Label("Подзаголовок"));
        var subtitle = Input(current.Subtitle, "Текст подзаголовка");
        panel.Children.Add(subtitle);
        panel.Children.Add(Label("Автор или организация (на титуле и в свойствах файла)"));
        var author = Input(current.Author, "Автор или организация");
        panel.Children.Add(author);
        var titleDate = Check("Дата публикации на титуле", current.TitlePageDate);
        titleDate.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(titleDate);
        Bind(titlePage, subtitle, titleDate);

        // ---- оглавление
        panel.Children.Add(Header("Оглавление"));
        var toc = Check("Оглавление в начале документа", current.TableOfContents);
        panel.Children.Add(toc);
        var tocDepth = Depth(current.TocDepth, "Уровней в оглавлении");
        panel.Children.Add(Row("Уровней заголовков в оглавлении:", tocDepth));
        Bind(toc, tocDepth);

        // ---- заголовки
        panel.Children.Add(Header("Заголовки и подписи"));
        var numberHeadings = Check("Нумеровать заголовки: 1, 1.1, 1.1.1…", current.NumberHeadings);
        panel.Children.Add(numberHeadings);
        var numberingDepth = Depth(current.NumberingDepth, "Нумеровать уровней");
        panel.Children.Add(Row("Нумеровать уровней:", numberingDepth));
        Bind(numberHeadings, numberingDepth);
        var pageBreak = Check("Каждый топик верхнего уровня — с новой страницы", current.PageBreakBeforeTopLevel);
        pageBreak.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(pageBreak);
        var numberFigures = Check("Нумеровать рисунки и таблицы в подписях", current.NumberFiguresAndTables);
        numberFigures.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(numberFigures);

        // ---- колонтитулы
        panel.Children.Add(Header("Колонтитулы"));
        panel.Children.Add(Label("Верхний колонтитул"));
        var header = Input(current.HeaderText, "Текст верхнего колонтитула");
        var headerAlign = Alignment(current.HeaderAlignment, "Выравнивание верхнего колонтитула");
        panel.Children.Add(WithAlignment(header, headerAlign));
        panel.Children.Add(Label("Нижний колонтитул"));
        var footer = Input(current.FooterText, "Текст нижнего колонтитула");
        var footerAlign = Alignment(current.FooterAlignment, "Выравнивание нижнего колонтитула");
        panel.Children.Add(WithAlignment(footer, footerAlign));
        panel.Children.Add(new TextBlock
        {
            Text = "Поля: {page} — номер страницы, {pages} — число страниц, {title} — название карты, " +
                   "{date} — дата публикации. Например: «Стр. {page} из {pages}». Пусто — колонтитула нет.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeManager.Brush("TextMuted"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        var noFirst = Check("Не показывать колонтитулы на первой странице", current.NoHeaderOnFirstPage);
        noFirst.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(noFirst);

        // ---- печать
        panel.Children.Add(Header("Печать"));
        var mirror = Check("Зеркальные поля (двусторонняя печать)", current.MirrorMargins);
        panel.Children.Add(mirror);
        var gutter = new TextBox
        {
            Text = current.GutterMm.ToString("0.#", CultureInfo.CurrentCulture),
            Width = 70,
            Padding = new Thickness(4, 3, 4, 3)
        };
        AutomationProperties.SetName(gutter, "Поле переплёта, мм");
        panel.Children.Add(Row("Поле переплёта, мм:", gutter));

        // ---- язык
        panel.Children.Add(Header("Язык и переносы"));
        var language = new ComboBox
        {
            IsEditable = true,
            ItemsSource = Languages,
            Text = current.Language,
            Width = 110,
            Padding = new Thickness(4, 3, 4, 3)
        };
        AutomationProperties.SetName(language, "Язык текста");
        panel.Children.Add(Row("Язык текста (проверка правописания в Word):", language));
        var hyphenation = Check("Автоматическая расстановка переносов", current.AutoHyphenation);
        hyphenation.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(hyphenation);

        DocxLayout? result = null;
        var window = Shell("Оформление DOCX", new ScrollViewer { Content = panel }, 560, 640);
        panel.Children.Add(Buttons(window, () =>
        {
            var gutterMm = double.TryParse(gutter.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                ? g
                : current.GutterMm;

            result = new DocxLayout
            {
                TitlePage = titlePage.IsChecked == true,
                Subtitle = subtitle.Text.Trim(),
                Author = author.Text.Trim(),
                TitlePageDate = titleDate.IsChecked == true,
                TableOfContents = toc.IsChecked == true,
                TocDepth = (int)(tocDepth.SelectedItem ?? current.TocDepth),
                NumberHeadings = numberHeadings.IsChecked == true,
                NumberingDepth = (int)(numberingDepth.SelectedItem ?? current.NumberingDepth),
                PageBreakBeforeTopLevel = pageBreak.IsChecked == true,
                NumberFiguresAndTables = numberFigures.IsChecked == true,
                HeaderText = header.Text,
                HeaderAlignment = (DocxHeaderAlignment)Math.Max(0, headerAlign.SelectedIndex),
                FooterText = footer.Text,
                FooterAlignment = (DocxHeaderAlignment)Math.Max(0, footerAlign.SelectedIndex),
                NoHeaderOnFirstPage = noFirst.IsChecked == true,
                MirrorMargins = mirror.IsChecked == true,
                GutterMm = gutterMm,
                Language = language.Text.Trim(),
                AutoHyphenation = hyphenation.IsChecked == true,
                // Параметры страницы правятся в Avalonia-версии — здесь сохраняются как были.
                TocTitle = current.TocTitle,
                PaperSize = current.PaperSize,
                Landscape = current.Landscape,
                MarginTopMm = current.MarginTopMm,
                MarginBottomMm = current.MarginBottomMm,
                MarginLeftMm = current.MarginLeftMm,
                MarginRightMm = current.MarginRightMm
            };
            result.Normalize();
        }));

        return window.ShowDialog() == true ? result : null;

        static TextBlock Header(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 16, 0, 6)
        };

        static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };

        // Имена для экранного диктора и UI-автоматизации: подпись над полем — отдельный TextBlock.
        static T Named<T>(T element, string name) where T : UIElement
        {
            AutomationProperties.SetName(element, name);
            return element;
        }

        static TextBox Input(string text, string name) =>
            Named(new TextBox { Text = text, Padding = new Thickness(4, 3, 4, 3) }, name);

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

        static DockPanel WithAlignment(TextBox text, ComboBox alignment)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(alignment, Dock.Right);
            dock.Children.Add(alignment);
            dock.Children.Add(text);
            return dock;
        }

        static StackPanel Row(string label, FrameworkElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(control);
            return row;
        }

        // Зависимые поля доступны, только когда включён их флажок.
        static void Bind(CheckBox toggle, params UIElement[] dependents)
        {
            void Update()
            {
                foreach (var dependent in dependents)
                {
                    dependent.IsEnabled = toggle.IsChecked == true;
                }
            }

            toggle.Checked += (_, _) => Update();
            toggle.Unchecked += (_, _) => Update();
            Update();
        }
    }
}
