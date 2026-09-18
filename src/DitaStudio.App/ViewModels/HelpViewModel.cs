using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HelpViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void ShowElementHelp()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        if (def is null)
        {
            Dialogs.Message("Справка", $"Элемент <{node.Name}> отсутствует в словаре DITA 1.3.");
            return;
        }

        var allowed = def.Automaton.AllowedNames;
        var attributes = def.Attributes.Keys.OrderBy(a => a, StringComparer.Ordinal);

        Dialogs.Message($"<{def.Name}>",
            $"{def.Description}\n\n" +
            $"Модуль: {def.Domain}\n" +
            $"@class: {def.ClassAttr}\n\n" +
            $"Содержимое: {def.ModelText}\n\n" +
            $"Допустимые дочерние элементы ({allowed.Count}): {string.Join(", ", allowed.Take(40))}" +
            (allowed.Count > 40 ? "…" : string.Empty) +
            $"\n\nАтрибуты: {string.Join(", ", attributes)}");
    }

    [RelayCommand]
    private void About() => Dialogs.About();
}
