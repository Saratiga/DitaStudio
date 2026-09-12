using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DitaStudio.Core.Project;
using DitaStudio.Core.Templates;
using Microsoft.Win32;

namespace DitaStudio.App.Views;

/// <summary>Небольшие диалоги приложения, собранные в коде — без отдельных XAML-файлов.</summary>
public static class Dialogs
{
    private static Window Shell(string title, UIElement content, double width = 460, double height = 320)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.CanResize,
            Content = content,
            ShowInTaskbar = false
        };
    }

    private static StackPanel Buttons(Window window, Action onOk, string okText = "ОК")
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var ok = new Button { Content = okText, Padding = new Thickness(18, 5, 18, 5), IsDefault = true };
        ok.Click += (_, _) =>
        {
            onOk();
            window.DialogResult = true;
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(18, 5, 18, 5),
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };

        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 3),
        FontSize = 12,
        Foreground = ThemeManager.Brush("TextMuted")
    };

    // ------------------------------------------------------------ новый файл

    public sealed record NewDocumentResult(DocumentTemplate Template, string Title, string FileName, string Folder);

    public static NewDocumentResult? NewDocument(string projectRoot, IEnumerable<string> folders, string? preselectedFolder)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        var templates = new ListBox { Height = 170, ItemsSource = DocumentTemplates.All, SelectedIndex = 0 };
        templates.ItemTemplate = BuildTemplateItemTemplate();

        var titleBox = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
        var fileBox = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
        var folderBox = new ComboBox { ItemsSource = folders.ToList(), Padding = new Thickness(4, 3, 4, 3) };
        folderBox.SelectedItem = preselectedFolder ?? folderBox.Items.OfType<string>().FirstOrDefault();

        var manual = false;
        fileBox.TextChanged += (_, _) => manual = fileBox.IsKeyboardFocusWithin || manual;
        titleBox.TextChanged += (_, _) =>
        {
            if (manual)
            {
                return;
            }

            var template = templates.SelectedItem as DocumentTemplate ?? DocumentTemplates.All[0];
            var stem = DocumentTemplates.SuggestId(titleBox.Text, template.RootElement);
            fileBox.Text = stem + template.Extension;
        };

        templates.SelectionChanged += (_, _) =>
        {
            if (manual || templates.SelectedItem is not DocumentTemplate template)
            {
                return;
            }

            var stem = DocumentTemplates.SuggestId(titleBox.Text, template.RootElement);
            fileBox.Text = stem + template.Extension;
        };

        panel.Children.Add(Label("Тип документа"));
        panel.Children.Add(templates);
        panel.Children.Add(Label("Заголовок"));
        panel.Children.Add(titleBox);
        panel.Children.Add(Label("Имя файла"));
        panel.Children.Add(fileBox);
        panel.Children.Add(Label("Папка"));
        panel.Children.Add(folderBox);

        NewDocumentResult? result = null;
        var window = Shell("Создать документ", new ScrollViewer { Content = panel }, 520, 560);

        panel.Children.Add(Buttons(window, () =>
        {
            var template = templates.SelectedItem as DocumentTemplate ?? DocumentTemplates.All[0];
            var title = string.IsNullOrWhiteSpace(titleBox.Text) ? "Без названия" : titleBox.Text.Trim();
            var fileName = string.IsNullOrWhiteSpace(fileBox.Text)
                ? DocumentTemplates.SuggestId(title, template.RootElement) + template.Extension
                : fileBox.Text.Trim();
            var folder = folderBox.SelectedItem as string ?? projectRoot;
            result = new NewDocumentResult(template, title, fileName, folder);
        }, "Создать"));

        titleBox.Focus();
        return window.ShowDialog() == true ? result : null;
    }

    private static DataTemplate BuildTemplateItemTemplate()
    {
        var stack = new FrameworkElementFactory(typeof(StackPanel));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

        var description = new FrameworkElementFactory(typeof(TextBlock));
        description.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Description"));
        description.SetValue(TextBlock.FontSizeProperty, 11.0);
        description.SetValue(TextBlock.ForegroundProperty, ThemeManager.Brush("TextMuted"));

        stack.AppendChild(name);
        stack.AppendChild(description);

        return new DataTemplate { VisualTree = stack };
    }

    // --------------------------------------------------------------- таблица

    public sealed record TableResult(int Rows, int Columns, bool Header, string Title);

    public static TableResult? InsertTable()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var rows = new TextBox { Text = "3", Padding = new Thickness(4, 3, 4, 3) };
        var cols = new TextBox { Text = "3", Padding = new Thickness(4, 3, 4, 3) };
        var header = new CheckBox { Content = "Строка заголовков", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
        var title = new TextBox { Padding = new Thickness(4, 3, 4, 3) };

        panel.Children.Add(Label("Заголовок таблицы"));
        panel.Children.Add(title);
        panel.Children.Add(Label("Строк"));
        panel.Children.Add(rows);
        panel.Children.Add(Label("Колонок"));
        panel.Children.Add(cols);
        panel.Children.Add(header);

        TableResult? result = null;
        var window = Shell("Вставить таблицу", panel, 380, 380);
        panel.Children.Add(Buttons(window, () =>
        {
            var r = int.TryParse(rows.Text, out var rv) ? Math.Clamp(rv, 1, 100) : 3;
            var c = int.TryParse(cols.Text, out var cv) ? Math.Clamp(cv, 1, 30) : 3;
            result = new TableResult(r, c, header.IsChecked == true, title.Text.Trim());
        }, "Вставить"));

        return window.ShowDialog() == true ? result : null;
    }

    // ---------------------------------------------------------------- ссылка

    public sealed record XrefResult(ProjectFile File, string? TopicId, string Text);

    public static XrefResult? InsertXref(DitaProject project, string? currentFile)
    {
        var filter = new TextBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { DisplayMemberPath = "RelativePath" };
        var text = new TextBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 8, 0, 0) };

        var topics = project.Files
            .Where(f => f.Kind == Core.Model.DitaDocumentKind.Topic)
            .ToList();
        list.ItemsSource = topics;

        filter.TextChanged += (_, _) =>
        {
            var query = filter.Text.Trim();
            list.ItemsSource = query.Length == 0
                ? topics
                : topics.Where(t =>
                    t.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ProjectFile file && string.IsNullOrWhiteSpace(text.Text))
            {
                text.Text = file.Title;
            }
        };

        XrefResult? result = null;
        var root = new DockPanel { Margin = new Thickness(16) };
        var window = Shell("Вставить ссылку", root, 560, 520);
        var buttons = Buttons(window, () =>
        {
            if (list.SelectedItem is ProjectFile file)
            {
                var doc = project.TryGetDocument(file.FullPath);
                result = new XrefResult(file, doc?.Id, text.Text.Trim());
            }
        }, "Вставить");

        DockPanel.SetDock(filter, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(text, Dock.Bottom);

        root.Children.Add(filter);
        root.Children.Add(buttons);
        root.Children.Add(text);
        root.Children.Add(list);

        _ = currentFile;
        filter.Focus();
        return window.ShowDialog() == true ? result : null;
    }

    // ------------------------------------------------------------- условия

    public sealed record ConditionsResult(Dictionary<string, HashSet<string>> Exclude, bool ShowDraftComments);

    public static ConditionsResult? PublishConditions(DitaProject project, ConditionsResult? current)
    {
        var attributes = new[] { "props", "platform", "product", "audience", "otherprops", "deliveryTarget" };
        var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in project.Files)
        {
            var doc = project.TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                foreach (var attribute in attributes)
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

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Отметьте значения, которые нужно исключить из сборки.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var checks = new List<(string Attribute, string Value, CheckBox Box)>();
        foreach (var (attribute, set) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            panel.Children.Add(Label($"@{attribute}"));
            foreach (var value in set.OrderBy(v => v, StringComparer.Ordinal))
            {
                var box = new CheckBox
                {
                    Content = value,
                    Margin = new Thickness(8, 2, 0, 2),
                    IsChecked = current is not null &&
                                current.Exclude.TryGetValue(attribute, out var excluded) &&
                                excluded.Contains(value)
                };
                checks.Add((attribute, value, box));
                panel.Children.Add(box);
            }
        }

        if (checks.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "В проекте нет условных атрибутов.",
                Foreground = ThemeManager.Brush("TextMuted"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        var drafts = new CheckBox
        {
            Content = "Включать черновые комментарии (draft-comment)",
            Margin = new Thickness(0, 14, 0, 0),
            IsChecked = current?.ShowDraftComments ?? false
        };
        panel.Children.Add(drafts);

        ConditionsResult? result = null;
        var window = Shell("Условия сборки", new ScrollViewer { Content = panel }, 460, 560);
        panel.Children.Add(Buttons(window, () =>
        {
            var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (attribute, value, box) in checks)
            {
                if (box.IsChecked != true)
                {
                    continue;
                }

                if (!exclude.TryGetValue(attribute, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    exclude[attribute] = set;
                }

                set.Add(value);
            }

            result = new ConditionsResult(exclude, drafts.IsChecked == true);
        }));

        return window.ShowDialog() == true ? result : null;
    }

    // ------------------------------------------------------- колонтитулы PDF

    public sealed record PdfHeaderFooterResult(bool Show, string HeaderText, string FooterText);

    public static PdfHeaderFooterResult? PdfHeaderFooter(DitaProject project)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Колонтитулы поддерживаются только при печати через WebView2 — это происходит " +
                   "автоматически, если задан свой текст или если в системе не установлен Edge/Chrome. " +
                   "Иначе PDF печатается без колонтитулов, как и раньше.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var show = new CheckBox { Content = "Показывать колонтитулы", IsChecked = project.PdfShowHeaderFooter };
        panel.Children.Add(show);

        panel.Children.Add(Label("Текст в шапке"));
        var header = new TextBox { Text = project.PdfHeaderText ?? string.Empty, Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(header);

        panel.Children.Add(Label("Текст в подвале"));
        var footer = new TextBox { Text = project.PdfFooterText ?? string.Empty, Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(footer);

        panel.Children.Add(new TextBlock
        {
            Text = "Номер страницы и общее число страниц добавляются автоматически справа в подвале.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeManager.Brush("TextMuted"),
            Margin = new Thickness(0, 6, 0, 0)
        });

        PdfHeaderFooterResult? result = null;
        var window = Shell("Колонтитулы PDF", panel, 440, 320);
        panel.Children.Add(Buttons(window, () =>
        {
            result = new PdfHeaderFooterResult(show.IsChecked == true, header.Text, footer.Text);
        }));

        return window.ShowDialog() == true ? result : null;
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
     между страницами с повтором строки шапки (thead) на каждой странице. */

""";

    public static void CustomCss(DitaProject project)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Файл стилей подключается к каждой публикации (HTML и PDF) в дополнение к " +
                   "встроенным стилям — его правила применяются последними и могут их переопределять.",
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Label("Подключённый файл"));
        var status = new TextBlock { FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
        void RefreshStatus() => status.Text = project.CustomCssPath ?? "не подключён";
        RefreshStatus();
        panel.Children.Add(status);

        Window? window = null;

        var create = new Button { Content = "Создать новый файл…", Padding = new Thickness(12, 5, 12, 5) };
        create.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Создать файл стилей",
                Filter = "Файлы CSS|*.css",
                InitialDirectory = project.RootPath,
                FileName = "custom.css"
            };

            if (dialog.ShowDialog(window) != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, DefaultCustomCss);
            }
            catch (Exception ex)
            {
                Message("Пользовательский CSS", $"Не удалось создать файл: {ex.Message}");
                return;
            }

            project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, dialog.FileName).Replace('\\', '/'));
            RefreshStatus();
        };

        var attach = new Button
        {
            Content = "Подключить существующий…",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0)
        };
        attach.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл стилей",
                Filter = "Файлы CSS|*.css",
                InitialDirectory = project.RootPath
            };

            if (dialog.ShowDialog(window) != true)
            {
                return;
            }

            project.SetCustomCssPath(Path.GetRelativePath(project.RootPath, dialog.FileName).Replace('\\', '/'));
            RefreshStatus();
        };

        var detach = new Button
        {
            Content = "Отключить",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0)
        };
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

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(18, 5, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            IsDefault = true,
            IsCancel = true
        };

        window = Shell("Пользовательский CSS", panel, 480, 300);
        close.Click += (_, _) => window!.Close();
        panel.Children.Add(close);
        window.ShowDialog();
    }

    // ------------------------------------------------------------ о программе

    public static void About()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "DITA Studio",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Редактор технической документации на DITA 1.3.\n" +
                   "Режимы «Автор», «Исходный код» и «Предпросмотр», карты публикации, " +
                   "проверка по контент-моделям, сборка HTML и PDF.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Элементов в словаре: {Core.Schema.DitaCatalog.Default.Elements.Count}",
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = ThemeManager.Brush("TextMuted")
        });

        var window = Shell("О программе", panel, 460, 280);
        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(18, 5, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.ShowDialog();
    }

    public static void Message(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
