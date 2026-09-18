using System.Windows;

namespace DitaStudio.App;

// Переключение темы. OnElementHelp/OnAbout мигрированы в HelpViewModel —
// см. ViewModels/HelpViewModel.cs. OnToggleTheme остаётся здесь, пока не
// мигрируют Project/Map/SidePanels/Documents (от них зависят Build*-вызовы
// ниже) — тогда его тоже можно будет перенести.
public partial class MainWindow
{
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
