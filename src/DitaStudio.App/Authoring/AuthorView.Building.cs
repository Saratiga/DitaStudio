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

// Построение визуального дерева режима «Автор» из DOM документа.
public sealed partial class AuthorView
{
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
}
