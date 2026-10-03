using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

// Построение визуального дерева режима «Автор» из DOM документа (как WPF AuthorView.Building).
public sealed partial class AuthorView
{
    /// <summary>Строит представление узла и запоминает его — для частичной перестройки.</summary>
    private Control? BuildNode(DitaNode node, int depth)
    {
        var view = BuildNodeCore(node, depth);
        if (view is not null && PagePlacement.Of(node) is { } place)
        {
            view = WithPlacementMark(place, view);
        }

        if (view is not null && AttributeNotesEnabled && AttributeNote(node) is { } note)
        {
            view = WithAttributeNote(note, view);
        }

        if (view is not null)
        {
            _views[node] = view;
            _viewNodes[view] = node;
        }

        return view;
    }

    private Control? BuildNodeCore(DitaNode node, int depth)
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
            return BuildUnknown(node);
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
                return BuildCalsTable(node);

            case "simpletable":
            case "properties":
            case "choicetable":
                return BuildSimpleTable(node);

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
            return BuildContainer(node, depth, headerText: null, backgroundKey: null);
        }

        if (node.Children.Count == 0 && (node.HasAttribute("conref") || node.HasAttribute("conkeyref")))
        {
            // Содержимое подставится из другого топика при публикации — писать сюда нечего.
            return BuildConrefRow(node);
        }

        if (def.Display is DisplayKind.Block or DisplayKind.Inline && InlineContent.HasStructuralChildren(node))
        {
            // Текст вперемешку с вложенными блоками (пункт со вложенным списком, заметка из
            // абзацев) — контейнер: участки текста отдельными редакторами, блоки — блоками.
            return BuildContainerByName(node, depth);
        }

        return def.Display switch
        {
            DisplayKind.Block => BuildTextBlockRow(node, InlineContent.FromNode(node)),
            DisplayKind.Inline => BuildTextBlockRow(node, InlineContent.FromNode(node)),
            DisplayKind.Empty => BuildEmptyRow(node),
            DisplayKind.Meta => BuildMetaBlock(node),
            DisplayKind.Preformatted => BuildPreformatted(node),
            _ => BuildContainerByName(node, depth)
        };
    }

    private Control BuildContainerByName(DitaNode node, int depth)
    {
        string? header = null;
        string? background = null;

        switch (node.Name)
        {
            case "note":
            {
                var type = node.GetAttribute("type") ?? "note";
                header = Labels.NoteLabel(type);
                background = type is "caution" or "danger" or "warning" or "attention" or "notice"
                    ? "EditorWarnBackground"
                    : "EditorNoteBackground";
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

    /// <summary>
    /// Блок-обёртка без собственного вида (div, bodydiv, section, sectiondiv, fig и их
    /// специализации — по @class): его границы в «Авторе» показываются полосой слева и подписью.
    /// </summary>
    private static bool IsFramedWrapper(DitaNode node) =>
        DitaCatalog.Default.Get(node.Name)?.ClassAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token is "topic/div" or "topic/bodydiv" or "topic/section" or "topic/sectiondiv" or
                "topic/fig" or "topic/figgroup" or "topic/abstract") == true;

    private Control BuildContainer(DitaNode node, int depth, string? headerText, string? backgroundKey)
    {
        var stack = new StackPanel();
        var framed = headerText is null && backgroundKey is null && IsFramedWrapper(node);
        if (framed)
        {
            var outputclass = node.GetAttribute("outputclass");
            var label = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(outputclass) ? node.Name : $"{node.Name} · {outputclass}",
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 2)
            };
            Themed(label, TextBlock.FontFamilyProperty, "MonoFont");
            Themed(label, TextBlock.ForegroundProperty, "EditorTag");
            stack.Children.Add(label);
        }

        if (headerText is not null)
        {
            var header = new TextBlock
            {
                Text = headerText,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 4)
            };
            Themed(header, TextBlock.ForegroundProperty, "TextPrimary");
            stack.Children.Add(header);
        }

        var isList = node.Name is "ul" or "ol" or "sl" or "steps" or "steps-unordered" or "substeps" or "choices";
        var counter = 0;

        // Смешанное содержимое: участки текста между вложенными блоками — редакторами. У
        // контейнеров (section, div) пустые участки между блоками не показываются — писать текст
        // прямо в раздел можно, но обычно не нужно; у блоков (p, li, note, entry) участок есть
        // всегда. Но контейнер без единого вложенного блока (пустой div, только что вставленный
        // sectiondiv) получает поле ввода — иначе в него нельзя ничего напечатать.
        var def = DitaCatalog.Default.Get(node.Name);
        var mixed = def is { IsMixed: true };
        var noBlocks = mixed && !node.Children.Any(InlineContent.IsStructuralChild);
        var segments = !mixed
            ? Array.Empty<(DitaNode? After, DitaNode? Before)>()
            : InlineContent.Segments(node)
                .Where(s => def!.Display != DisplayKind.Container || noBlocks ||
                            InlineContent.Segment(node, s.After, s.Before).Length > 0)
                .ToArray();

        void AddSegmentBefore(DitaNode? before)
        {
            foreach (var (after, end) in segments.Where(s => ReferenceEquals(s.Before, before)))
            {
                stack.Children.Add(BuildTextBlockRow(node, InlineContent.Segment(node, after, end)));
            }
        }

        foreach (var child in node.Children)
        {
            if (mixed && !InlineContent.IsStructuralChild(child))
            {
                continue;
            }

            AddSegmentBefore(child);
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

                var row = new Grid { Margin = new Thickness(0, 1, 0, 1), ColumnDefinitions = new ColumnDefinitions("26,*") };
                var markerBlock = new TextBlock
                {
                    Text = marker,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 1 + FirstTextOffset(element), 8, 0)
                };
                Themed(markerBlock, TextBlock.ForegroundProperty, "TextMuted");
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

        AddSegmentBefore(null);
        if (!isList && segments.Length == 0)
        {
            _childPanels[node] = stack;
        }

        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(headerText is null ? 0 : 10, headerText is null ? 0 : 6, 0, headerText is null ? 0 : 6),
            Margin = new Thickness(0, node.Name is "body" or "conbody" or "taskbody" or "refbody" or "troublebody" ? 4 : 6, 0, 4),
            BorderThickness = new Thickness(backgroundKey is null ? 0 : 1),
            CornerRadius = new CornerRadius(4)
        };

        if (backgroundKey is not null)
        {
            Themed(border, Border.BackgroundProperty, backgroundKey);
            Themed(border, Border.BorderBrushProperty, "Line");
        }

        if (node.Name is "section" or "example" or "fig")
        {
            border.BorderThickness = new Thickness(0);
            border.Margin = new Thickness(0, 12, 0, 8);
        }

        if (framed)
        {
            border.BorderThickness = new Thickness(2, 0, 0, 0);
            border.Padding = new Thickness(10, 2, 0, 2);
            border.CornerRadius = new CornerRadius(0);
            Themed(border, Border.BorderBrushProperty, "EditorChipBorder");
        }

        AttachSelection(border, node);
        return border;
    }

    private Control BuildTextBlockRow(DitaNode node, InlineContent content)
    {
        var editor = CreateEditor(content);

        switch (node.Name)
        {
            case "title":
            case "glossterm":
            {
                var level = node.Parent is not null && (DitaCatalog.Default.Get(node.Parent.Name)?.IsTopicType ?? false)
                    ? TitleLevel(node.Parent)
                    : 3;
                editor.FontSize = level switch { 1 => 25, 2 => 20, 3 => 17, _ => 15 };
                editor.FontWeight = FontWeight.SemiBold;
                editor.Margin = new Thickness(0, level == 1 ? 0 : 14, 0, 6);
                break;
            }

            case "shortdesc":
                editor.FontSize = 15;
                editor.FontStyle = FontStyle.Italic;
                Themed(editor, ForegroundProperty, "TextMuted");
                editor.Margin = new Thickness(0, 0, 0, 10);
                break;

            case "cmd":
                editor.FontWeight = FontWeight.Medium;
                break;

            case "dt":
            case "pt":
                editor.FontWeight = FontWeight.SemiBold;
                editor.Margin = new Thickness(0, 6, 0, 0);
                break;

            case "dd":
            case "pd":
                editor.Margin = new Thickness(20, 0, 0, 0);
                break;

            case "stepresult":
            case "info":
            case "stepxmp":
                Themed(editor, ForegroundProperty, "TextMuted");
                editor.Margin = new Thickness(0, 2, 0, 0);
                break;
        }

        // Вертикальные отступы — у строки, а не у редактора: иначе подпись элемента слева
        // остаётся наверху, а текст заголовка съезжает вниз.
        var container = new Border
        {
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 1 + editor.Margin.Top, 0, 1 + editor.Margin.Bottom),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = Brushes.Transparent
        };
        editor.Margin = new Thickness(editor.Margin.Left, 0, editor.Margin.Right, 0);
        ApplyBlockFormat(editor, node);

        container.Child = TaggedRow(node, WithTitleBadge(node, editor));
        AttachSelection(container, node);
        return container;
    }

    /// <summary>
    /// Оформление блока из «Автора» (классы align-…, у ячейки — @align): редактор смещается
    /// целиком — AvaloniaEdit не выравнивает строки внутри себя, поэтому короткий текст (заголовок,
    /// подпись, ячейка) стоит как в публикации, а у длинного абзаца видна только сторона.
    /// </summary>
    private static DitaNode FormatSource(DitaNode node, Func<DitaNode, bool> has)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (has(current))
            {
                return current;
            }
        }

        return node;
    }

    internal static void ApplyBlockFormat(BlockEditor editor, DitaNode node)
    {
        // Оформление контейнера (note, section, div) действует на всё внутри: берётся у ближайшего предка с классом.
        var aligned = FormatSource(node, n => TextFormatting.AlignmentOf(n) is not null);
        var sized = FormatSource(node, n => TextFormatting.SizeOf(n) is not null);
        var colored = FormatSource(node, n => TextFormatting.ColorOf(n) is not null);
        editor.HorizontalAlignment = TextFormatting.AlignmentOf(aligned) switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch
        };
        editor.MinWidth = editor.HorizontalAlignment == HorizontalAlignment.Stretch ? 0 : 40;
        if (TextFormatting.SizeOf(sized) is { } points)
        {
            editor.FontSize = points * 4.0 / 3;
        }

        if (TextFormatting.ColorOf(colored) is { } hex)
        {
            // Цвет текста редактора привязан к ресурсу темы — своя привязка его вытесняет.
            editor.Bind(ForegroundProperty, new Avalonia.Data.Binding { Source = new SolidColorBrush(Color.Parse(hex)) });
        }
    }

    private BlockEditor CreateEditor(InlineContent content)
    {
        var node = content.Node;
        var editor = new BlockEditor(content);
        editor.ContentChanged += (_, _) => Modified();
        editor.ContextMenuBuilding += (sender, items) => ContextMenuBuilding?.Invoke(sender, items);
        editor.Written += (_, _) => RefreshFootnotes();
        // Вопрос о переименовании файла — только если текст заголовка на выходе отличается от того, что был при входе.
        string? titleAtFocus = null;
        var isRootTitle = node.Name == "title" && Document is not null && ReferenceEquals(node.Parent, Document.Root);
        editor.GotFocusRecorded += (_, _) => titleAtFocus = isRootTitle ? NormalizedTitle(node) : null;
        editor.EditCommitted += (_, _) =>
        {
            if (isRootTitle && titleAtFocus is not null && titleAtFocus != NormalizedTitle(node))
            {
                titleAtFocus = NormalizedTitle(node);
                RootTitleCommitted?.Invoke(this, EventArgs.Empty);
            }
        };
        editor.ChipClicked += (_, e) => EditChip(e.Node, e.Control, editor);
        editor.ChipFactory = chip => chip.Name == "image" && TryLoadImage(chip) is { } bitmap
            ? new ResizableImage(bitmap, ResizableImage.WidthFromAttributes(chip.GetAttribute("width"), chip.GetAttribute("height"), bitmap),
                480, 240, width => ResizeImage(chip, width, editor))
            : null;
        editor.Focused += (_, _) =>
        {
            ClearOutline(); // курсор в тексте — выделение таблицы целиком снимается
            ClearCellSelection();
            // Выделенный текст в других блоках при переходе сюда снимается — иначе выделений на экране несколько.
            foreach (var other in _order)
            {
                if (!ReferenceEquals(other, editor) && !other.TextArea.Selection.IsEmpty)
                {
                    other.TextArea.ClearSelection();
                }
            }

            _activeEditor = editor;
            CurrentNode = node;
            HighlightEditor(editor);
        };
        editor.MouseSelectionFinished += (_, _) => OnMouseSelectionFinished(editor);
        editor.BlocksPasteRequested += (_, _) => _ = PasteBlocksAsync(editor.Node);
        editor.WrapRequested += (_, element) => Surface.WrapCurrentInline(element);
        editor.StructureRequested += OnStructureRequested;

        _editors.TryAdd(node, editor);
        _order.Add(editor);
        return editor;
    }
}
