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

    /// <summary>
    /// Блок «на отдельном листе» (outputclass place-…): над ним — подпись с положением. В самом
    /// «Авторе» блок остаётся в тексте: лист есть только у печатного издания (PDF, DOCX).
    /// </summary>
    private static Control WithPlacementMark(string place, Control view)
    {
        var mark = new TextBlock
        {
            Text = "▣ Отдельный лист · " + PagePlacement.LabelOf(place)?.ToLowerInvariant(),
            FontSize = 11,
            Margin = new Thickness(4, 4, 0, 0),
            Tag = "placement-mark"
        };
        Themed(mark, TextBlock.ForegroundProperty, "Accent");
        ToolTip.SetTip(mark, "В PDF и DOCX блок стоит на своей странице в выбранной области листа; на сайте — в тексте. " +
                             "Изменить: контекстное меню → Оформление → Положение на листе.");
        return new StackPanel { Tag = ViewWrapperTag, Children = { mark, view } };
    }

    /// <summary>Показывать у блоков серые пометки условных атрибутов (<c>product</c>, <c>audience</c>…) и <c>outputclass</c>.</summary>
    public static bool AttributeNotesEnabled { get; set; } = true;

    private static readonly string[] ConditionAttributes = { "product", "audience", "platform", "props", "otherprops", "deliveryTarget" };

    // Классы, которые «Автор» показывает иначе (оформление текста, положение на листе, высота строки) — в пометку не попадают.
    private static readonly string[] HiddenClassPrefixes = { "align-", "size-", "color-", "row-height-", "place-", "page-break-" };

    // Служебные атрибуты: идентификатор, пространства имён и «технический» class каталога — пользователю не интересны.
    private static readonly string[] ServiceAttributes = { "id", "class", "domains", "xtrf", "xtrc" };

    // Раскладка таблицы (CALS) и простой таблицы: ею управляют команды и рамка таблицы в «Авторе», в пометки она не попадает.
    private static readonly string[] TableLayoutAttributes =
    {
        "cols", "colname", "colnum", "colwidth", "colsep", "rowsep", "frame", "namest", "nameend", "spanname", "morerows",
        "align", "valign", "char", "charoff", "scale", "pgwide", "rowheader", "keycol", "relcolwidth", "orient", "tabstyle", "tocentry", "shortdesc"
    };

    private const int NoteValueLimit = 60;

    /// <summary>
    /// Текст серой пометки у блока с атрибутами: «product: Альфа, Бета · rev: 2 · class: warning-box». Условные атрибуты — первыми,
    /// остальные по алфавиту, классы оформления (<c>outputclass</c>) — в конце. Не показываются служебные атрибуты, раскладка
    /// таблицы и классы, которые «Автор» рисует иначе. null — у элемента нечего показывать.
    /// </summary>
    public static string? AttributeNote(DitaNode node)
    {
        var parts = new List<string>();
        void Add(string name, string value)
        {
            var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }

            var text = ConditionAttributes.Contains(name) ? string.Join(", ", tokens) : string.Join(" ", tokens);
            parts.Add($"{name}: {(text.Length > NoteValueLimit ? text[..(NoteValueLimit - 1)] + "…" : text)}");
        }

        foreach (var name in ConditionAttributes)
        {
            if (node.GetAttribute(name) is { } value)
            {
                Add(name, value);
            }
        }

        var others = node.Attributes
            .Where(a => !ConditionAttributes.Contains(a.Name) && a.Name != "outputclass" && !ServiceAttributes.Contains(a.Name) &&
                        !TableLayoutAttributes.Contains(a.Name) && !a.Name.StartsWith("xml:", StringComparison.Ordinal) &&
                        !a.Name.StartsWith("xmlns", StringComparison.Ordinal) && !a.Name.StartsWith("ditaarch:", StringComparison.Ordinal))
            .OrderBy(a => a.Name, StringComparer.Ordinal);
        foreach (var attribute in others)
        {
            Add(attribute.Name, attribute.Value);
        }

        var classes = (node.GetAttribute("outputclass") ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !HiddenClassPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        if (classes.Count > 0)
        {
            Add("class", string.Join(" ", classes));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static Control WithAttributeNote(string text, Control view)
    {
        var note = new TextBlock { Text = text, FontSize = 10.5, Margin = new Thickness(4, 2, 0, 0), Tag = "attribute-note", TextWrapping = TextWrapping.Wrap };
        Themed(note, TextBlock.ForegroundProperty, "EditorTag");
        ToolTip.SetTip(note, "Атрибуты элемента: при условной сборке (product, audience…) он попадает в публикацию только для выбранных значений. " +
                             "Правка — панель «Атрибуты». Показ пометок: «Структура → Показывать пометки атрибутов».");
        return new StackPanel { Tag = ViewWrapperTag, Children = { note, view } };
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

    /// <summary>
    /// Пометки, видные без атрибутов: у заголовка «без номера» — справа, у нумерованного абзаца —
    /// «№» слева (сам номер зависит от места в карте и считается при публикации).
    /// </summary>
    /// <summary>«Рисунок N.» / «Таблица N.»: номер — по порядку подписанных рисунков (таблиц) в топике.</summary>
    private static string CaptionBadgeText(DitaNode captioned)
    {
        var root = captioned;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var number = root.DescendantsAndSelf().Where(n => n.Name == captioned.Name && n.FirstElement("title") is not null)
            .TakeWhile(n => !ReferenceEquals(n, captioned)).Count() + 1;
        var labels = Labels.For("ru");
        // Пустое название — подпись «Рисунок N» без точки; точка появляется вместе с названием.
        return $"{(captioned.Name == "fig" ? labels.Figure : labels.Table)} {number}" + (CaptionRules.HasContent(captioned.FirstElement("title")) ? "." : string.Empty);
    }

    /// <summary>Пересчитывает номера подписей после правки: добавленный или убранный рисунок сдвигает соседние.</summary>
    private void RefreshCaptionBadges()
    {
        foreach (var badge in _panel.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Tag is DitaNode { Name: "fig" or "table" }))
        {
            badge.Text = CaptionBadgeText((DitaNode)badge.Tag!);
        }
    }

    private static Control WithTitleBadge(DitaNode node, BlockEditor editor)
    {
        if (node.Name == "p" && HeadingNumbering.IsNumbered(node))
        {
            var mark = new TextBlock { Text = "№", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 6, 0) };
            Themed(mark, TextBlock.ForegroundProperty, "Accent");
            ToolTip.SetTip(mark, "Нумерованный абзац: номер по заголовкам (например, 2.3.1) — при публикации");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(editor, 1);
            row.Children.Add(mark);
            row.Children.Add(editor);
            return row;
        }

        if (node.Name == "title" && node.Parent is { Name: "fig" or "table" } captioned)
        {
            // Подпись «Рисунок N.» / «Таблица N.»: в файле её нет, она складывается при публикации.
            var mark = new TextBlock
            {
                Text = CaptionBadgeText(captioned),
                Tag = captioned,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 1, 6, 0)
            };
            Themed(mark, TextBlock.ForegroundProperty, "Accent");
            ToolTip.SetTip(mark, "Подпись при публикации. Номер сквозной по изданию и зависит от настройки «Нумеровать рисунки и таблицы»; здесь — по порядку в этом топике.");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(editor, 1);
            row.Children.Add(mark);
            row.Children.Add(editor);
            return row;
        }

        if (node.Name != "title" || !TocRules.IsUnnumbered(node))
        {
            return editor;
        }

        var badge = new TextBlock
        {
            Text = "без номера · не в оглавлении",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Themed(badge, TextBlock.ForegroundProperty, "EditorTag");
        ToolTip.SetTip(badge, "При публикации заголовок не нумеруется и не попадает в оглавление (outputclass=\"nonumber\").");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(editor);
        grid.Children.Add(badge);
        return grid;
    }

    /// <summary>Расстояние от верха блока до первой строки текста — чтобы номер или маркер
    /// пункта стоял на одной линии с текстом шага, а не выше него.</summary>
    private static double FirstTextOffset(Control element)
    {
        var offset = 0.0;
        for (Control? current = element; current is not null and not BlockEditor;)
        {
            offset += current.Margin.Top;
            switch (current)
            {
                case Border border:
                    offset += border.Padding.Top + border.BorderThickness.Top;
                    current = border.Child;
                    break;
                case Panel panel:
                    current = panel.Children.FirstOrDefault(c => c is not TextBlock) ?? panel.Children.FirstOrDefault();
                    if (current is TextBlock)
                    {
                        return offset;
                    }

                    break;
                default:
                    return offset;
            }
        }

        return offset;
    }

    private static int TitleLevel(DitaNode topic)
    {
        var level = 1;
        for (var parent = topic.Parent; parent is not null; parent = parent.Parent)
        {
            if (DitaCatalog.Default.Get(parent.Name)?.IsTopicType == true)
            {
                level++;
            }
        }

        return Math.Min(level, 4);
    }

    private static string NormalizedTitle(DitaNode title) =>
        string.Join(' ', title.InnerText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
        editor.ChipFactory = chip => chip.Name == "image" && TryLoadImage(chip) is { } bitmap
            ? new ResizableImage(bitmap, ResizableImage.WidthFromAttributes(chip.GetAttribute("width"), chip.GetAttribute("height"), bitmap),
                480, 240, width => ResizeImage(chip, width, editor))
            : null;
        editor.Focused += (_, _) =>
        {
            ClearOutline(); // курсор в тексте — выделение таблицы целиком снимается
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
        editor.WrapRequested += (_, element) => Surface.WrapCurrentInline(element);
        editor.StructureRequested += OnStructureRequested;

        _editors.TryAdd(node, editor);
        _order.Add(editor);
        return editor;
    }

    private Control BuildPreformatted(DitaNode node)
    {
        var box = new TextBox
        {
            Text = node.InnerText.Trim('\n'),
            AcceptsReturn = true,
            AcceptsTab = true,
            FontSize = 13,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            TextWrapping = TextWrapping.NoWrap
        };
        Themed(box, TextBox.FontFamilyProperty, "MonoFont");
        Themed(box, TextBox.BackgroundProperty, "CodeBackground");
        Themed(box, TextBox.BorderBrushProperty, "Line");
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);

        box.TextChanged += (_, _) =>
        {
            node.SetText(box.Text ?? string.Empty);
            Modified();
        };
        box.GotFocus += (_, _) => CurrentNode = node;

        var container = new Border { Child = box, Margin = new Thickness(0, 8, 0, 8) };
        AttachSelection(container, node);
        return container;
    }

    private Control BuildImageBlock(DitaNode node)
    {
        var href = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "—";
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        if (TryLoadImage(node) is { } bitmap)
        {
            panel.Children.Add(new ResizableImage(bitmap,
                ResizableImage.WidthFromAttributes(node.GetAttribute("width"), node.GetAttribute("height"), bitmap),
                520, 260, width => ResizeImage(node, width, null))
            {
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

        var caption = new TextBlock { Text = href, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Themed(caption, TextBlock.ForegroundProperty, "EditorTag");
        Themed(caption, TextBlock.FontFamilyProperty, "MonoFont");
        panel.Children.Add(caption);

        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        Themed(border, Border.BackgroundProperty, "SurfaceAlt");
        Themed(border, Border.BorderBrushProperty, "Line");

        AttachSelection(border, node);
        return border;
    }

    /// <summary>Размер картинки мышью: @width в px, @height снимается — пропорции сохраняются.</summary>
    private void ResizeImage(DitaNode image, double width, BlockEditor? editor)
    {
        BeforeStructuralEdit?.Invoke(this, "Размер изображения");
        image.SetAttribute("width", width.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "px");
        image.RemoveAttribute("height");
        Modified();
        editor?.TextArea.TextView.Redraw();
    }

    private Bitmap? TryLoadImage(DitaNode node)
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
            // Через поток, чтобы файл картинки не оставался открытым (его могут заменить).
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // SVG и повреждённые файлы Bitmap не читает — показываем значок.
            return null;
        }
    }

    private Control BuildEmptyRow(DitaNode node)
    {
        var text = new TextBlock
        {
            Text = $"<{node.Name}{FormatAttributes(node)}/>",
            FontSize = 11.5,
            Margin = new Thickness(4, 3, 0, 3),
            TextWrapping = TextWrapping.Wrap
        };
        Themed(text, TextBlock.FontFamilyProperty, "MonoFont");
        Themed(text, TextBlock.ForegroundProperty, "EditorTag");

        var border = new Border { Child = text, Padding = new Thickness(2), Background = Brushes.Transparent };
        AttachSelection(border, node);
        return border;
    }

    private Control BuildConrefRow(DitaNode node)
    {
        var chip = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = $"⇗ {node.GetAttribute("conref") ?? node.GetAttribute("conkeyref")}", FontSize = 11.5 }
        };
        Themed(chip, Border.BackgroundProperty, "EditorChipBackground");
        Themed(chip, Border.BorderBrushProperty, "EditorChipBorder");
        Themed(chip.Child, TextBlock.ForegroundProperty, "EditorChipText");
        Themed(chip.Child, TextBlock.FontFamilyProperty, "MonoFont");
        ToolTip.SetTip(chip, "Содержимое подставляется по conref при публикации; правится в исходном топике.");

        var border = new Border { Child = TaggedRow(node, chip), Padding = new Thickness(4, 3, 4, 3), Background = Brushes.Transparent };
        AttachSelection(border, node);
        return border;
    }

    /// <summary>Подпись элемента слева от содержимого строки (если подписи включены).</summary>
    private Control TaggedRow(DitaNode node, Control content)
    {
        if (!Surface.ShowElementTags)
        {
            return content;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*") };
        var tag = new TextBlock
        {
            Text = node.Name,
            FontSize = 10.5,
            Margin = new Thickness(0, 4, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Themed(tag, TextBlock.FontFamilyProperty, "MonoFont");
        Themed(tag, TextBlock.ForegroundProperty, "EditorTag");

        Grid.SetColumn(tag, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(tag);
        grid.Children.Add(content);
        return grid;
    }

    private static string FormatAttributes(DitaNode node) =>
        node.Attributes.Count == 0 ? string.Empty : " " + string.Join(" ", node.Attributes.Select(a => $"{a.Name}=\"{a.Value}\""));

    private Control BuildComment(DitaNode node)
    {
        var text = new TextBlock { Text = "// " + node.Value.Trim(), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        Themed(text, TextBlock.FontFamilyProperty, "MonoFont");
        Themed(text, TextBlock.ForegroundProperty, "TextMuted");

        var border = new Border { Child = text, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(6, 3, 6, 3) };
        Themed(border, Border.BackgroundProperty, "SurfaceAlt");
        AttachSelection(border, node); // комментарий выделяется щелчком и удаляется клавишей, как любой блок
        return border;
    }

    private Control BuildUnknown(DitaNode node)
    {
        var text = new TextBlock { Text = Core.Model.XmlSerializer.ToXml(node), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        Themed(text, TextBlock.FontFamilyProperty, "MonoFont");
        Themed(text, TextBlock.ForegroundProperty, "Danger");

        var border = new Border
        {
            Child = text,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 4, 4, 4),
            Margin = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent
        };
        Themed(border, Border.BorderBrushProperty, "Danger");

        AttachSelection(border, node);
        return border;
    }

    private Control BuildMetaBlock(DitaNode node)
    {
        var xml = new TextBox
        {
            Text = Core.Model.XmlSerializer.ToXml(node),
            IsReadOnly = true,
            FontSize = 11.5,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.NoWrap
        };
        Themed(xml, TextBox.FontFamilyProperty, "MonoFont");
        Themed(xml, TextBox.BackgroundProperty, "SurfaceAlt");

        var expander = new Expander
        {
            Header = $"{node.Name} — метаданные",
            FontSize = 11.5,
            Margin = new Thickness(0, 6, 0, 6),
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = xml
        };
        Themed(expander, ForegroundProperty, "EditorTag");

        var border = new Border { Child = expander };
        AttachSelection(border, node);
        return border;
    }
}
