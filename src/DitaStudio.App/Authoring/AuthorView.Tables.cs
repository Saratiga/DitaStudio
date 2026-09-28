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

// Таблицы в режиме «Автор»: построение сетки CALS (с объединёнными ячейками) и simpletable.
public sealed partial class AuthorView
{
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
}
