using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Вкладка «Карта»: список карт проекта, дерево выбранной карты и операции над ним —
// добавление ссылок на топики и разделов, перестановка/вложение (кнопками и
// перетаскиванием), удаление, таблица соответствий. Класс разбит по областям (partial):
// здесь — зависимости и выбор; .Tabs — вкладки карт; .Tree — дерево; .Files — файлы строк;
// .Edit — правка структуры; .ContextMenu — буфер обмена и свойства; .RelTable — таблица
// соответствий (перевод в строки диалога — RelTableConverter).
public partial class MapViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly DocumentsViewModel _documents;
    private readonly SearchViewModel _search;
    private readonly ProjectFileOperations _fileOps;

    /// <summary>Карты активного проекта — из них открывается новая вкладка карты.</summary>
    public ObservableCollection<ProjectFile> Maps { get; } = new();

    [ObservableProperty]
    private ProjectFile? selectedMap;

    /// <summary>Открытые карты (вкладками) — всех открытых проектов. Выбор вкладки другого проекта делает его активным.</summary>
    public ObservableCollection<OpenMapTab> OpenMaps { get; } = new();

    [ObservableProperty]
    private OpenMapTab? selectedMapTab;

    private bool _syncingTabs;

    // Последняя выбранная вкладка каждого проекта — при возврате к проекту открывается она.
    private readonly Dictionary<DitaProject, OpenMapTab> _lastTab = new();

    public ObservableCollection<MapTreeNode> Tree { get; } = new();

    [ObservableProperty]
    private MapTreeNode? selectedNode;

    public MapViewModel(ShellContext context, DocumentsViewModel documents, SearchViewModel search, ProjectFileOperations fileOps)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _documents = documents;
        _search = search;
        _fileOps = fileOps;
    }

    partial void OnSelectedNodeChanged(MapTreeNode? value)
    {
        if (value is not null)
        {
            _shell.StatusText = value.IsBroken
                ? $"{value.Item.ElementName}: {value.Title} — {value.Item.BrokenReason}"
                : $"{value.Item.ElementName}: {value.Title}";
        }

        // Что доступно строке, зависит от её вида: у раздела нет файла, у корня карты нет родителя.
        OnPropertyChanged(nameof(SelectedHasFile));
        OnPropertyChanged(nameof(SelectedIsBroken));
        CreateMissingFileCommand.NotifyCanExecuteChanged();
        ReplaceFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedPageBreakBefore));
        OnPropertyChanged(nameof(SelectedPageBreakNone));
        OnPropertyChanged(nameof(HasStructureSelection));
        FindReferencesCommand.NotifyCanExecuteChanged();
        TogglePageBreakBeforeCommand.NotifyCanExecuteChanged();
        ToggleNoPageBreakCommand.NotifyCanExecuteChanged();
        RenameFileCommand.NotifyCanExecuteChanged();
        DeleteFileCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        IndentCommand.NotifyCanExecuteChanged();
        OutdentCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
    }

    /// <summary>У выбранной строки есть файл проекта (топик, вложенная карта). У раздела
    /// (<c>topichead</c>), группы, ключа без файла и битой ссылки его нет — команды «Найти ссылки»,
    /// «Переименовать / переместить файл», «Удалить файл» им недоступны, а раздел убирается из карты
    /// командой «Убрать из карты».</summary>
    public bool SelectedHasFile => SelectedFile is not null;

    /// <summary>Выбрана строка, которую можно переставить, скопировать, дублировать и убрать:
    /// любая, кроме корня карты.</summary>
    public bool HasStructureSelection => SelectedNode is { IsRoot: false };

    /// <summary>Окну нужно показать строку (она может быть за краем прокрутки): после добавления новой строки.</summary>
    public event Action<MapTreeNode>? RevealRequested;

    /// <summary>Выбирает в дереве строку по узлу документа (после перестроения дерева).</summary>
    private void Select(DitaNode node, bool reveal = false)
    {
        SelectedNode = Tree.SelectMany(Flatten).FirstOrDefault(n => ReferenceEquals(n.Item.Node, node));
        if (reveal && SelectedNode is { } selected)
        {
            RevealRequested?.Invoke(selected);
        }
    }

    private ProjectFile? SelectedFile =>
        SelectedNode?.Item.TargetPath is { } target ? _workspace.Project?.FindFile(target) : null;
}
