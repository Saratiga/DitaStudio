using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DitaStudio.App.ViewModels;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;

namespace DitaStudio.App;

// Общая часть окна: поля состояния, конструктор, горячие клавиши. Остальные обработчики
// разложены по темам в MainWindow.*.cs (partial class) — Project/Documents/SidePanels/Insert/
// Validation/Search/Map/Publish/Help.
public partial class MainWindow : Window
{
    private readonly Dictionary<string, DocumentPane> _panes = new(StringComparer.OrdinalIgnoreCase);

    // Не поля — тонкие проходы к MainViewModel, чтобы Map/Insert/Publish/
    // SidePanels (ещё не мигрированы) продолжали читать/писать их по имени,
    // как раньше.
    private DitaProject? _project => ViewModel.Project;

    private Dialogs.ConditionsResult? _conditions
    {
        get => ViewModel.Conditions;
        set => ViewModel.Conditions = value;
    }

    private MapTree? _mapTree;
    private MapItem? _mapDragCandidate;
    private Point _mapDragStart;
    private string? _lastOutputDirectory;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainViewModel(_panes)
        {
            OpenDocument = OpenDocument,
            UpdateTabHeaders = UpdateTabHeaders,
            RefreshEditorContext = () =>
            {
                OnEditorSelectionChanged();
                BuildOutline();
            },
            RefreshProjectTree = BuildProjectTree,
            RefreshMapSelector = BuildMapSelector,
            RefreshRecentProjectsMenu = RefreshRecentProjectsMenu
        };
        DataContext = ViewModel;
        InitializeComponent();
        RegisterShortcuts();
        ThemeManager.ApplyTitleBar(this);
        ThemeMenuItem.IsChecked = ThemeManager.Current == ThemeManager.Theme.Dark;
        RefreshRecentProjectsMenu();
        UpdateStatus("Откройте папку с проектом DITA: Файл → Открыть папку проекта.");
    }

    private void RefreshRecentProjectsMenu()
    {
        RecentProjectsMenu.Items.Clear();

        var recent = RecentProjects.Load();
        if (recent.Count == 0)
        {
            RecentProjectsMenu.Items.Add(new MenuItem { Header = "(пусто)", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => LoadProject(path);
            RecentProjectsMenu.Items.Add(item);
        }
    }

    private DocumentPane? Current => ViewModel.Current;

    private void RegisterShortcuts()
    {
        void Bind(Key key, ModifierKeys modifiers, Action action)
        {
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
            InputBindings.Add(new KeyBinding(command, key, modifiers));
        }

        InputBindings.Add(new KeyBinding(ViewModel.Documents.SaveCurrentCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ViewModel.Documents.SaveAllCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        Bind(Key.N, ModifierKeys.Control, NewDocument);
        InputBindings.Add(new KeyBinding(ViewModel.ProjectPanel.OpenProjectCommand, Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ViewModel.Documents.CloseCurrentTabCommand, Key.W, ModifierKeys.Control));
        Bind(Key.F5, ModifierKeys.None, () => PublishSite());
        InputBindings.Add(new KeyBinding(ViewModel.Validation.ValidateProjectCommand, Key.F7, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(ViewModel.Help.ShowElementHelpCommand, Key.F1, ModifierKeys.None));
        Bind(Key.E, ModifierKeys.Control, FocusPalette);
        Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, FocusSearch);
        Bind(Key.Z, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformUndo());
        Bind(Key.Y, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformRedo());
        Bind(Key.Up, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(true));
        Bind(Key.Down, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(false));
        Bind(Key.Right, ModifierKeys.Control | ModifierKeys.Alt, () => OnMergeCellRight(this, new RoutedEventArgs()));
        Bind(Key.Down, ModifierKeys.Control | ModifierKeys.Alt, () => OnMergeCellDown(this, new RoutedEventArgs()));
    }

    private void UpdateStatus(string text) => ViewModel.StatusText = text;
}
