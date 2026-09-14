using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;

namespace DitaStudio.App;

// Общая часть окна: поля состояния, конструктор, горячие клавиши. Остальные обработчики
// разложены по темам в MainWindow.*.cs (partial class) — Project/Documents/SidePanels/Insert/
// Validation/Search/Map/Publish/Help.
public partial class MainWindow : Window
{
    private readonly Dictionary<string, DocumentPane> _panes = new(StringComparer.OrdinalIgnoreCase);
    private DitaProject? _project;
    private MapTree? _mapTree;
    private MapItem? _mapDragCandidate;
    private Point _mapDragStart;
    private Dialogs.ConditionsResult? _conditions;
    private string? _lastOutputDirectory;

    public MainWindow()
    {
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

    private DocumentPane? Current => DocumentTabs.SelectedItem is TabItem { Content: DocumentPane pane } ? pane : null;

    private void RegisterShortcuts()
    {
        void Bind(Key key, ModifierKeys modifiers, Action action)
        {
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
            InputBindings.Add(new KeyBinding(command, key, modifiers));
        }

        Bind(Key.S, ModifierKeys.Control, () => SaveCurrent());
        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, SaveAll);
        Bind(Key.N, ModifierKeys.Control, NewDocument);
        Bind(Key.O, ModifierKeys.Control | ModifierKeys.Shift, OpenProject);
        Bind(Key.W, ModifierKeys.Control, CloseCurrentTab);
        Bind(Key.F5, ModifierKeys.None, () => PublishSite());
        Bind(Key.F7, ModifierKeys.None, ValidateProject);
        Bind(Key.F1, ModifierKeys.None, ShowElementHelp);
        Bind(Key.E, ModifierKeys.Control, FocusPalette);
        Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, FocusSearch);
        Bind(Key.Z, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformUndo());
        Bind(Key.Y, ModifierKeys.Control | ModifierKeys.Alt, () => Current?.PerformRedo());
        Bind(Key.Up, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(true));
        Bind(Key.Down, ModifierKeys.Control | ModifierKeys.Shift, () => MoveElement(false));
        Bind(Key.Right, ModifierKeys.Control | ModifierKeys.Alt, () => OnMergeCellRight(this, new RoutedEventArgs()));
        Bind(Key.Down, ModifierKeys.Control | ModifierKeys.Alt, () => OnMergeCellDown(this, new RoutedEventArgs()));
    }

    private void UpdateStatus(string text) => StatusText.Text = text;
}
