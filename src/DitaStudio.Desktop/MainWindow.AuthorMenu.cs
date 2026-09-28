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

namespace DitaStudio.Desktop;

// Подменю «Вставить элемент» в контекстном меню режима «Автор» — то же, что меню «Вставка»
// и панель «Вставка», но прямо у курсора.
public partial class MainWindow
{
    private DocumentView CreateDocumentView(DitaProject project, DitaDocument document, IPdfPrinter pdfPrinter)
    {
        var view = new DocumentView(project, document, pdfPrinter);
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

        return new MenuItem
        {
            Header = "Оформление",
            ItemsSource = new List<Control> { new MenuItem { Header = "Выравнивание", ItemsSource = align } }
        };
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
