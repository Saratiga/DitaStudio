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
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

// Особые блоки: код, изображения, пустая строка, conref, комментарий, неизвестный элемент, метаданные.
public sealed partial class AuthorView
{
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
        BeforeStructuralEdit?.Invoke(this, Loc.T("Author_ImageSize"));
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
        ToolTip.SetTip(chip, Loc.T("Author_TheContentIsSubstitutedByConref"));

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
            Header = Loc.T("Author_0Metadata", node.Name),
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
