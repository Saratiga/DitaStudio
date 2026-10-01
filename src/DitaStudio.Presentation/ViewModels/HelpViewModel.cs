using DitaStudio.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Schema;

namespace DitaStudio.Presentation.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly UiServices _ui;
    private readonly IDocumentHost _docs;

    public HelpViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _ui = context.Ui;
        _docs = docs;
    }

    [RelayCommand]
    private async Task ShowElementHelp()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        if (def is null)
        {
            await _ui.Dialogs.MessageAsync("Справка", $"Элемент <{node.Name}> отсутствует в словаре DITA 1.3.");
            return;
        }

        var allowed = def.Automaton.AllowedNames;
        var attributes = def.Attributes.Keys.OrderBy(a => a, StringComparer.Ordinal);

        await _ui.Dialogs.MessageAsync($"<{def.Name}>",
            $"{def.Description}\n\n" +
            $"Модуль: {def.Domain}\n" +
            $"@class: {def.ClassAttr}\n\n" +
            $"Содержимое: {def.ModelText}\n\n" +
            $"Допустимые дочерние элементы ({allowed.Count}): {string.Join(", ", allowed.Take(40))}" +
            (allowed.Count > 40 ? "…" : string.Empty) +
            $"\n\nАтрибуты: {string.Join(", ", attributes)}");
    }

    [RelayCommand]
    private Task About() => _ui.Dialogs.AboutAsync();
}
