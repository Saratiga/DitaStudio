using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
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
        view.AuthorEditor.ContextMenuBuilding += (_, items) => items.Add(BuildInsertMenu());
        return view;
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
