using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>
/// Корневая VM окна: собирает дочерние VM и общие части (<see cref="Workspace"/>, <see cref="ShellHooks"/>, сервисы оболочки) и
/// реализует <see cref="IShellState"/> — строка состояния, заголовок окна, нижняя панель. Сами дочерние VM друг о друге и об этом
/// классе не знают: каждая получает в конструкторе только то, что ей нужно (см. ShellContracts.cs).
/// </summary>
public partial class MainViewModel : ObservableObject, IShellState
{
    [ObservableProperty]
    private string statusText = "Готово";

    // Заголовок окна: «DITA Studio» или «DITA Studio — проект».
    [ObservableProperty]
    private string windowTitle = "DITA Studio";

    // Индекс вкладки нижней панели (Проверка/Поиск/Журнал сборки) — общий для нескольких VM.
    [ObservableProperty]
    private int bottomTabIndex;

    // Путь текущего элемента в строке состояния.
    [ObservableProperty]
    private string contextText = string.Empty;

    /// <summary>Открытые проекты и активный из них.</summary>
    public Workspace Workspace { get; } = new();

    /// <summary>Обратные вызовы окна — его контролы и меню; подключает оболочка.</summary>
    public ShellHooks Hooks { get; } = new();

    /// <summary>Сервисы оболочки: диалоги, выбор файлов, UI-поток, печать в PDF, создание вкладки документа.</summary>
    public UiServices Services { get; }

    public IDialogService Dialogs => Services.Dialogs;

    public IFilePicker Files => Services.Files;

    // Короткие пути к состоянию рабочей области — для окна и тестов.
    public DitaProject? Project => Workspace.Project;

    public ObservableCollection<DitaProject> Projects => Workspace.Projects;

    public ConditionsResult? Conditions
    {
        get => Workspace.Conditions;
        set => Workspace.Conditions = value;
    }

    public DitaProject? ProjectOf(string path) => Workspace.ProjectOf(path);

    public void ActivateProject(DitaProject project) => Workspace.ActivateProject(project);

    /// <summary>Документ выбранной вкладки или null.</summary>
    public IDocumentView? Current => Documents.Current;

    /// <summary>Открытые вкладки по полному пути.</summary>
    public Dictionary<string, IDocumentView> Panes => Documents.Panes;

    /// <summary>Открывает файл во вкладке (или переключается на уже открытую).</summary>
    public Func<string, IDocumentView?> OpenDocument => Documents.OpenDocument;

    public HelpViewModel Help { get; }
    public SearchViewModel Search { get; }
    public ValidationViewModel Validation { get; }
    public DocumentsViewModel Documents { get; }

    // Не "Project" — это имя уже занято активным DitaProject выше.
    public ProjectViewModel ProjectPanel { get; }
    public PublishViewModel Publish { get; }
    public InsertViewModel Insert { get; }
    public MapViewModel Map { get; }

    // Правая панель (атрибуты, палитра, структура).
    public SidePanelsViewModel SidePanels { get; }

    // Защита от потери данных: копии несохранённых правок и слежение за изменениями файлов другими программами.
    public AutoRecovery Recovery { get; }
    public ExternalChangeWatcher ExternalChanges { get; }

    /// <summary>Общий хвост рефакторинга для оболочки и тестов; см. <see cref="IDocumentHost.ApplyRefactorResult"/>.</summary>
    public void ApplyRefactorResultToDocuments(RefactorResult result) => Documents.ApplyRefactorResult(result);

    public MainViewModel(UiServices services)
    {
        Services = services;
        var context = new ShellContext(this, Workspace, services, Hooks);

        // Порядок важен: каждая VM получает уже созданные; циклических зависимостей нет — связь «вверх»
        // идёт событиями (DocumentsViewModel, IWorkspace).
        Documents = new DocumentsViewModel(context);
        Recovery = new AutoRecovery(context, Documents);
        ExternalChanges = new ExternalChangeWatcher(context, Documents);
        Search = new SearchViewModel(context, Documents);
        var fileOps = new ProjectFileOperations(context, Documents);
        Map = new MapViewModel(context, Documents, Search, fileOps);
        ProjectPanel = new ProjectViewModel(context, Documents, Recovery, ExternalChanges, Map, fileOps);

        Help = new HelpViewModel(context, Documents);
        Validation = new ValidationViewModel(context, Documents);
        Publish = new PublishViewModel(context, Documents, Map);
        Insert = new InsertViewModel(context, Documents);
        SidePanels = new SidePanelsViewModel(context, Documents, Insert);

        // Смена активного проекта: заголовок окна, затем всё, что показывает его данные.
        Workspace.ActiveProjectChanged += (_, _) =>
        {
            WindowTitle = Workspace.Project is { } active ? $"DITA Studio — {active.Name}" : "DITA Studio";
            Hooks.RefreshMapSelector?.Invoke();
            ProjectPanel.RefreshKeysList();
            Hooks.RefreshEditorContext?.Invoke();
        };
    }
}
