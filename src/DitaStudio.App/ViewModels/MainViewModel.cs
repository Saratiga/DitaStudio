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

    // Производное от Documents.SelectedTab — сама вкладка теперь источник
    // истины (шаг 5 спеки), не отдельное наблюдаемое поле.
    public DocumentPane? Current => Documents.SelectedTab?.Pane;

    // До миграции области Project (шаг 6 спеки) актуальное значение сюда
    // проставляет LoadProject в MainWindow.Project.cs.
    [ObservableProperty]
    private DitaProject? project;

    // Тот же экземпляр словаря, что MainWindow.xaml.cs держит в _panes — не
    // копия. DocumentsViewModel пишет в него напрямую (Add/Remove), поэтому
    // тип — мутируемый Dictionary, а не IReadOnlyDictionary.
    public Dictionary<string, DocumentPane> Panes { get; }

    // Временные мосты к ещё не мигрированным областям Project/SidePanels.
    // Заменяются на прямые вызовы VM-команд, когда области мигрируют.
    public Action? UpdateTabHeaders { get; set; }
    public Func<string, DocumentPane?>? OpenDocument { get; set; }
    public Action? RefreshEditorContext { get; set; }
    public Action? RefreshProjectKeys { get; set; }

    // Индекс вкладки нижней панели (Проверка/Поиск/Журнал сборки) — общий для
    // нескольких VM, поэтому живёт здесь, а не в одной из них.
    [ObservableProperty]
    private int bottomTabIndex;

    // Путь текущего элемента в статус-баре. Остальная часть правой панели
    // (атрибуты/палитра/структура) — императивное построение WPF-дерева,
    // вызываемое из многих мест (Insert/Documents/Help) — сознательно
    // оставлено в MainWindow.SidePanels.cs, не мигрируется сейчас (тот же
    // класс риска, что построение дерева проекта в спеке).
    [ObservableProperty]
    private string contextText = string.Empty;

    public HelpViewModel Help { get; }
    public SearchViewModel Search { get; }
    public ValidationViewModel Validation { get; }
    public DocumentsViewModel Documents { get; }

    public MainViewModel(Dictionary<string, DocumentPane> panes)
    {
        Panes = panes;
        Documents = new DocumentsViewModel(this);
        Help = new HelpViewModel(this);
        Search = new SearchViewModel(this);
        Validation = new ValidationViewModel(this);
    }
}
