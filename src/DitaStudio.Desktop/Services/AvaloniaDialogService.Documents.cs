using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

// Диалоги документов и ссылок: новый документ, таблица, перекрёстная ссылка, таблица
// соответствий, переименование id и файла, вынесение в conref.
public sealed partial class AvaloniaDialogService
{
    // ------------------------------------------------------------ новый файл

    public async Task<NewDocumentResult?> NewDocumentAsync(string projectRoot, IEnumerable<string> folders, string? preselectedFolder)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        var templates = new ListBox
        {
            Height = 170,
            ItemsSource = DocumentTemplates.All,
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<DocumentTemplate>((template, _) =>
            {
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = template?.DisplayName, FontWeight = FontWeight.SemiBold });
                stack.Children.Add(Muted(new TextBlock { Text = template?.Description, FontSize = 11 }));
                return stack;
            })
        };

        var titleBox = Input();
        var fileBox = Input();
        var folderList = folders.ToList();
        var folderBox = new ComboBox
        {
            ItemsSource = folderList,
            Padding = new Thickness(4, 3, 4, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = preselectedFolder ?? folderList.FirstOrDefault()
        };

        // Имя файла предлагается по заголовку, пока пользователь не задал его сам.
        var manual = false;
        fileBox.TextChanged += (_, _) => manual = fileBox.IsKeyboardFocusWithin || manual;
        void SuggestFileName()
        {
            if (manual)
            {
                return;
            }

            var template = templates.SelectedItem as DocumentTemplate ?? DocumentTemplates.All[0];
            fileBox.Text = DocumentTemplates.SuggestId(titleBox.Text ?? string.Empty, template.RootElement) + template.Extension;
        }

        titleBox.TextChanged += (_, _) => SuggestFileName();
        templates.SelectionChanged += (_, _) => SuggestFileName();

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
            result = new NewDocumentResult(template, title, fileName, folderBox.SelectedItem as string ?? projectRoot);
        }, "Создать"));

        window.Opened += (_, _) => titleBox.Focus();
        return await ShowAsync(window) ? result : null;
    }

    // ------------------------------------------------------------ таблица

    public async Task<TableResult?> InsertTableAsync()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var rows = Input("3");
        var cols = Input("3");
        var header = new CheckBox { Content = "Строка заголовков", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
        var title = Input();

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
            result = new TableResult(r, c, header.IsChecked == true, (title.Text ?? string.Empty).Trim());
        }, "Вставить"));

        return await ShowAsync(window) ? result : null;
    }

    // ------------------------------------------------------------ ссылка

    /// <summary>Список топиков проекта с фильтром по пути и заголовку.</summary>
    private static (TextBox Filter, ListBox List) TopicPicker(DitaProject project)
    {
        var filter = new TextBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { DisplayMemberBinding = new Binding(nameof(ProjectFile.RelativePath)) };
        var topics = project.Files.Where(f => f.Kind == DitaDocumentKind.Topic).ToList();
        list.ItemsSource = topics;

        filter.TextChanged += (_, _) =>
        {
            var query = (filter.Text ?? string.Empty).Trim();
            list.ItemsSource = query.Length == 0
                ? topics
                : topics.Where(t => t.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };

        return (filter, list);
    }

    public async Task<XrefResult?> InsertXrefAsync(DitaProject project, string? currentFile)
    {
        var (filter, list) = TopicPicker(project);
        var text = new TextBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 8, 0, 0) };

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
                result = new XrefResult(file, doc?.Id, (text.Text ?? string.Empty).Trim());
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
        window.Opened += (_, _) => filter.Focus();
        return await ShowAsync(window) ? result : null;
    }

    // ------------------------------------------------ таблица соответствий

    public async Task<List<List<RelTableCell>>?> EditRelTableAsync(DitaProject project, List<List<RelTableCell>> initialRows)
    {
        var rows = initialRows.Count > 0
            ? initialRows.Select(r => r.Select(c => new RelTableCell { File = c.File, TopicId = c.TopicId }).ToList()).ToList()
            : new List<List<RelTableCell>> { new() { new RelTableCell(), new RelTableCell() } };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Muted(Wrapped(
            "Каждая строка связывает между собой топики из разных столбцов — при публикации " +
            "они попадут друг другу в «Смотрите также». Топики одного столбца друг с другом не связываются.", 10)));

        var grid = new Grid();
        var scroller = new ScrollViewer
        {
            Content = grid,
            MaxHeight = 340,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        void Rebuild()
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            var cols = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
            for (var c = 0; c < cols; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(160, GridUnitType.Pixel));
            }

            for (var r = 0; r < rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var cell = rows[r][c];
                    var cellPanel = new DockPanel { Margin = new Thickness(3) };

                    if (cell.File is not null)
                    {
                        var clear = new Button
                        {
                            Content = "✕",
                            FontSize = 10,
                            Padding = new Thickness(4, 0, 4, 0),
                            BorderThickness = new Thickness(0),
                            Background = Brushes.Transparent
                        };
                        ToolTip.SetTip(clear, "Очистить ячейку");
                        clear.Click += (_, _) =>
                        {
                            cell.File = null;
                            cell.TopicId = null;
                            Rebuild();
                        };
                        DockPanel.SetDock(clear, Dock.Right);
                        cellPanel.Children.Add(clear);
                    }

                    var pick = new Button
                    {
                        Content = cell.File?.Title ?? "+ выбрать топик",
                        Padding = new Thickness(6, 4, 6, 4),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    pick.Click += async (_, _) =>
                    {
                        var picked = await InsertXrefAsync(project, null);
                        if (picked is not null)
                        {
                            cell.File = picked.File;
                            cell.TopicId = picked.TopicId;
                            Rebuild();
                        }
                    };
                    cellPanel.Children.Add(pick);

                    Grid.SetRow(cellPanel, r);
                    Grid.SetColumn(cellPanel, c);
                    grid.Children.Add(cellPanel);
                }
            }
        }

        Rebuild();

        Button ToolbarButton(string text, Action action, bool first = false)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(first ? 0 : 6, 0, 0, 0) };
            button.Click += (_, _) => action();
            return button;
        }

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(ToolbarButton("+ Строка", () =>
        {
            var width = rows.Count > 0 ? rows[0].Count : 2;
            rows.Add(Enumerable.Range(0, width).Select(_ => new RelTableCell()).ToList());
            Rebuild();
        }, first: true));
        toolbar.Children.Add(ToolbarButton("+ Столбец", () =>
        {
            foreach (var row in rows)
            {
                row.Add(new RelTableCell());
            }

            Rebuild();
        }));
        toolbar.Children.Add(ToolbarButton("− Строка", () =>
        {
            if (rows.Count > 1)
            {
                rows.RemoveAt(rows.Count - 1);
                Rebuild();
            }
        }));
        toolbar.Children.Add(ToolbarButton("− Столбец", () =>
        {
            if (rows.Count > 0 && rows[0].Count > 1)
            {
                foreach (var row in rows)
                {
                    row.RemoveAt(row.Count - 1);
                }

                Rebuild();
            }
        }));

        panel.Children.Add(toolbar);
        panel.Children.Add(scroller);

        List<List<RelTableCell>>? result = null;
        var window = Shell("Таблица соответствий", panel, 640, 560);
        panel.Children.Add(Buttons(window, () => result = rows));
        return await ShowAsync(window) ? result : null;
    }

    // ------------------------------------------------------------ рефакторинг

    private async Task<string?> RenameAsync(string title, string currentLabel, string current, string hint, string okText, double height)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label(currentLabel));
        panel.Children.Add(new SelectableTextBlock { Text = current, FontFamily = Mono, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Label(title == "Переименовать id" ? "Новый id" : "Новый путь"));
        var box = Input(current);
        panel.Children.Add(box);
        panel.Children.Add(Muted(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }));

        string? result = null;
        var window = Shell(title, panel, 460, height);
        panel.Children.Add(Buttons(window, () =>
        {
            var value = (box.Text ?? string.Empty).Trim();
            if (value.Length > 0)
            {
                result = value;
            }
        }, okText));

        window.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return await ShowAsync(window) ? result : null;
    }

    public async Task<string?> PromptTextAsync(string title, string label, string initial, string hint, string okText = "ОК")
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label(label));
        var box = Input(initial);
        panel.Children.Add(box);
        panel.Children.Add(Muted(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }));

        string? result = null;
        var window = Shell(title, panel, 420, 220);
        panel.Children.Add(Buttons(window, () =>
        {
            var value = (box.Text ?? string.Empty).Trim();
            if (value.Length > 0)
            {
                result = value;
            }
        }, okText));
        window.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return await ShowAsync(window) ? result : null;
    }

    public Task<string?> RenameIdAsync(string currentId) => RenameAsync(
        "Переименовать id", "Текущий id", currentId,
        "Ссылки на этот id (href, conref) во всём проекте будут обновлены автоматически.",
        "Переименовать", 260);

    public Task<string?> RenameFileAsync(string currentRelativePath) => RenameAsync(
        "Переименовать / переместить файл", "Текущий путь (относительно проекта)", currentRelativePath,
        "Можно указать другую папку через «/» — она будет создана при необходимости. " +
        "Ссылки на этот файл (href, conref) во всём проекте будут пересчитаны автоматически.",
        "Перенести", 300);

    public async Task<ExtractToConrefResult?> ExtractToConrefAsync(DitaProject project, string suggestedId)
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var intro = Wrapped("Содержимое элемента переносится в целевой топик целиком, а на исходном месте " +
                            "остаётся пустая ссылка conref — редактировать текст затем нужно в целевом топике.", 10);
        var idLabel = Label("id вынесенного элемента");
        var idBox = new TextBox { Text = suggestedId, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 10) };
        var existingRadio = new RadioButton { Content = "В существующий топик", IsChecked = true, GroupName = "target" };
        var newRadio = new RadioButton { Content = "В новый файл", GroupName = "target", Margin = new Thickness(0, 4, 0, 0) };
        var newFileBox = new TextBox
        {
            Text = "reusable/shared.dita",
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(20, 4, 0, 10),
            IsEnabled = false
        };
        var (filter, list) = TopicPicker(project);
        filter.Margin = new Thickness(0, 8, 0, 4);

        existingRadio.IsCheckedChanged += (_, _) =>
        {
            var existing = existingRadio.IsChecked == true;
            list.IsEnabled = existing;
            filter.IsEnabled = existing;
            newFileBox.IsEnabled = !existing;
        };

        foreach (var control in new Control[] { intro, idLabel, idBox, existingRadio, newRadio, newFileBox, filter })
        {
            DockPanel.SetDock(control, Dock.Top);
        }

        ExtractToConrefResult? result = null;
        var window = Shell("Вынести в conref", root, 520, 560);
        var buttons = Buttons(window, () =>
        {
            var id = (idBox.Text ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return;
            }

            if (existingRadio.IsChecked == true)
            {
                if (list.SelectedItem is ProjectFile file)
                {
                    result = new ExtractToConrefResult { TargetFile = file, ElementId = id };
                }
            }
            else if ((newFileBox.Text ?? string.Empty).Trim() is { Length: > 0 } name)
            {
                result = new ExtractToConrefResult { NewFileName = name, ElementId = id };
            }
        }, "Вынести");
        DockPanel.SetDock(buttons, Dock.Bottom);

        root.Children.Add(intro);
        root.Children.Add(idLabel);
        root.Children.Add(idBox);
        root.Children.Add(existingRadio);
        root.Children.Add(newRadio);
        root.Children.Add(newFileBox);
        root.Children.Add(filter);
        root.Children.Add(buttons);
        root.Children.Add(list);

        window.Opened += (_, _) => idBox.Focus();
        return await ShowAsync(window) ? result : null;
    }
}
