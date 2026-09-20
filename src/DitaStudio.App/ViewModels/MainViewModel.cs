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

    // Открытый проект. Источник истины теперь ProjectViewModel.LoadProject.
    [ObservableProperty]
    private DitaProject? project;

    // Условия сборки — используются ProjectViewModel (начальные значения при
    // открытии проекта) и ещё не мигрированным MainWindow.Publish.cs.
    [ObservableProperty]
    private Dialogs.ConditionsResult? conditions;

    // Заголовок окна. По умолчанию — как раньше в XAML, дальше ProjectViewModel
    // подставляет имя открытого проекта.
    [ObservableProperty]
    private string windowTitle = "DITA Studio";

    // Тот же экземпляр словаря, что MainWindow.xaml.cs держит в _panes — не
    // копия. DocumentsViewModel пишет в него напрямую (Add/Remove), поэтому
    // тип — мутируемый Dictionary, а не IReadOnlyDictionary.
    public Dictionary<string, DocumentPane> Panes { get; }

    // Временные мосты к ещё не мигрированной области SidePanels и к
    // императивному построению дерева карты/проекта (MainWindow.Map.cs/
    // Project.cs) — сознательно не мигрированы, см. комментарии на месте.
    public Action? UpdateTabHeaders { get; set; }
    public Func<string, DocumentPane?>? OpenDocument { get; set; }
    public Action? RefreshEditorContext { get; set; }
    public Action? RefreshProjectTree { get; set; }
    public Action? RefreshMapSelector { get; set; }
    public Action? RefreshMapTree { get; set; }
    public Action? RefreshRecentProjectsMenu { get; set; }
    public Action? RefreshOutline { get; set; }
    public Action? RefreshAttributePanel { get; set; }
    public Action<RefactorResult>? ApplyRefactorResult { get; set; }

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

    // Не "Project" — это имя уже занято открытым DitaProject выше.
    public ProjectViewModel ProjectPanel { get; }
    public PublishViewModel Publish { get; }
    public InsertViewModel Insert { get; }
    public MapViewModel Map { get; }

    public MainViewModel(Dictionary<string, DocumentPane> panes)
    {
        Panes = panes;
        Documents = new DocumentsViewModel(this);
        Help = new HelpViewModel(this);
        Search = new SearchViewModel(this);
        Validation = new ValidationViewModel(this);
        ProjectPanel = new ProjectViewModel(this);
        Publish = new PublishViewModel(this);
        Insert = new InsertViewModel(this);
        Map = new MapViewModel(this);
    }
}
