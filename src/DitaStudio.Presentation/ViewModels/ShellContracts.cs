using System.Collections.ObjectModel;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Что ViewModel'и берут у окна и друг у друга. Вместо одного MainViewModel, который знает всех, каждая ViewModel получает только
// то, что ей нужно: состояние строки состояния и заголовка (IShellState), открытые проекты (IWorkspace), открытые документы
// (IDocumentHost), сервисы оболочки (UiServices) и обратные вызовы окна (ShellHooks). MainViewModel собирает всё это воедино
// и реализует IShellState.

/// <summary>Состояние окна, которое показывают строка состояния, заголовок и нижняя панель.</summary>
public interface IShellState
{
    string StatusText { get; set; }

    string WindowTitle { get; set; }

    /// <summary>Путь текущего элемента в строке состояния.</summary>
    string ContextText { get; set; }

    /// <summary>Вкладка нижней панели (Проверка / Поиск / Журнал сборки).</summary>
    int BottomTabIndex { get; set; }
}

/// <summary>Открытые проекты окна и активный из них.</summary>
public interface IWorkspace
{
    /// <summary>Активный проект: проект выбранной вкладки, файла в дереве или карты; null — ничего не открыто.</summary>
    DitaProject? Project { get; }

    ObservableCollection<DitaProject> Projects { get; }

    /// <summary>Условия сборки активного проекта (для диалога условий и публикации).</summary>
    ConditionsResult? Conditions { get; set; }

    /// <summary>Проект, которому принадлежит файл (самая глубокая из открытых папок, в которой он лежит); null — файл вне проектов.</summary>
    DitaProject? ProjectOf(string path);

    /// <summary>Делает проект активным (каталог элементов, условия, заголовок окна, ключи и карты — его).</summary>
    void ActivateProject(DitaProject project);

    /// <summary>Закрыт последний проект: активного нет.</summary>
    void Deactivate();

    /// <summary>Сменился активный проект (в том числе на «никакой»).</summary>
    event EventHandler? ActiveProjectChanged;

    /// <summary>Ключи проектов могли измениться (сохранён документ, внешняя правка, перенос или удаление файла): панель ключей перечитывается.</summary>
    event EventHandler? KeysChanged;

    void NotifyKeysChanged();
}

/// <summary>Открытые документы: текущая вкладка, вкладки по пути, открытие файла.</summary>
public interface IDocumentHost
{
    /// <summary>Документ выбранной вкладки или null.</summary>
    IDocumentView? Current { get; }

    /// <summary>Открытые вкладки по полному пути.</summary>
    Dictionary<string, IDocumentView> Panes { get; }

    /// <summary>Открывает файл во вкладке (или переключается на уже открытую); null — не удалось открыть.</summary>
    IDocumentView? OpenDocument(string path);

    /// <summary>Обновляет заголовки вкладок (признак несохранённого, имя файла).</summary>
    void RefreshAllTabTitles();

    /// <summary>Общий хвост рефакторинга: открытые документы помечаются несохранёнными и перерисовываются, закрытые — сохраняются.</summary>
    void ApplyRefactorResult(RefactorResult result);

    /// <summary>Документ сохранён или его вкладка закрыта без сохранения: копия для восстановления больше не нужна.</summary>
    event Action<string>? DocumentSettled;

    /// <summary>То же для всех несохранённых документов проекта (null — всех проектов): закрытие проекта или окна.</summary>
    event Action<DitaProject?>? ProjectSettled;

    /// <summary>Пользователь закончил править заголовок топика в документе (фокус ушёл).</summary>
    event Action<IDocumentView>? RootTitleCommitted;
}

/// <summary>
/// Обратные вызовы окна: перерисовать то, что ViewModel'и сами перерисовать не могут (контролы, меню). Окно подключает их при
/// создании; не подключённый вызов ничего не делает.
/// </summary>
public sealed class ShellHooks
{
    public Action? RefreshEditorContext { get; set; }
    public Action? RefreshProjectTree { get; set; }
    public Action? RefreshMapSelector { get; set; }
    public Action? RefreshMapTree { get; set; }
    public Action? RefreshRecentProjectsMenu { get; set; }
    public Action? RefreshOutline { get; set; }
    public Action? RefreshAttributePanel { get; set; }
}

/// <summary>Общее для всех ViewModel'ей: состояние окна, проекты, сервисы оболочки и обратные вызовы окна.</summary>
public sealed record ShellContext(IShellState Shell, IWorkspace Workspace, UiServices Ui, ShellHooks Hooks);
