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
                Foreground = EditorText,
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
                    Foreground = EditorTextMuted,
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
                editor.Foreground = EditorTextMuted;
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
                editor.Foreground = EditorTextMuted;
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
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = EditorText
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
                Foreground = EditorTextMuted,
                TextWrapping = TextWrapping.Wrap
            },
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(6, 3, 6, 3),
            Background = MetaBackground
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
                Foreground = ThemeManager.Brush("Danger")
            },
            BorderBrush = ThemeManager.Brush("Danger"),
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

            stack.Children.Add(BuildCalsGrid(tgroup, rows, headerCount));
        }

        var border = new Border { Child = stack, Tag = node };
        AttachSelection(border, node);
        return border;
    }

    /// <summary>Сетка CALS-таблицы с учётом объединённых ячеек (namest/nameend, morerows).
    /// Колонка ячейки берётся из colspec (если он есть), иначе — из позиции по порядку.</summary>
    private FrameworkElement BuildCalsGrid(DitaNode tgroup, List<DitaNode> rows, int headerCount)
    {
        var colNames = ReadColumnNames(tgroup, rows);
        var columns = colNames.Count;

        var grid = new Grid();
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // occupied[r, c] — колонка занята объединением ячейки из более ранней строки (rowspan).
        var occupied = new bool[rows.Count, Math.Max(columns, 1)];

        for (var r = 0; r < rows.Count; r++)
        {
            PlaceCalsRowCells(grid, rows, r, columns, headerCount, colNames, occupied);
        }

        return grid;
    }

    /// <summary>Раскладывает ячейки одной строки CALS-таблицы по сетке с учётом namest/nameend (colspan)
    /// и morerows (rowspan); помечает занятые клетки в <paramref name="occupied"/>.</summary>
    private void PlaceCalsRowCells(Grid grid, List<DitaNode> rows, int r, int columns, int headerCount, List<string> colNames, bool[,] occupied)
    {
        var isHeader = r < headerCount;
        var cursor = 0;

        foreach (var entry in rows[r].ElementChildren().Where(e => e.Name == "entry"))
        {
            while (cursor < columns && occupied[r, cursor])
            {
                cursor++;
            }

            var colSpan = 1;
            var namest = entry.GetAttribute("namest");
            var nameend = entry.GetAttribute("nameend");
            if (!string.IsNullOrEmpty(namest) && !string.IsNullOrEmpty(nameend))
            {
                var startIdx = colNames.IndexOf(namest!);
                var endIdx = colNames.IndexOf(nameend!);
                if (startIdx >= 0 && endIdx >= startIdx)
                {
                    colSpan = endIdx - startIdx + 1;
                }
            }

            var rowSpan = 1;
            if (int.TryParse(entry.GetAttribute("morerows"), out var more) && more > 0)
            {
                rowSpan = more + 1;
            }

            var editor = CreateEditor(entry);
            editor.FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal;

            var cellBorder = new Border
            {
                Child = editor,
                BorderBrush = ContainerBorder,
                BorderThickness = new Thickness(cursor == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                Padding = new Thickness(7, 5, 7, 5),
                Background = isHeader ? MetaBackground : Brushes.Transparent,
                Tag = entry
            };
            AttachSelection(cellBorder, entry);

            Grid.SetRow(cellBorder, r);
            Grid.SetColumn(cellBorder, Math.Min(cursor, Math.Max(columns - 1, 0)));
            Grid.SetColumnSpan(cellBorder, Math.Max(1, Math.Min(colSpan, columns - cursor)));
            Grid.SetRowSpan(cellBorder, Math.Max(1, Math.Min(rowSpan, rows.Count - r)));
            grid.Children.Add(cellBorder);

            for (var rr = r; rr < Math.Min(r + rowSpan, rows.Count); rr++)
            {
                for (var cc = cursor; cc < Math.Min(cursor + colSpan, columns); cc++)
                {
                    occupied[rr, cc] = true;
                }
            }

            cursor += colSpan;
        }
    }

    /// <summary>Список имён колонок из colspec; если их нет — просто "c1".."cN" по числу колонок в строках.
    /// В отличие от EditCommands.EnsureColumnNames, ничего не меняет в документе — только читает.</summary>
    private static List<string> ReadColumnNames(DitaNode tgroup, List<DitaNode> rows)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        if (colspecs.Count > 0)
        {
            var names = new List<string>(colspecs.Count);
            for (var i = 0; i < colspecs.Count; i++)
            {
                var name = colspecs[i].GetAttribute("colname");
                names.Add(string.IsNullOrEmpty(name) ? $"c{i + 1}" : name!);
            }

            return names;
        }

        var count = int.TryParse(tgroup.GetAttribute("cols"), out var cols) && cols > 0
            ? cols
            : rows.Count == 0 ? 1 : rows.Max(r => r.ElementChildren().Count(c => c.Name == "entry"));

        return Enumerable.Range(1, Math.Max(count, 1)).Select(i => $"c{i}").ToList();
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

    /// <summary>Объединяет выделенную ячейку CALS-таблицы со следующей в строке (colspan).</summary>
    public bool MergeCurrentCellRight()
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение ячеек по горизонтали");
        var merged = EditCommands.MergeTableCellRight(CurrentNode);
        if (merged is null)
        {
            return false;
        }

        CurrentNode = merged;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(merged), 0);
        return true;
    }

    /// <summary>Объединяет выделенную ячейку CALS-таблицы с той же колонкой строкой ниже (rowspan).</summary>
    public bool MergeCurrentCellDown()
    {
        if (Document is null || CurrentNode is null)
        {
            return false;
        }

        BeforeStructuralEdit?.Invoke(this, "Объединение ячеек по вертикали");
        var merged = EditCommands.MergeTableCellDown(CurrentNode);
        if (merged is null)
        {
            return false;
        }

        CurrentNode = merged;
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(merged), 0);
        return true;
    }

    /// <summary>Переключает класс вывода (outputclass) на выделенном элементе — например,
    /// разрыв страницы перед заголовком при печати. Возвращает новое состояние (включён/выключён).</summary>
    public bool? ToggleCurrentOutputClass(string token)
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit?.Invoke(this, "Изменение оформления вывода");
        var enabled = EditCommands.ToggleOutputClassToken(CurrentNode, token);
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return enabled;
    }

    /// <summary>Отмечает/снимает пометку изменения (атрибут rev) на текущем элементе — штатный
    /// DITA-механизм ревизий; при публикации рисуется полосой на полях (см. BuildClassAttr в
    /// HtmlRenderer). Не полноценный track changes — просто «здесь что-то менялось».</summary>
    public bool? ToggleCurrentRev()
    {
        if (Document is null || CurrentNode is null)
        {
            return null;
        }

        BeforeStructuralEdit?.Invoke(this, "Отметка изменения (rev)");
        var hasRev = !string.IsNullOrWhiteSpace(CurrentNode.GetAttribute("rev"));
        CurrentNode.SetAttribute("rev", hasRev ? null : "changed");
        Document.IsDirty = true;
        DocumentModified?.Invoke(this, EventArgs.Empty);
        Rebuild(FirstEditable(CurrentNode), 0);
        return !hasRev;
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
