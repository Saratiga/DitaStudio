using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.App.Views;

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

    public HelpViewModel Help { get; }

    public MainViewModel()
    {
        Help = new HelpViewModel(this);
    }
}
