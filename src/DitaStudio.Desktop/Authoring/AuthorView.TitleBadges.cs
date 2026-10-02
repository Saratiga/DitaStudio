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

// Подписи у заголовков: номер рисунка и таблицы, уровень заголовка, нумерация.
public sealed partial class AuthorView
{
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
        var labels = Labels.For(root.GetAttribute("xml:lang")); // язык документа, нет его — язык интерфейса
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
            ToolTip.SetTip(mark, Loc.T("Author_NumberedParagraphNumberFromTheHeadings"));
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
            ToolTip.SetTip(mark, Loc.T("Author_CaptionWhenPublishedTheNumberRuns"));
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
            Text = Loc.T("Author_NoNumberNotInTableOf"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Themed(badge, TextBlock.ForegroundProperty, "EditorTag");
        ToolTip.SetTip(badge, Loc.T("Author_WhenPublishedTheTitleIsNot"));
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
}
