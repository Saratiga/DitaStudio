using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Вставка элементов (абзац, список, сноска), инлайн-форматирование, перестановка и удаление
// элемента, отмена. Класс разбит по областям (partial): .Tables — таблицы и ячейки;
// .ImagesAndLinks — изображения, рисунки, ссылки, id и conref; .Text — оформление текста,
// маркер, нумерация, правки рецензента. Узел таблицы по параметрам диалога строит TableNodeBuilder.
public partial class InsertViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly IDocumentHost _docs;

    [ObservableProperty]
    private bool showElementTags = true;

    public InsertViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _docs = docs;

        // Список размеров шрифта строится на текущем языке — пересобирается при его смене.
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(FontSizes));
    }

    // [RelayCommand] — не только для InsertParagraph/InsertUl/и т.п. ниже,
    // но и для палитры вставки (MainWindow.SidePanels.cs, не мигрирована),
    // которая вызывает произвольное имя элемента из каталога по double-click.
    [RelayCommand]
    private void InsertElement(string name)
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            return;
        }

        pane.Mode = EditorMode.Author;
        if (!pane.Author.InsertElement(name))
        {
            _shell.StatusText = Loc.T("Msg_TheElement0IsNotAllowed", name);
            return;
        }

        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        _shell.StatusText = Loc.T("Msg_Inserted0", name);
    }

    /// <summary>
    /// Что можно вставить у курсора текущей вкладки: фразовые элементы — в строку текущего блока,
    /// блочные — после него (как это делает <see cref="InsertElementCommand"/>). По описанию.
    /// </summary>
    public (IReadOnlyList<ElementDef> Inline, IReadOnlyList<ElementDef> After) InsertCandidates()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            return (Array.Empty<ElementDef>(), Array.Empty<ElementDef>());
        }

        var catalog = DitaCatalog.Default;
        var inline = catalog.Get(node.Name) is { IsMixed: true }
            ? catalog.InsertableAt(node, DitaCatalog.ChildNames(node).Count)
                .Where(d => d.Display is DisplayKind.Inline or DisplayKind.Empty)
                .ToList()
            : new List<ElementDef>();

        var after = node.Parent is { } parent
            ? catalog.InsertableAt(parent, EditCommands.ElementIndexOf(parent, node) + 1)
                .Where(d => d.Display is not (DisplayKind.Inline or DisplayKind.Empty))
                .ToList()
            : new List<ElementDef>();

        static List<ElementDef> Sorted(IEnumerable<ElementDef> defs) =>
            defs.DistinctBy(d => d.Name)
                .OrderBy(d => string.IsNullOrEmpty(d.Description) ? d.Name : d.Description, StringComparer.CurrentCulture)
                .ToList();

        return (Sorted(inline), Sorted(after));
    }

    [RelayCommand]
    private void InsertParagraph() => InsertElement("p");

    [RelayCommand]
    private void InsertSection() => InsertElement("section");

    [RelayCommand]
    private void InsertUl() => InsertElement("ul");

    [RelayCommand]
    private void InsertOl() => InsertElement("ol");

    [RelayCommand]
    private void InsertNote() => InsertElement("note");

    [RelayCommand]
    private void InsertCodeblock() => InsertElement("codeblock");

    [RelayCommand]
    private void InsertFootnote() => InsertElement("fn");

    private void Format(string element)
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            return;
        }

        if (!pane.Author.WrapCurrentInline(element))
        {
            _shell.StatusText = Loc.T("Msg_SelectTextInAuthorMode");
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    [RelayCommand]
    private void FormatBold() => Format("b");

    [RelayCommand]
    private void FormatItalic() => Format("i");

    [RelayCommand]
    private void FormatUnderline() => Format("u");

    [RelayCommand]
    private void FormatCode() => Format("codeph");

    [RelayCommand]
    private void FormatUicontrol() => Format("uicontrol");

    [RelayCommand]
    private void MoveUp() => MoveElement(true);

    [RelayCommand]
    private void MoveDown() => MoveElement(false);

    private void MoveElement(bool up)
    {
        if (_docs.Current?.Author.MoveCurrent(up) == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
        }
    }

    [RelayCommand]
    private async Task DeleteElement()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            return;
        }

        if (!await _ui.Dialogs.ConfirmAsync(Loc.T("Msg_Delete"), Loc.T("Msg_DeleteTheElement0WithIts", node.Name)))
        {
            return;
        }

        if (_docs.Current?.Author.DeleteCurrent() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
        }
    }

    /// <summary>Выполняет выбранную команду плагина (см. IAuthorCommandPlugin) на открытом
    /// документе — пункт меню один и тот же для любого числа подключённых плагинов.</summary>
    [RelayCommand]
    private async Task RunAuthorCommandPlugin()
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PluginCommand"), Loc.T("Msg_OpenADocument"));
            return;
        }

        var command = await _ui.Dialogs.PickOneAsync(Loc.T("Msg_PluginCommand"), Loc.T("Msg_ChooseACommand"), PluginRegistry.AuthorCommands, c => c.Name);
        if (command is null)
        {
            return;
        }

        pane.CommitPendingEdits();

        try
        {
            command.Execute(pane);
        }
        catch (Exception ex)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PluginCommand"), Loc.T("Msg_ThePlugin0Failed1", command.Name, ex.Message));
            return;
        }

        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = Loc.T("Msg_PluginCommand0Completed", command.Name);
    }

    partial void OnShowElementTagsChanged(bool value)
    {
        foreach (var pane in _docs.Panes.Values)
        {
            pane.Author.ShowElementTags = value;
            pane.Author.Rebuild();
        }
    }

    [RelayCommand]
    private void Undo()
    {
        _docs.Current?.PerformUndo();
        _shell.StatusText = Loc.T("Msg_Undone");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _hooks.RefreshMapTree?.Invoke();
    }

    [RelayCommand]
    private void Redo()
    {
        _docs.Current?.PerformRedo();
        _shell.StatusText = Loc.T("Msg_Redone");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
        // Отмена правки карты заменяет её узлы новыми — строки дерева карты ссылались бы на старые
        // и команды меню действовали бы на узлы, которых уже нет в документе.
        _hooks.RefreshMapTree?.Invoke();
    }
}
