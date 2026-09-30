using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Desktop.Views;
using DitaStudio.Presentation.Services;
using DitaStudio.Presentation.ViewModels;

namespace DitaStudio.Desktop;

// Подменю «Вставить элемент» в контекстном меню режима «Автор» — то же, что меню «Вставка»
// и панель «Вставка», но прямо у курсора.
public partial class MainWindow
{
    private DocumentView CreateDocumentView(DitaProject project, DitaDocument document, IPdfPrinter pdfPrinter)
    {
        var view = new DocumentView(project, document, pdfPrinter);
        if (_livePreview)
        {
            view.ShowLivePreview = true;
        }

        view.AuthorEditor.ContextMenuBuilding += (_, items) =>
        {
            if (ViewModel.Current?.Author.CurrentNode is { } node && TableCommands.CellOf(node) is not null)
            {
                items.AddRange(BuildTableMenuItems());
                items.Add(new Separator());
            }

            if (ViewModel.Current?.Author.CurrentNode is { Name: "title" } title)
            {
                items.Add(new MenuItem
                {
                    Header = "Заголовок без номера (не в оглавлении)",
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = TocRules.IsUnnumbered(title),
                    Command = ViewModel.Insert.ToggleUnnumberedTitleCommand
                });
                items.Add(new Separator());
            }

            items.Add(BuildFormatMenu());
            items.Add(BuildInsertMenu());
        };
        return view;
    }

    /// <summary>Пункты ячейки таблицы: строки, столбцы, объединение, атрибуты.</summary>
    internal List<Control> BuildTableMenuItems()
    {
        var insert = ViewModel.Insert;
        MenuItem Table(string header, TableOperation operation) =>
            new() { Header = header, Command = insert.EditTableCommand, CommandParameter = operation };

        var cellAttributes = new MenuItem { Header = "Атрибуты ячейки…" };
        cellAttributes.Click += (_, _) => RightTabs.SelectedIndex = 0;
        var tableProperties = new MenuItem { Header = "Свойства таблицы…" };
        tableProperties.Click += (_, _) =>
        {
            if (ViewModel.Current is { } pane && pane.Author.CurrentNode is { } node &&
                TableCommands.CellOf(node) is { } cell && TableCommands.TableOf(cell) is { } table)
            {
                pane.FocusNode(table);
                RightTabs.SelectedIndex = 0;
            }
        };

        return new List<Control>
        {
            cellAttributes,
            new Separator(),
            Table("Вставить строку выше", TableOperation.InsertRowAbove),
            Table("Вставить строку ниже", TableOperation.InsertRowBelow),
            Table("Удалить строку", TableOperation.DeleteRow),
            new Separator(),
            Table("Вставить столбец слева", TableOperation.InsertColumnLeft),
            Table("Вставить столбец справа", TableOperation.InsertColumnRight),
            Table("Удалить столбец", TableOperation.DeleteColumn),
            new Separator(),
            new MenuItem { Header = "Объединить с ячейкой справа", Command = insert.MergeCellRightCommand, InputGesture = new KeyGesture(Key.Right, KeyModifiers.Control | KeyModifiers.Alt) },
            new MenuItem { Header = "Объединить с ячейкой снизу", Command = insert.MergeCellDownCommand, InputGesture = new KeyGesture(Key.Down, KeyModifiers.Control | KeyModifiers.Alt) },
            Table("Разделить ячейку", TableOperation.SplitCell),
            new Separator(),
            tableProperties
        };
    }

    /// <summary>Оформление текущего блока или выделения: выравнивание (размер и цвет — там же).</summary>
    internal MenuItem BuildFormatMenu()
    {
        var node = ViewModel.Current?.Author.CurrentNode;
        var current = node is null ? null : TextFormatting.AlignmentOf(node) ?? "left";
        var align = TextFormatting.Alignments.Select(a => (Control)new MenuItem
        {
            Header = a.Label,
            Command = ViewModel.Insert.SetAlignmentCommand,
            CommandParameter = a.Token,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = current == a.Css
        }).ToList();

        var sizes = ViewModel.Insert.FontSizes.Select(size => (Control)new MenuItem
        {
            Header = size is InsertViewModel.NormalSize or InsertViewModel.CustomSize ? size : size + " пт",
            Command = ViewModel.Insert.SetFontSizeCommand,
            CommandParameter = size
        }).ToList();

        var colors = TextFormatting.Colors.Select(c => (Control)new MenuItem
        {
            Header = c.Label,
            Icon = Swatch(c.Hex),
            Command = ViewModel.Insert.SetTextColorCommand,
            CommandParameter = c.Token
        }).Append(new Separator())
          .Append(new MenuItem { Header = "Без цвета", Command = ViewModel.Insert.SetTextColorCommand, CommandParameter = null })
          .ToList();

        var placeable = PagePlacement.PlaceableFor(node);
        var place = placeable is null ? null : PagePlacement.Of(placeable);
        var positions = new List<Control>
        {
            new MenuItem
            {
                Header = "Обычное (в тексте)",
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = place is null,
                Command = ViewModel.Insert.SetPagePlacementCommand,
                CommandParameter = null
            },
            new Separator()
        };
        positions.AddRange(PagePlacement.Positions.Select(p => (Control)new MenuItem
        {
            Header = p.Label,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = place == p.Token,
            Command = ViewModel.Insert.SetPagePlacementCommand,
            CommandParameter = p.Token
        }));

        return new MenuItem
        {
            Header = "Оформление",
            ItemsSource = new List<Control>
            {
                new MenuItem { Header = "Выравнивание", ItemsSource = align },
                new MenuItem { Header = "Размер шрифта", ItemsSource = sizes },
                new MenuItem { Header = "Цвет текста", ItemsSource = colors },
                new Separator(),
                new MenuItem
                {
                    Header = "Нумерованный абзац (2.3.1)",
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = node is { Name: "p" } && HeadingNumbering.IsNumbered(node),
                    IsEnabled = node is { Name: "p" },
                    Command = ViewModel.Insert.ToggleNumberedParagraphCommand
                },
                new MenuItem
                {
                    Header = "Положение на листе (PDF, DOCX)",
                    ItemsSource = positions,
                    IsEnabled = placeable is not null
                }
            }
        };
    }

