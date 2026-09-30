using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

// Таблицы в режиме «Автор»: сетка CALS (с объединёнными ячейками) и simpletable — как в WPF-версии.
public sealed partial class AuthorView
{
    private Control BuildCalsTable(DitaNode node)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };

        if (node.FirstElement("title") is { } title)
        {
            var titleEditor = CreateEditor(InlineContent.FromNode(title));
            titleEditor.FontWeight = FontWeight.SemiBold;
            titleEditor.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(WithTitleBadge(title, titleEditor)); // слева — «Таблица N.»
        }

        foreach (var tgroup in node.ElementChildren().Where(e => e.Name == "tgroup"))
        {
            var rows = new List<DitaNode>();
            if (tgroup.FirstElement("thead") is { } head)
            {
                rows.AddRange(head.ElementChildren().Where(r => r.Name == "row"));
            }

            var headerCount = rows.Count;
            if (tgroup.FirstElement("tbody") is { } body)
            {
                rows.AddRange(body.ElementChildren().Where(r => r.Name == "row"));
            }

            stack.Children.Add(BuildCalsGrid(tgroup, rows, headerCount));
        }

        // Прозрачный фон: щелчок по полям над и под таблицей попадает в рамку и выделяет таблицу целиком.
        var border = new Border { Child = stack, Background = Brushes.Transparent };
        AttachSelection(border, node);
        return border;
    }

    /// <summary>Сетка CALS-таблицы с учётом объединённых ячеек (namest/nameend, morerows).
    /// Колонка ячейки берётся из colspec (если он есть), иначе — из позиции по порядку.</summary>
    private Control BuildCalsGrid(DitaNode tgroup, List<DitaNode> rows, int headerCount)
    {
        var colNames = ReadColumnNames(tgroup, rows);
        var columns = colNames.Count;

        var grid = new Grid();
        var fractions = TableLayout.CalsFractions(tgroup) is { } f && f.Length == columns ? f : null;
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(fractions?[c] * 100 ?? 1, GridUnitType.Star));
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(RowDefinitionFor(rows[r]));
        }

        // occupied[r, c] — колонка занята объединением ячейки из более ранней строки (rowspan).
        var occupied = new bool[rows.Count, Math.Max(columns, 1)];
        for (var r = 0; r < rows.Count; r++)
        {
            PlaceCalsRowCells(grid, rows, r, columns, headerCount, colNames, occupied);
        }

        TableResizeHandles.Attach(grid,
            weights => ResizeTable("Ширина столбцов", () => TableLayout.SetCalsWidths(tgroup, weights)),
            (row, height) => ResizeTable("Высота строки", () => TableLayout.SetRowHeight(rows[row], ToMm(height))));
        return grid;
    }

    /// <summary>Строка сетки: высота по содержимому, но не меньше заданной классом row-height-Nmm.</summary>
    private static RowDefinition RowDefinitionFor(DitaNode row) => new(GridLength.Auto)
    {
        MinHeight = TableLayout.RowHeightMm(row) is { } mm ? mm * 96 / 25.4 : 0
    };

    private static double? ToMm(double? pixels) => pixels is { } px && px > 0 ? px * 25.4 / 96 : null;

    /// <summary>Ширина столбцов или высота строки, заданные мышью: в модель, с точкой отмены.</summary>
    private void ResizeTable(string description, Action change)
    {
        BeforeStructuralEdit?.Invoke(this, description);
        change();
        Modified();
    }

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

            var rowSpan = int.TryParse(entry.GetAttribute("morerows"), out var more) && more > 0 ? more + 1 : 1;

            var cell = BuildCell(entry, isHeader, cursor == 0, r == 0);
            Grid.SetRow(cell, r);
            Grid.SetColumn(cell, Math.Min(cursor, Math.Max(columns - 1, 0)));
            Grid.SetColumnSpan(cell, Math.Max(1, Math.Min(colSpan, columns - cursor)));
            Grid.SetRowSpan(cell, Math.Max(1, Math.Min(rowSpan, rows.Count - r)));
            grid.Children.Add(cell);

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

    private Border BuildCell(DitaNode? cellNode, bool isHeader, bool firstColumn, bool firstRow)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(firstColumn ? 1 : 0, firstRow ? 1 : 0, 1, 1),
            Padding = new Thickness(7, 5, 7, 5),
            Background = Brushes.Transparent
        };
        Themed(border, Border.BorderBrushProperty, "Line");
        if (isHeader)
        {
            Themed(border, Border.BackgroundProperty, "SurfaceAlt");
        }

        if (cellNode is null)
        {
            border.Child = new TextBlock();
            return border;
        }

        if (InlineContent.HasStructuralChildren(cellNode))
        {
            // Ячейка из абзацев или со списком — вложенные блоки как в тексте топика.
            border.Child = BuildContainer(cellNode, 0, headerText: null, backgroundKey: null);
            AttachSelection(border, cellNode);
            return border;
        }

        var editor = CreateEditor(InlineContent.FromNode(cellNode));
        editor.FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal;
        ApplyBlockFormat(editor, cellNode);
        border.Child = editor;
        AttachSelection(border, cellNode);
        return border;
    }

    /// <summary>Имена колонок из colspec; если их нет — "c1".."cN" по числу колонок в строках.
    /// Ничего не меняет в документе — только читает.</summary>
    private static List<string> ReadColumnNames(DitaNode tgroup, List<DitaNode> rows)
    {
        var colspecs = tgroup.ElementChildren().Where(e => e.Name == "colspec").ToList();
        if (colspecs.Count > 0)
        {
            return colspecs.Select((spec, i) => spec.GetAttribute("colname") is { Length: > 0 } name ? name : $"c{i + 1}").ToList();
        }

        var count = int.TryParse(tgroup.GetAttribute("cols"), out var cols) && cols > 0
            ? cols
            : rows.Count == 0 ? 1 : rows.Max(r => r.ElementChildren().Count(c => c.Name == "entry"));

        return Enumerable.Range(1, Math.Max(count, 1)).Select(i => $"c{i}").ToList();
    }

    private Control BuildSimpleTable(DitaNode node)
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
        if (node.ElementChildren().FirstOrDefault(e => e.Name is "sthead" or "prophead" or "chhead") is { } head)
        {
            rows.Add(head);
        }

        var headerCount = rows.Count;
        rows.AddRange(node.ElementChildren().Where(e => e.Name is "strow" or "property" or "chrow"));

        var allNames = headNames.Concat(cellNames).ToArray();
        var columns = rows.Count == 0 ? 1 : rows.Max(r => r.ElementChildren().Count(c => allNames.Contains(c.Name)));

        var grid = new Grid();
        var fractions = TableLayout.SimpleFractions(node, columns) is { } f && f.Length == columns ? f : null;
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(fractions?[c] * 100 ?? 1, GridUnitType.Star));
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(RowDefinitionFor(rows[r]));
            var cells = rows[r].ElementChildren().Where(c => allNames.Contains(c.Name)).ToList();
            for (var c = 0; c < columns; c++)
            {
                var cell = BuildCell(c < cells.Count ? cells[c] : null, r < headerCount, c == 0, r == 0);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        TableResizeHandles.Attach(grid,
            weights => ResizeTable("Ширина столбцов", () => TableLayout.SetSimpleWidths(node, weights)),
            (row, height) => ResizeTable("Высота строки", () => TableLayout.SetRowHeight(rows[row], ToMm(height))));

        var border = new Border { Child = grid, Margin = new Thickness(0, 10, 0, 10) };
        AttachSelection(border, node);
        return border;
    }
}
