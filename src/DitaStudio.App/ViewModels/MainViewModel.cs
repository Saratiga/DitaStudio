using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;

namespace DitaStudio.App.ViewModels;

// Корневая VM окна. Дочерние VM (Help и далее) добавляются по мере миграции
// соответствующих областей — см.
// docs/superpowers/specs/2026-09-18-mainwindow-mvvm-design.md.
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Готово";

    // До миграции области Documents (шаг 5 спеки) актуальное значение сюда
    // проставляет OnDocumentTabChanged в MainWindow.Documents.cs — это
    // временный мост, а не источник истины.
    [ObservableProperty]
    private DocumentPane? current;

    // До миграции области Project (шаг 6 спеки) актуальное значение сюда
    // проставляет LoadProject в MainWindow.Project.cs.
    [ObservableProperty]
    private DitaProject? project;

    // Тот же экземпляр словаря, что MainWindow.xaml.cs держит в _panes — не
    // копия, поэтому не нуждается в отдельном мосте на изменение содержимого.
    public IReadOnlyDictionary<string, DocumentPane> Panes { get; }

    // Временные мосты к ещё не мигрированной области Documents. Заменяются
    // на прямые вызовы VM-команд, когда область мигрирует (шаг 5 спеки).
    public Action? UpdateTabHeaders { get; set; }
    public Func<string, DocumentPane?>? OpenDocument { get; set; }

    public HelpViewModel Help { get; }
    public SearchViewModel Search { get; }

    public MainViewModel(IReadOnlyDictionary<string, DocumentPane> panes)
    {
        Panes = panes;
        Help = new HelpViewModel(this);
        Search = new SearchViewModel(this);
    }
}