    private static Border Swatch(string hex) => new()
    {
        Width = 14,
        Height = 14,
        CornerRadius = new Avalonia.CornerRadius(2),
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex))
    };

    /// <summary>Палитра кнопки «Цвет текста» на панели: образцы цветов и «Без цвета».</summary>
    private void BuildColorPalette()
    {
        var panel = new WrapPanel { Width = 150, Margin = new Avalonia.Thickness(4) };
        foreach (var (token, label, hex) in TextFormatting.Colors)
        {
            var swatch = new Button
            {
                Content = Swatch(hex),
                Padding = new Avalonia.Thickness(4),
                Margin = new Avalonia.Thickness(2),
                Command = ViewModel.Insert.SetTextColorCommand,
                CommandParameter = token
            };
            ToolTip.SetTip(swatch, label);
            swatch.Click += (_, _) => ColorButton.Flyout?.Hide();
            panel.Children.Add(swatch);
        }

        var none = new Button
        {
            Content = "Без цвета",
            Margin = new Avalonia.Thickness(2, 6, 2, 2),
            Command = ViewModel.Insert.SetTextColorCommand,
            CommandParameter = null
        };
        none.Click += (_, _) => ColorButton.Flyout?.Hide();
        panel.Children.Add(none);
        ColorButton.Flyout = new Flyout { Content = panel };
    }

    /// <summary>Список размеров на панели: применить и сбросить выбор (можно выбрать тот же снова).</summary>
    private void OnFontSizeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string size } box)
        {
            ViewModel.Insert.SetFontSizeCommand.Execute(size);
            box.SelectedItem = null;
        }
    }

    /// <summary>
    /// Частые вставки, затем все допустимые у курсора элементы: фразовые — в строку текста,
    /// блочные — после текущего блока; «Все элементы…» открывает панель «Вставка».
    /// </summary>
    internal MenuItem BuildInsertMenu()
    {
        var insert = ViewModel.Insert;
        var (inline, after) = insert.InsertCandidates();
        var items = new List<Control>
        {
            CommandItem("Сноска", insert.InsertFootnoteCommand),
            CommandItem("Изображение…", insert.InsertImageCommand),
            CommandItem("Оформить изображение как рисунок", insert.WrapImageAsFigureCommand),
            CommandItem("Подпись рисунка / таблицы: добавить или убрать", insert.ToggleCaptionCommand),
            CommandItem("Перекрёстная ссылка…", insert.InsertXrefCommand),
            CommandItem("Таблица…", insert.InsertTableCommand),
            new Separator(),
            ElementsItem("В строку текста", inline),
            ElementsItem("Блок после текущего", after),
            new Separator()
        };

        var all = new MenuItem { Header = "Все элементы…", InputGesture = new KeyGesture(Key.E, KeyModifiers.Control) };
        all.Click += (_, _) => FocusPalette();
        items.Add(all);
        return new MenuItem { Header = "Вставить элемент", ItemsSource = items };
    }

    private static MenuItem CommandItem(string header, ICommand command) => new() { Header = header, Command = command };

    private MenuItem ElementsItem(string header, IReadOnlyList<ElementDef> defs)
    {
        var children = defs.Select(def =>
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(def.Description) ? $"<{def.Name}>" : $"{def.Description}  <{def.Name}>"
            };
            item.Click += (_, _) => ViewModel.Insert.InsertElementCommand.Execute(def.Name);
            return item;
        }).ToList();

        return new MenuItem { Header = header, ItemsSource = children, IsEnabled = children.Count > 0 };
    }
}
