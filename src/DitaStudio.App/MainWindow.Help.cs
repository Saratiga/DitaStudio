using System.Windows;
using DitaStudio.App.Views;
using DitaStudio.Core.Schema;

namespace DitaStudio.App;

// Справка по элементу под курсором, «О программе», переключение темы.
public partial class MainWindow
{
    private void OnElementHelp(object sender, RoutedEventArgs e) => ShowElementHelp();

    private void ShowElementHelp()
    {
        var node = Current?.Author.CurrentNode;
        if (node is null)
        {
            UpdateStatus("Поставьте курсор в элемент.");
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

    private void OnAbout(object sender, RoutedEventArgs e) => Dialogs.About();

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        ThemeManager.ApplyTitleBar(this);

        // Раскрашенные в коде части (дерево проекта, карта, структура, атрибуты, открытые
        // документы в режиме «Автор») не следят за DynamicResource сами по себе — перестраиваем
        // их, чтобы цвета подхватились сразу.
        BuildProjectTree();
        BuildMapTree();
        BuildOutline();
        BuildAttributePanel();
        BuildPalette();

        foreach (var pane in _panes.Values)
        {
            pane.Author.Rebuild();
        }
    }
}
