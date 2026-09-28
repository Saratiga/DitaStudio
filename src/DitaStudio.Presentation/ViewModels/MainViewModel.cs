using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Корневая VM окна. Дочерние VM (Help и далее) добавляются по мере миграции
// соответствующих областей — см.
// docs/superpowers/specs/2026-09-18-mainwindow-mvvm-design.md.
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Готово";

    // Производное от Documents.SelectedTab — сама вкладка теперь источник
    // истины (шаг 5 спеки), не отдельное наблюдаемое поле.
    public IDocumentView? Current => Documents.SelectedTab?.Pane;

    // Открытый проект. Источник истины теперь ProjectViewModel.LoadProject.
    [ObservableProperty]
    private DitaProject? project;

    // Условия сборки — используются ProjectViewModel (начальные значения при
    // открытии проекта) и ещё не мигрированным MainWindow.Publish.cs.
    [ObservableProperty]
    private ConditionsResult? conditions;

    // Заголовок окна. По умолчанию — как раньше в XAML, дальше ProjectViewModel
    // подставляет имя открытого проекта.
    [ObservableProperty]
    private string windowTitle = "DITA Studio";

    // Открытые вкладки по полному пути. DocumentsViewModel пишет в словарь
    // напрямую (Add/Remove), окно оболочки — при переносе файла.
    public Dictionary<string, IDocumentView> Panes { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Сервисы оболочки (WPF или Avalonia): диалоги, выбор файлов, UI-поток,
    // печать в PDF, создание вкладки документа.
    public UiServices Services { get; }

    public IDialogService Dialogs => Services.Dialogs;

    public IFilePicker Files => Services.Files;

    // Временные мосты к ещё не мигрированной области SidePanels и к
    // императивному построению дерева карты/проекта (MainWindow.Map.cs/
    // Project.cs) — сознательно не мигрированы, см. комментарии на месте.
    public Action? UpdateTabHeaders { get; set; }
    public Func<string, IDocumentView?>? OpenDocument { get; set; }
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

    // Правая панель (атрибуты, палитра, структура) — использует Avalonia-оболочка.
    public SidePanelsViewModel SidePanels { get; }

    // Защита от потери данных: копии несохранённых правок и слежение за
    // изменениями файлов другими программами. Подключаются к проекту в
    // ProjectViewModel.LoadProject.
    public AutoRecovery Recovery { get; }
    public ExternalChangeWatcher ExternalChanges { get; }

    /// <summary>Общий хвост рефакторинга (перенос файла, переименование id, вынесение в conref):
    /// документы, открытые во вкладках, помечаются несохранёнными и перерисовываются
    /// (пользователь сохранит сам, как обычную правку); закрытые документы сохраняются на диск
    /// сразу — иначе изменения в файлах, которые никто сейчас не видит, легко потерять.
    /// Оболочка подключает его как <see cref="ApplyRefactorResult"/>.</summary>
    public void ApplyRefactorResultToDocuments(RefactorResult result)
    {
        foreach (var doc in result.ChangedDocuments)
        {
            var openPane = Panes.Values.FirstOrDefault(p => ReferenceEquals(p.Document, doc));
            if (openPane is not null)
            {
                openPane.Document.IsDirty = true;
                openPane.ReloadViews();
            }
            else if (doc.FilePath is not null)
            {
                doc.Save(doc.FilePath);
            }
        }

        Documents.RefreshAllTabTitles();
        RefreshAttributePanel?.Invoke();
    }

    public MainViewModel(UiServices services)
    {
        Services = services;
        Documents = new DocumentsViewModel(this);
        Help = new HelpViewModel(this);
        Search = new SearchViewModel(this);
        Validation = new ValidationViewModel(this);
        ProjectPanel = new ProjectViewModel(this);
        Publish = new PublishViewModel(this);
        Insert = new InsertViewModel(this);
        Map = new MapViewModel(this);
        SidePanels = new SidePanelsViewModel(this);
        Recovery = new AutoRecovery(this);
        ExternalChanges = new ExternalChangeWatcher(this);
    }
}
