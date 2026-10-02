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
using DitaStudio.Core.Localization;

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
                    Header = Loc.T("Menu_UnnumberedTitle"),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = TocRules.IsUnnumbered(title),
                    Command = ViewModel.Insert.ToggleUnnumberedTitleCommand
                });
                items.Add(new Separator());
            }

            var wrap = new MenuItem { Header = Loc.T("Menu_WrapIn"), IsEnabled = view.AuthorEditor.WrapOptions().Count > 0 };
            wrap.Click += (_, _) => view.AuthorEditor.ShowWrapMenu();
            items.Add(wrap);
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

        var cellAttributes = new MenuItem { Header = Loc.T("Win_CellAttributes") };
        cellAttributes.Click += (_, _) => RightTabs.SelectedIndex = 0;
        var tableProperties = new MenuItem { Header = Loc.T("Win_TableProperties") };
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
            Table(Loc.T("Menu_InsertRowAbove"), TableOperation.InsertRowAbove),
            Table(Loc.T("Menu_InsertRowBelow"), TableOperation.InsertRowBelow),
            Table(Loc.T("Menu_DeleteRow"), TableOperation.DeleteRow),
            new Separator(),
            Table(Loc.T("Menu_InsertColumnLeft"), TableOperation.InsertColumnLeft),
            Table(Loc.T("Menu_InsertColumnRight"), TableOperation.InsertColumnRight),
            Table(Loc.T("Menu_DeleteColumn"), TableOperation.DeleteColumn),
            new Separator(),
            new MenuItem { Header = Loc.T("Win_MergeWithTheCellOnThe"), Command = insert.MergeCellRightCommand, InputGesture = new KeyGesture(Key.Right, KeyModifiers.Control | KeyModifiers.Alt) },
            new MenuItem { Header = Loc.T("Win_MergeWithTheCellBelow"), Command = insert.MergeCellDownCommand, InputGesture = new KeyGesture(Key.Down, KeyModifiers.Control | KeyModifiers.Alt) },
            Table(Loc.T("Menu_SplitCell"), TableOperation.SplitCell),
            new Separator(),
            new MenuItem { Header = Loc.T("Menu_SelectRow"), Command = insert.SelectTableRowCommand },
            new MenuItem { Header = Loc.T("Menu_SelectColumn"), Command = insert.SelectTableColumnCommand },
            new MenuItem { Header = Loc.T("Menu_Borders"), ItemsSource = Authoring.AuthorView.BorderMenuItems((edges, _) => insert.SetCellBordersCommand.Execute(edges)) },
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
            Header = size == InsertViewModel.NormalSize || size == InsertViewModel.CustomSize ? size : size + Loc.T("Win_Pt"),
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
          .Append(new MenuItem { Header = Loc.T("Win_NoColor"), Command = ViewModel.Insert.SetTextColorCommand, CommandParameter = null })
          .ToList();

        var marks = MarkerMenuItems();

        var placeable = PagePlacement.PlaceableFor(node);
        var place = placeable is null ? null : PagePlacement.Of(placeable);
        var positions = new List<Control>
        {
            new MenuItem
            {
                Header = Loc.T("Menu_PlacementNormal"),
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
            Header = Loc.T("Win_Formatting"),
            ItemsSource = new List<Control>
            {
                new MenuItem { Header = Loc.T("Win_Alignment"), ItemsSource = align },
                new MenuItem { Header = Loc.T("Win_FontSize"), ItemsSource = sizes },
                new MenuItem { Header = Loc.T("Win_TextColor"), ItemsSource = colors },
                new MenuItem { Header = Loc.T("Win_Marker"), ItemsSource = marks },
                new Separator(),
                new MenuItem
                {
                    Header = Loc.T("Menu_NumberedParagraph"),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = node is { Name: "p" } && HeadingNumbering.IsNumbered(node),
                    IsEnabled = node is { Name: "p" },
                    Command = ViewModel.Insert.ToggleNumberedParagraphCommand
                },
                new MenuItem
                {
                    Header = Loc.T("Menu_PagePlacement"),
                    ItemsSource = positions,
                    IsEnabled = placeable is not null
                }
            }
        };
    }

    /// <summary>Пункты маркера: четыре цвета, «Другой цвет…» и «Нет цвета» — для меню «Оформление» и палитры кнопки.</summary>
    private List<Control> MarkerMenuItems()
    {
        var items = TextFormatting.Marks.Select(m => (Control)new MenuItem
        {
            Header = m.Label,
            Icon = Swatch(m.Hex),
            Command = ViewModel.Insert.SetMarkerCommand,
            CommandParameter = m.Token
        }).ToList();
        items.Add(new MenuItem { Header = Loc.T("Win_MoreColors"), Command = ViewModel.Insert.SetMarkerCommand, CommandParameter = InsertViewModel.CustomMarker });
        items.Add(new Separator());
        items.Add(new MenuItem { Header = Loc.T("Win_NoColor2"), Command = ViewModel.Insert.SetMarkerCommand, CommandParameter = null });
        return items;
    }

    /// <summary>Меню «Границы» как в Word: у кнопки на панели и в «Структура → Таблица → Границы выделенных ячеек».</summary>
    private void BuildCellBordersMenus()
    {
        List<Control> Items() => Authoring.AuthorView.BorderMenuItems((edges, _) => ViewModel.Insert.SetCellBordersCommand.Execute(edges));
        CellBordersMenu.ItemsSource = Items();
        CellBordersButton.Flyout = new MenuFlyout { ItemsSource = Items() };
    }

    /// <summary>Палитра кнопки «Маркер» на панели: цвета, «Другой цвет…», «Нет цвета» (с выделением снимает маркер, без — ластик).</summary>
    private void BuildMarkerPalette()
    {
        var panel = new StackPanel { Width = 170, Margin = new Avalonia.Thickness(4) };
        var swatches = new WrapPanel();
        foreach (var (token, label, hex) in TextFormatting.Marks)
        {
            var swatch = new Button
            {
                Content = Swatch(hex),
                Padding = new Avalonia.Thickness(4),
                Margin = new Avalonia.Thickness(2),
                Command = ViewModel.Insert.SetMarkerCommand,
                CommandParameter = token
            };
            ToolTip.SetTip(swatch, label);
            swatch.Click += (_, _) => MarkerButton.Flyout?.Hide();
            swatches.Children.Add(swatch);
        }

        panel.Children.Add(swatches);
        var custom = new Button
        {
            Content = Loc.T("Win_MoreColors"),
            Margin = new Avalonia.Thickness(2, 6, 2, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Command = ViewModel.Insert.SetMarkerCommand,
            CommandParameter = InsertViewModel.CustomMarker
        };
        custom.Click += (_, _) => MarkerButton.Flyout?.Hide();
        panel.Children.Add(custom);
        var none = new Button
        {
            Content = Loc.T("Win_NoColor2"),
            Margin = new Avalonia.Thickness(2, 4, 2, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Command = ViewModel.Insert.SetMarkerCommand,
            CommandParameter = null
        };
        none.Click += (_, _) => MarkerButton.Flyout?.Hide();
        panel.Children.Add(none);
        MarkerButton.Flyout = new Flyout { Content = panel };
    }

    /// <summary>Кнопка «Маркер»: подсвечена, пока включена кисть; полоска под буквой — цвет кисти.</summary>
    internal void RefreshMarkerButton()
    {
        var author = ViewModel.Current?.Author;
        var active = author?.MarkerPenActive == true;
        MarkerButton.Classes.Set("active", active);
        if (active && author!.MarkerPenToken is { } token && TextFormatting.ParseMarkToken(token) is { } hex)
        {
            MarkerSwatch.Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        }
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
            Content = Loc.T("Win_NoColor"),
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
            CommandItem(Loc.T("Menu_Footnote"), insert.InsertFootnoteCommand),
            CommandItem(Loc.T("Menu_InsertImage"), insert.InsertImageCommand),
            CommandItem(Loc.T("Menu_ImageAsFigure"), insert.WrapImageAsFigureCommand),
            CommandItem(Loc.T("Menu_ToggleCaption"), insert.ToggleCaptionCommand),
            CommandItem(Loc.T("Menu_CrossReference"), insert.InsertXrefCommand),
            CommandItem(Loc.T("Menu_InsertTable"), insert.InsertTableCommand),
            new Separator(),
            ElementsItem(Loc.T("Win_IntoTheLineOfText"), inline),
            ElementsItem(Loc.T("Win_BlockAfterTheCurrentOne"), after),
            new Separator()
        };

        var all = new MenuItem { Header = Loc.T("Win_AllElements"), InputGesture = new KeyGesture(Key.E, KeyModifiers.Control) };
        all.Click += (_, _) => FocusPalette();
        items.Add(all);
        return new MenuItem { Header = Loc.T("Win_InsertElement"), ItemsSource = items };
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
