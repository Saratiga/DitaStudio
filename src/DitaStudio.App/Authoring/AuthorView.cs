using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;

namespace DitaStudio.App.Authoring;

/// <summary>
/// Режим «Автор»: документ показывается как оформленный текст, но каждая правка
/// сразу попадает в дерево DITA. Структурные операции проверяются по контент-модели.
/// </summary>
public sealed partial class AuthorView : ScrollViewer, IAuthorSurface
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(24, 18, 24, 120) };
    private readonly Dictionary<DitaNode, InlineEditor> _editors = new();
    private readonly List<InlineEditor> _order = new();

    // Читаются из текущей темы при каждой перестройке (не кэшируются/не замораживаются), чтобы
    // переключение темы подхватывалось перестройкой — см. MainWindow.OnToggleTheme.
    private static Brush TagBrush => ThemeManager.Brush("EditorTag");
    private static Brush ContainerBorder => ThemeManager.Brush("Line");
    private static Brush SelectedBorder => ThemeManager.Brush("Accent");
    private static Brush NoteBackground => ThemeManager.Brush("EditorNoteBackground");
    private static Brush WarnBackground => ThemeManager.Brush("EditorWarnBackground");
    private static Brush CodeBackground => ThemeManager.Brush("CodeBackground");
    private static Brush MetaBackground => ThemeManager.Brush("SurfaceAlt");
    private static Brush EditorText => ThemeManager.Brush("TextPrimary");
    private static Brush EditorTextMuted => ThemeManager.Brush("TextMuted");

    private DitaNode? _current;
    private Border? _currentBorder;

    public AuthorView()
    {
        Content = _panel;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Background = ThemeManager.Brush("Surface");
        Padding = new Thickness(0);
    }

    public DitaDocument? Document { get; private set; }

    public DitaProject? Project { get; set; }

    /// <summary>Элемент, в котором сейчас находится курсор.</summary>
    public DitaNode? CurrentNode
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value))
            {
                return;
            }

            _current = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Labels Labels { get; set; } = Labels.Russian;

    /// <summary>Показывать имена элементов слева от блоков.</summary>
    public bool ShowElementTags { get; set; } = true;

    public event EventHandler? SelectionChanged;

    /// <summary>Документ изменён (для отметки «не сохранено»).</summary>
    public event EventHandler? DocumentModified;

    /// <summary>Просьба сохранить состояние для отмены перед структурной операцией.</summary>
    public event EventHandler<string>? BeforeStructuralEdit;

    // ---------------------------------------------------------------- загрузка

    public void Load(DitaDocument document)
    {
        Document = document;
        Rebuild();
    }

    void IAuthorSurface.Rebuild() => Rebuild();

    public void Rebuild(DitaNode? focusNode = null, int caretOffset = 0)
    {
        Background = ThemeManager.Brush("Surface");
        _editors.Clear();
        _order.Clear();
        _panel.Children.Clear();
        _currentBorder = null;

        if (Document is null)
        {
            return;
        }

        var root = BuildNode(Document.Root, 0);
        if (root is not null)
        {
            _panel.Children.Add(root);
        }

        if (focusNode is not null && _editors.TryGetValue(focusNode, out var editor))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                editor.Focus();
                editor.PlaceCaretAt(caretOffset);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
