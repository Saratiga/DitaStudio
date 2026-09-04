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

namespace DitaStudio.App.Authoring;

/// <summary>
/// Режим «Автор»: документ показывается как оформленный текст, но каждая правка
/// сразу попадает в дерево DITA. Структурные операции проверяются по контент-модели.
/// </summary>
public sealed class AuthorView : ScrollViewer
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(24, 18, 24, 120) };
    private readonly Dictionary<DitaNode, InlineEditor> _editors = new();
    private readonly List<InlineEditor> _order = new();

    private static readonly Brush TagBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2));
    private static readonly Brush ContainerBorder = new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEE));
    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA9));
    private static readonly Brush NoteBackground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF4, 0xFB));
    private static readonly Brush WarnBackground = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xE2));
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));
    private static readonly Brush MetaBackground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC));

    private DitaNode? _current;
    private Border? _currentBorder;

    static AuthorView()
    {
        TagBrush.Freeze();
        ContainerBorder.Freeze();
        SelectedBorder.Freeze();
        NoteBackground.Freeze();
        WarnBackground.Freeze();
        CodeBackground.Freeze();
        MetaBackground.Freeze();
    }

    public AuthorView()
    {
        Content = _panel;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Background = Brushes.White;
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

    public void Rebuild(DitaNode? focusNode = null, int caretOffset = 0)
    {
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

    // ------------------------------------------------------------ построение

    private FrameworkElement? BuildNode(DitaNode node, int depth)
    {
        if (node.Kind == NodeKind.Comment)
        {
            return BuildComment(node);
        }

        if (node.Kind != NodeKind.Element)
        {
            return null;
        }

        var def = DitaCatalog.Default.Get(node.Name);

        if (def is null)
        {
            return BuildUnknown(node, depth);
        }

        switch (node.Name)
        {
            case "prolog":
            case "titlealts":
            case "topicmeta":
            case "metadata":
            case "bookmeta":
                return BuildMetaBlock(node);

            case "table":
                return BuildCalsTable(node, depth);

            case "simpletable":
            case "properties":
            case "choicetable":
                return BuildSimpleTable(node, depth);

            case "pre":
            case "codeblock":
            case "screen":
            case "msgblock":
            case "lines":
                return BuildPreformatted(node);

            case "image":
                return BuildImageBlock(node);
        }

        if (def.IsTopicType)
        {
            return BuildContainer(node, depth, headerText: null, background: null);
        }

        return def.Display switch
        {
            DisplayKind.Block => BuildTextBlockRow(node, depth),
            DisplayKind.Inline => BuildTextBlockRow(node, depth),
            DisplayKind.Empty => BuildEmptyRow(node),
            DisplayKind.Meta => BuildMetaBlock(node),
            DisplayKind.Preformatted => BuildPreformatted(node),
            _ => BuildContainerByName(node, depth)
        };
    }

    private FrameworkElement BuildContainerByName(DitaNode node, int depth)
    {
        string? header = null;
        Brush? background = null;

        switch (node.Name)
        {
            case "note":
            {
                var type = node.GetAttribute("type") ?? "note";
                header = Labels.NoteLabel(type);
                background = type is "caution" or "danger" or "warning" or "attention" or "notice"
                    ? WarnBackground
                    : NoteBackground;
                break;
            }

            case "prereq":
                header = Labels.Prerequisites;
                break;
            case "context":
                header = Labels.Context;
                break;
            case "steps":
            case "steps-unordered":
                header = Labels.Steps;
                break;
            case "result":
                header = Labels.Result;
                break;
            case "postreq":
                header = Labels.PostRequisites;
                break;
            case "tasktroubleshooting":
                header = Labels.TaskTroubleshooting;
                break;
            case "example":
                header = Labels.Example;
                break;
            case "condition":
                header = Labels.Condition;
                break;
            case "cause":
                header = Labels.Cause;
                break;
            case "remedy":
                header = Labels.Remedy;
                break;
            case "related-links":
                header = Labels.RelatedLinks;
                break;
        }

        return BuildContainer(node, depth, header, background);
    }

    private FrameworkElement BuildContainer(DitaNode node, int depth, string? headerText, Brush? background)
    {
        var stack = new StackPanel();

        if (headerText is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = headerText,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x44, 0x54)),
                Margin = new Thickness(0, 2, 0, 4)
            });
        }

        var isList = node.Name is "ul" or "ol" or "sl" or "steps" or "steps-unordered" or "substeps" or "choices";
        var counter = 0;

        foreach (var child in node.Children)
        {
            var element = BuildNode(child, depth + 1);
            if (element is null)
            {
                continue;
            }

            if (isList && child.Kind == NodeKind.Element)
            {
                counter++;
                var marker = node.Name is "ol" or "steps" or "substeps"
                    ? $"{counter}."
                    : node.Name == "sl" ? string.Empty : "•";

                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var markerBlock = new TextBlock
                {
                    Text = marker,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x64, 0x72)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 8, 0)
                };
                Grid.SetColumn(markerBlock, 0);
                Grid.SetColumn(element, 1);
                row.Children.Add(markerBlock);
                row.Children.Add(element);
                stack.Children.Add(row);
            }
            else
            {
                stack.Children.Add(element);
            }
        }

        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(headerText is null ? 0 : 10, headerText is null ? 0 : 6, 0, headerText is null ? 0 : 6),
            Margin = new Thickness(0, node.Name is "body" or "conbody" or "taskbody" or "refbody" or "troublebody" ? 4 : 6, 0, 4),
            Background = background,
            BorderBrush = background is null ? null : ContainerBorder,
            BorderThickness = new Thickness(background is null ? 0 : 1),
            CornerRadius = new CornerRadius(4),
            Tag = node
        };

        if (node.Name is "section" or "example" or "fig")
        {
            border.BorderBrush = ContainerBorder;
            border.BorderThickness = new Thickness(0, 0, 0, 0);
            border.Margin = new Thickness(0, 12, 0, 8);
        }

        AttachSelection(border, node);
        return border;
    }

    private FrameworkElement BuildTextBlockRow(DitaNode node, int depth)
    {
        var editor = CreateEditor(node);

        switch (node.Name)
        {
            case "title":
            case "glossterm":
            {
                var level = node.Parent is not null && (DitaCatalog.Default.Get(node.Parent.Name)?.IsTopicType ?? false)
                    ? TitleLevel(node.Parent)
                    : 3;
                editor.FontSize = level switch { 1 => 25, 2 => 20, 3 => 17, _ => 15 };
                editor.FontWeight = FontWeights.SemiBold;
                editor.Margin = new Thickness(0, level == 1 ? 0 : 14, 0, 6);
                break;
            }

            case "shortdesc":
                editor.FontSize = 15;
                editor.FontStyle = FontStyles.Italic;
                editor.Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x54, 0x62));
                editor.Margin = new Thickness(0, 0, 0, 10);
                break;

            case "cmd":
                editor.FontWeight = FontWeights.Medium;
                break;

            case "dt":
            case "pt":
                editor.FontWeight = FontWeights.SemiBold;
                editor.Margin = new Thickness(0, 6, 0, 0);
                break;

            case "dd":
            case "pd":
                editor.Margin = new Thickness(20, 0, 0, 0);
                break;

            case "stepresult":
            case "info":
            case "stepxmp":
                editor.Foreground = new SolidColorBrush(Color.FromRgb(0x3E, 0x47, 0x55));
                editor.Margin = new Thickness(0, 2, 0, 0);
                break;
        }

        var container = new Border
        {
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 1, 0, 1),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = Brushes.Transparent,
            Tag = node
        };

        if (!ShowElementTags)
        {
            container.Child = editor;
            AttachSelection(container, node);
            return container;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tag = new TextBlock
        {
            Text = node.Name,
            FontFamily = InlineStyles.Mono,
            FontSize = 10.5,
            Foreground = TagBrush,
            Margin = new Thickness(0, 4, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Grid.SetColumn(tag, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(tag);
        grid.Children.Add(editor);
        container.Child = grid;

        AttachSelection(container, node);
        return container;
    }

    private static int TitleLevel(DitaNode topic)
    {
        var level = 1;
        var parent = topic.Parent;
        while (parent is not null)
        {
            if (DitaCatalog.Default.Get(parent.Name)?.IsTopicType == true)
            {
                level++;
            }

            parent = parent.Parent;
        }

        return Math.Min(level, 4);
    }

    private InlineEditor CreateEditor(DitaNode node)
    {
        var editor = new InlineEditor(node)
        {
            FontSize = 14.5,
            FontFamily = new FontFamily("Segoe UI")
        };

        editor.ContentChanged += (_, _) =>
        {
            if (Document is not null)
            {
                Document.IsDirty = true;
            }

            DocumentModified?.Invoke(this, EventArgs.Empty);
        };

        editor.Focused += (_, _) =>
        {
            CurrentNode = node;
            HighlightCurrent(editor);
        };

        editor.StructureRequested += OnStructureRequested;

        _editors[node] = editor;
        _order.Add(editor);
        return editor;
    }

    private FrameworkElement BuildPreformatted(DitaNode node)
    {
        var box = new TextBox
        {
            Text = node.InnerText.Trim('\n'),
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = InlineStyles.Mono,
            FontSize = 13,
            Background = CodeBackground,
            BorderBrush = ContainerBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Tag = node
        };

        box.TextChanged += (_, _) =>
        {
            node.SetText(box.Text);
            if (Document is not null)
            {
                Document.IsDirty = true;
            }

            DocumentModified?.Invoke(this, EventArgs.Empty);
        };

        box.GotKeyboardFocus += (_, _) => CurrentNode = node;

        var container = new Border
        {
            Child = box,
            Margin = new Thickness(0, 8, 0, 8),
            Tag = node
        };

        AttachSelection(container, node);
        return container;
    }

    private FrameworkElement BuildImageBlock(DitaNode node)
    {
        var href = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "—";
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var image = TryLoadImage(node);
        if (image is not null)
        {
            panel.Children.Add(new Image
            {
                Source = image,
                MaxHeight = 260,
                MaxWidth = 520,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 10, 0)
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "🖼",
                FontSize = 22,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = href,
            Foreground = TagBrush,
            FontFamily = InlineStyles.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 8),
            Background = MetaBackground,
            BorderBrush = ContainerBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Tag = node
        };

        AttachSelection(border, node);
        return border;
    }

    private ImageSource? TryLoadImage(DitaNode node)
    {
        var href = node.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) || Document?.FilePath is null || RefResolver.IsExternal(href!))
        {
            return null;
        }

        var path = RefResolver.ResolvePath(Document.FilePath, href!);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private FrameworkElement BuildEmptyRow(DitaNode node)
    {
        var text = new TextBlock
        {
            Text = $"<{node.Name}{FormatAttributes(node)}/>",
            FontFamily = InlineStyles.Mono,
            FontSize = 11.5,
            Foreground = TagBrush,
            Margin = new Thickness(4, 3, 0, 3),
            TextWrapping = TextWrapping.Wrap
        };

        var border = new Border { Child = text, Tag = node, Padding = new Thickness(2) };
        AttachSelection(border, node);
        return border;
    }

    private FrameworkElement BuildComment(DitaNode node)
    {
        return new Border
        {
            Child = new TextBlock
            {
                Text = "// " + node.Value.Trim(),
                FontFamily = InlineStyles.Mono,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x8A, 0x5A)),
                TextWrapping = TextWrapping.Wrap
            },
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFC, 0xF3))
        };
    }

    private FrameworkElement BuildUnknown(DitaNode node, int depth)
    {
        var border = new Border
        {
            Child = new TextBlock
            {
                Text = Core.Model.XmlSerializer.ToXml(node),
                FontFamily = InlineStyles.Mono,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
            },
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 4, 4, 4),
            Margin = new Thickness(0, 4, 0, 4),
            Tag = node
        };

        AttachSelection(border, node);
        return border;
    }

    private FrameworkElement BuildMetaBlock(DitaNode node)
    {
        var expander = new Expander
        {
            Header = $"{node.Name} — метаданные",
            FontSize = 11.5,
            Foreground = TagBrush,
            Margin = new Thickness(0, 6, 0, 6),
            IsExpanded = false,
            Content = new TextBox
            {
                Text = Core.Model.XmlSerializer.ToXml(node),
                IsReadOnly = true,
                FontFamily = InlineStyles.Mono,
                FontSize = 11.5,
                Background = MetaBackground,
                BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };

        var border = new Border { Child = expander, Tag = node };
        AttachSelection(border, node);
        return border;
    }

    // ------------------------------------------------------------- таблицы

    private FrameworkElement BuildCalsTable(DitaNode node, int depth)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };

        var title = node.FirstElement("title");
        if (title is not null)
        {
            var titleEditor = CreateEditor(title);
            titleEditor.FontWeight = FontWeights.SemiBold;
            titleEditor.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(titleEditor);
        }

        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            var rows = new List<DitaNode>();
            var head = tgroup.FirstElement("thead");
            var bodyGroup = tgroup.FirstElement("tbody");
            if (head is not null)
            {
                rows.AddRange(head.ElementChildren().Where(r => r.Name == "row"));
            }

            var headerCount = rows.Count;
            if (bodyGroup is not null)
            {
                rows.AddRange(bodyGroup.ElementChildren().Where(r => r.Name == "row"));
            }

            var columns = rows.Count == 0
                ? 1
                : rows.Max(r => r.ElementChildren().Count(c => c.Name == "entry"));

            stack.Children.Add(BuildGrid(rows, headerCount, columns, "entry"));
        }

        var border = new Border { Child = stack, Tag = node };
        AttachSelection(border, node);
        return border;
    }

    private FrameworkElement BuildSimpleTable(DitaNode node, int depth)
    {
        var headNames = node.Name switch
        {
            "properties" => new[] { "proptypehd", "propvaluehd", "propdeschd" },
            "choicetable" => new[] { "choptionhd", "chdeschd" },
            _ => new[] { "stentry" }
        };
        var cellNames = node.Name switch
        {
            "properties" => new[] { "proptype", "propvalue", "propdesc" },
            "choicetable" => new[] { "choption", "chdesc" },
            _ => new[] { "stentry" }
        };

        var rows = new List<DitaNode>();
        var head = node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead");
        if (head is not null)
        {
            rows.Add(head);
        }

        var headerCount = rows.Count;
        rows.AddRange(node.ElementChildren().Where(e => e.Name is "strow" or "property" or "chrow"));

        var allNames = headNames.Concat(cellNames).ToArray();
        var columns = rows.Count == 0
            ? 1
            : rows.Max(r => r.ElementChildren().Count(c => allNames.Contains(c.Name)));

        var grid = BuildGrid(rows, headerCount, columns, allNames);
        var border = new Border { Child = grid, Margin = new Thickness(0, 10, 0, 10), Tag = node };
        AttachSelection(border, node);
        return border;
    }

    private FrameworkElement BuildGrid(List<DitaNode> rows, int headerCount, int columns, params string[] cellNames)
    {
        var grid = new Grid();
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = rows[r].ElementChildren().Where(c => cellNames.Contains(c.Name)).ToList();

            for (var c = 0; c < columns; c++)
            {
                var isHeader = r < headerCount;
                FrameworkElement content;
                if (c < cells.Count)
                {
                    var editor = CreateEditor(cells[c]);
                    editor.FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal;
                    content = editor;
                }
                else
                {
                    content = new TextBlock { Text = string.Empty };
                }

                var cellBorder = new Border
                {
                    Child = content,
                    BorderBrush = ContainerBorder,
                    BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                    Padding = new Thickness(7, 5, 7, 5),
                    Background = isHeader ? MetaBackground : Brushes.Transparent
                };

                if (c < cells.Count)
                {
                    cellBorder.Tag = cells[c];
                    AttachSelection(cellBorder, cells[c]);
                }

                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        return grid;
    }

    // ------------------------------------------------------------- выделение

    private void AttachSelection(Border border, DitaNode node)
    {
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Border)
            {
                CurrentNode = node;
                HighlightBorder(border);
            }
        };
    }

    private void HighlightCurrent(InlineEditor editor)
    {
        var parent = VisualTreeHelper.GetParent(editor);
        while (parent is not null && parent is not Border)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (parent is Border border)
        {
            HighlightBorder(border);
        }
    }

    private void HighlightBorder(Border border)
    {
        if (_currentBorder is not null && !ReferenceEquals(_currentBorder, border))
        {
            _currentBorder.BorderBrush = Brushes.Transparent;
        }

        if (border.BorderThickness.Left >= 2 && border.BorderThickness.Top == 0)
        {
            border.BorderBrush = SelectedBorder;
            _currentBorder = border;
        }
    }

    private static string FormatAttributes(DitaNode node)
    {
        if (node.Attributes.Count == 0)
        {
            return string.Empty;
        }

        return " " + string.Join(" ", node.Attributes.Select(a => $"{a.Name}=\"{a.Value}\""));
    }

    // ------------------------------------------------------- структурные правки

    private void OnStructureRequested(object? sender, StructureRequestEventArgs e)
    {
        if (Document is null)
        {
            return;
        }

        switch (e.Request)
        {
            case StructureRequest.Split:
                e.Handled = HandleSplit(e.Node, e.CaretOffset);
                break;

            case StructureRequest.MergeWithPrevious:
                e.Handled = HandleMerge(e.Node);
                break;

            case StructureRequest.Indent:
                e.Handled = HandleIndent(e.Node, outdent: false);
                break;

            case StructureRequest.Outdent:
                e.Handled = HandleIndent(e.Node, outdent: true);
                break;

            case StructureRequest.NextBlock:
                e.Handled = MoveFocus(e.Node, 1);
                break;

            case StructureRequest.PreviousBlock:
                e.Handled = MoveFocus(e.Node, -1);
                break;

            case StructureRequest.DeleteForward:
                e.Handled = false;
                break;
        }
    }

    private bool HandleSplit(DitaNode node, int caretOffset)
    {
        if (Document is null)
        {
            return false;
        }

        // В шаге и элементе списка Enter создаёт следующий шаг/пункт.
        var target = node;
        if (node.Name == "cmd" && node.Parent is { Name: "step" or "substep" })
        {
            target = node.Parent;
        }

        BeforeStructuralEdit?.Invoke(this, "Разделение блока");

        DitaNode? created;
        if (ReferenceEquals(target, node))
        {
            created = EditCommands.SplitBlock(node, caretOffset);
            if (created is null)
            {
                return false;
            }
        }
        else
        {
            created = EditCommands.InsertAfter(target, target.Name);
            if (created is null)
            {
                return false;
            }

            created = created.FirstElement("cmd") ?? created;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(created, 0);
        return true;
    }

    private bool HandleMerge(DitaNode node)
    {
        if (Document is null)
        {
            return false;
        }

        var previous = EditCommands.PreviousElement(node);
        if (previous is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение блоков");

        var offset = previous.InnerText.Length;
        var merged = EditCommands.MergeWithPrevious(node);
        if (merged is null)
        {
            // Пустой блок просто удаляем.
            if (node.InnerText.Length == 0 && EditCommands.Delete(node))
            {
                Document.IsDirty = true;
                DocumentModified?.Invoke(this, EventArgs.Empty);
                Rebuild(previous, previous.InnerText.Length);
                return true;
            }

            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(merged, offset);
        return true;
    }

    private bool HandleIndent(DitaNode node, bool outdent)
    {
        if (Document is null)
        {
            return false;
        }

        var item = node.Name is "li" or "step" or "substep" ? node : node.Parent;
        if (item is null || item.Name is not ("li" or "step" or "substep"))
        {
            return false;
        }

        var list = item.Parent;
        if (list is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, outdent ? "Уменьшение уровня" : "Увеличение уровня");

        if (!outdent)
        {
            var previous = EditCommands.PreviousElement(item);
            if (previous is null)
            {
                return false;
            }

            var nestedName = list.Name switch
            {
                "steps" or "steps-unordered" => "substeps",
                "substeps" => "substeps",
                "ol" => "ol",
                _ => "ul"
            };

            var itemName = nestedName == "substeps" ? "substep" : "li";
            var nested = previous.ElementChildren().FirstOrDefault(c => c.Name == nestedName);
            if (nested is null)
            {
                nested = EditCommands.Append(previous, nestedName);
                if (nested is null)
                {
                    return false;
                }

                foreach (var auto in nested.Children.ToList())
                {
                    nested.Remove(auto);
                }
            }

            item.RemoveSelf();
            nested.Add(item);
            if (item.Name != itemName)
            {
                EditCommands.ChangeElementName(item, itemName);
            }
        }
        else
        {
            var grandItem = list.Parent;
            if (grandItem is null || grandItem.Name is not ("li" or "step" or "substep"))
            {
                return false;
            }

            var outerList = grandItem.Parent;
            if (outerList is null)
            {
                return false;
            }

            var index = outerList.IndexOf(grandItem) + 1;
            item.RemoveSelf();

            var outerItemName = outerList.Name is "steps" or "steps-unordered" ? "step" : "li";
            outerList.Insert(index, item);
            if (item.Name != outerItemName)
            {
                EditCommands.ChangeElementName(item, outerItemName);
            }

            if (list.Children.Count == 0)
            {
                list.RemoveSelf();
            }
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        var focus = item.FirstElement("cmd") ?? item;
        Rebuild(focus, 0);
        return true;
    }

    private bool MoveFocus(DitaNode node, int direction)
    {
        if (!_editors.TryGetValue(node, out var editor))
        {
            return false;
        }

        var index = _order.IndexOf(editor) + direction;
        if (index < 0 || index >= _order.Count)
        {
            return false;
        }

        var next = _order[index];
        next.Focus();
        if (direction > 0)
        {
            next.PlaceCaretAt(0);
        }
        else
        {
            next.PlaceCaretAtEnd();
        }

        return true;
    }

    // --------------------------------------------------------------- команды

    /// <summary>Сохраняет незаписанные правки текущего редактора в модель.</summary>
    public void FlushPendingEdits()
    {
        foreach (var editor in _order)
        {
            editor.Flush();
        }
    }

    public InlineEditor? EditorFor(DitaNode node) => _editors.TryGetValue(node, out var e) ? e : null;

    /// <summary>Вставляет элемент рядом с текущим или внутрь него.</summary>
    public bool InsertElement(string elementName)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        FlushPendingEdits();
        BeforeStructuralEdit?.Invoke(this, $"Вставка <{elementName}>");

        var created = EditCommands.InsertAfter(CurrentNode, elementName);
        if (created is null && CurrentNode.Parent is not null)
        {
            created = EditCommands.Append(CurrentNode, elementName);
        }

        if (created is null)
        {
            var ancestor = CurrentNode.Parent;
            while (ancestor is not null && created is null)
            {
                created = EditCommands.InsertAfter(ancestor, elementName)
                          ?? EditCommands.Append(ancestor, elementName);
                ancestor = ancestor.Parent;
            }
        }

        if (created is null)
        {
            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(created), 0);
        return true;
    }

    private static DitaNode FirstEditable(DitaNode node)
    {
        foreach (var candidate in node.DescendantsAndSelf())
        {
            if (candidate.Kind != NodeKind.Element)
            {
                continue;
            }

            var def = DitaCatalog.Default.Get(candidate.Name);
            if (def is not null && def.IsMixed)
            {
                return candidate;
            }
        }

        return node;
    }

    public bool DeleteCurrent()
    {
        if (Document is null || CurrentNode is null || CurrentNode.Parent is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Удаление <{CurrentNode.Name}>");
        var parent = CurrentNode.Parent;
        var focus = EditCommands.PreviousElement(CurrentNode) ?? parent;
        EditCommands.Delete(CurrentNode);
        CurrentNode = null;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(focus), 0);
        return true;
    }

    public bool MoveCurrent(bool up)
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, up ? "Перемещение вверх" : "Перемещение вниз");
        var moved = up ? EditCommands.MoveUp(CurrentNode) : EditCommands.MoveDown(CurrentNode);
        if (!moved)
        {
            return false;
        }

        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return true;
    }

    public bool WrapCurrentInline(string elementName)
    {
        if (CurrentNode is null || !_editors.TryGetValue(CurrentNode, out var editor))
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Оформление <{elementName}>");
        editor.WrapSelection(elementName);
        return true;
    }

    public bool InsertInlineNode(DitaNode node)
    {
        if (CurrentNode is null || !_editors.TryGetValue(CurrentNode, out var editor))
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, $"Вставка <{node.Name}>");
        editor.InsertInlineNode(node);
        return true;
    }
}
