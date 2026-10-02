using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// Невидимые «ручки» на границах сетки таблицы «Автора»: между столбцами (курсор ↔) и под
/// строками (курсор ↕). Перетаскивание меняет ширину соседних столбцов или минимальную высоту
/// строки сразу на экране, отпускание сообщает итог — его пишут в DITA (доли colwidth /
/// relcolwidth, класс row-height-Nmm). Двойной щелчок по нижней границе строки — сброс высоты.
/// </summary>
public static class TableResizeHandles
{
    private const double MinColumn = 24;
    private const double MinRow = 16;

    /// <param name="columnsChanged">Новые веса столбцов (пропорции ширин).</param>
    /// <param name="rowChanged">Строка и её новая минимальная высота в px; null — высота сброшена.</param>
    public static void Attach(Grid grid, Action<double[]> columnsChanged, Action<int, double?> rowChanged)
    {
        var rows = Math.Max(1, grid.RowDefinitions.Count);
        var columns = grid.ColumnDefinitions.Count;
        for (var c = 0; c < columns - 1; c++)
        {
            grid.Children.Add(ColumnHandle(grid, c, rows, columnsChanged));
        }

        for (var r = 0; r < grid.RowDefinitions.Count; r++)
        {
            grid.Children.Add(RowHandle(grid, r, Math.Max(1, columns), rowChanged));
        }
    }

    private static Border ColumnHandle(Grid grid, int column, int rows, Action<double[]> changed)
    {
        var handle = new Border
        {
            Width = 7,
            Margin = new Thickness(0, 0, -3, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            ZIndex = 10,
            Tag = "column-handle"
        };
        ToolTip.SetTip(handle, Loc.T("Author_DragToChangeTheColumnWidth"));
        Grid.SetColumn(handle, column);
        Grid.SetRowSpan(handle, rows);

        Point? start = null;
        double[] widths = Array.Empty<double>();
        handle.PointerPressed += (_, e) =>
        {
            start = e.GetPosition(grid);
            // Звёздные ширины — по фактическим: раскладка не прыгнет в начале перетаскивания.
            widths = grid.ColumnDefinitions.Select(d => Math.Max(d.ActualWidth, 1)).ToArray();
            SetStars(grid, widths);
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not { } origin)
            {
                return;
            }

            var pair = widths[column] + widths[column + 1];
            var left = Math.Clamp(widths[column] + e.GetPosition(grid).X - origin.X, MinColumn, pair - MinColumn);
            var current = (double[])widths.Clone();
            current[column] = left;
            current[column + 1] = pair - left;
            SetStars(grid, current);
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (start is null)
            {
                return;
            }

            start = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            changed(grid.ColumnDefinitions.Select(d => d.Width.Value).ToArray());
        };
        return handle;
    }

    private static Border RowHandle(Grid grid, int row, int columns, Action<int, double?> changed)
    {
        var handle = new Border
        {
            Height = 6,
            Margin = new Thickness(0, 0, 0, -3),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            ZIndex = 9,
            Tag = "row-handle"
        };
        ToolTip.SetTip(handle, Loc.T("Author_DragToChangeTheRowHeight"));
        Grid.SetRow(handle, row);
        Grid.SetColumnSpan(handle, columns);

        Point? start = null;
        double height = 0;
        handle.PointerPressed += (_, e) =>
        {
            start = e.GetPosition(grid);
            height = grid.RowDefinitions[row].ActualHeight;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not { } origin)
            {
                return;
            }

            grid.RowDefinitions[row].MinHeight = Math.Max(MinRow, height + e.GetPosition(grid).Y - origin.Y);
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (start is null)
            {
                return;
            }

            start = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            changed(row, grid.RowDefinitions[row].MinHeight);
        };
        handle.DoubleTapped += (_, e) =>
        {
            grid.RowDefinitions[row].MinHeight = 0;
            changed(row, null);
            e.Handled = true;
        };
        return handle;
    }

    private static void SetStars(Grid grid, IReadOnlyList<double> widths)
    {
        for (var i = 0; i < widths.Count && i < grid.ColumnDefinitions.Count; i++)
        {
            grid.ColumnDefinitions[i].Width = new GridLength(widths[i], GridUnitType.Star);
        }
    }
}
