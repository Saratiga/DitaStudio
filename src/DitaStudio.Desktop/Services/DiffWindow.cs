using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Diff;
using DitaStudio.Core.IO;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Services;

/// <summary>Построчное сравнение двух файлов с построчным слиянием (→/← на группе различий) —
/// перенос WPF-окна DiffWindow. Работает с текстом файлов как есть, без разбора DOM.</summary>
internal static class DiffWindow
{
    private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x22, 0x22));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromRgb(0x1f, 0x4d, 0x24));

    public static Window Create(string leftPath, string rightPath, AvaloniaDialogService dialogs)
    {
        var window = new Window
        {
            Title = Loc.T("Dlg_Comparison01", Path.GetFileName(leftPath), Path.GetFileName(rightPath)),
            Width = 1100,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var outer = new DockPanel();

        var header = new Grid { Margin = new Thickness(10, 10, 10, 4), ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        var leftHeader = new TextBlock { Text = leftPath, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var arrowHeader = new TextBlock { Text = "  ↔  ", HorizontalAlignment = HorizontalAlignment.Center };
        var rightHeader = new TextBlock { Text = rightPath, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(arrowHeader, 1);
        Grid.SetColumn(rightHeader, 2);
        header.Children.Add(leftHeader);
        header.Children.Add(arrowHeader);
        header.Children.Add(rightHeader);
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var status = new TextBlock { Margin = new Thickness(10, 0, 10, 6), Foreground = Brushes.Gray };
        DockPanel.SetDock(status, Dock.Top);
        outer.Children.Add(status);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        outer.Children.Add(scroll);

        var mono = (Application.Current?.FindResource("MonoFont") as FontFamily) ?? FontFamily.Default;

        void Rebuild()
        {
            var leftText = File.ReadAllText(leftPath);
            var rightText = File.ReadAllText(rightPath);
            var diff = XmlDiff.Compare(leftText, rightText);
            var leftLines = XmlDiff.Normalize(leftText);
            var rightLines = XmlDiff.Normalize(rightText);

            var added = diff.Count(d => d.Kind == DiffKind.Added);
            var removed = diff.Count(d => d.Kind == DiffKind.Removed);
            status.Text = added == 0 && removed == 0 ? Loc.T("Dlg_TheFilesAreIdentical") : Loc.T("Dlg_LinesAdded0Removed1", added, removed);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            for (var r = 0; r < diff.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var leftIndex = 0;
            var rightIndex = 0;
            var row = 0;
            while (row < diff.Count)
            {
                var line = diff[row];
                if (line.Kind == DiffKind.Equal)
                {
                    AddCell(grid, row, 0, line.Left!, Brushes.Transparent, mono);
                    AddCell(grid, row, 2, line.Right!, Brushes.Transparent, mono);
                    leftIndex++;
                    rightIndex++;
                    row++;
                    continue;
                }

                var hunkStart = row;
                var leftStart = leftIndex;
                var rightStart = rightIndex;
                var leftCount = 0;
                var rightCount = 0;
                while (row < diff.Count && diff[row].Kind != DiffKind.Equal)
                {
                    var h = diff[row];
                    AddCell(grid, row, 0, h.Left ?? string.Empty, h.Kind == DiffKind.Removed ? RemovedBrush : Brushes.Transparent, mono);
                    AddCell(grid, row, 2, h.Right ?? string.Empty, h.Kind == DiffKind.Added ? AddedBrush : Brushes.Transparent, mono);
                    if (h.Left is not null) { leftIndex++; leftCount++; }
                    if (h.Right is not null) { rightIndex++; rightCount++; }
                    row++;
                }

                var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var toRight = new Button { Content = "→", Width = 26, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(0, 2) };
                var toLeft = new Button { Content = "←", Width = 26, Padding = new Thickness(0, 2) };
                ToolTip.SetTip(toRight, Loc.T("Dlg_CopyLeftToRight"));
                ToolTip.SetTip(toLeft, Loc.T("Dlg_CopyRightToLeft"));

                toRight.Click += async (_, _) =>
                {
                    var merged = rightLines.Take(rightStart).Concat(leftLines.Skip(leftStart).Take(leftCount)).Concat(rightLines.Skip(rightStart + rightCount));
                    await WriteMergedAsync(rightPath, rightText, merged);
                };
                toLeft.Click += async (_, _) =>
                {
                    var merged = leftLines.Take(leftStart).Concat(rightLines.Skip(rightStart).Take(rightCount)).Concat(leftLines.Skip(leftStart + leftCount));
                    await WriteMergedAsync(leftPath, leftText, merged);
                };

                arrows.Children.Add(toRight);
                arrows.Children.Add(toLeft);
                Grid.SetColumn(arrows, 1);
                Grid.SetRow(arrows, hunkStart);
                Grid.SetRowSpan(arrows, row - hunkStart);
                grid.Children.Add(arrows);
            }

            scroll.Content = grid;
        }

        // Сравнение идёт по строкам без \r, но файл записываем с его собственными переводами
        // строк и кодировкой (BOM) — иначе одно слияние превращало бы CRLF-файл в LF целиком.
        async Task WriteMergedAsync(string path, string originalText, IEnumerable<string> lines)
        {
            var newline = originalText.Contains("\r\n") ? "\r\n" : "\n";
            try
            {
                AtomicFile.WriteAllText(path, string.Join(newline, lines), EncodingOf(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await dialogs.ReportAsync(Loc.T("Dlg_Comparison"), Loc.T("Dlg_CouldNotWrite01", Path.GetFileName(path), ex.Message));
                return;
            }

            Rebuild();
        }

        Rebuild();
        window.Content = outer;
        return window;
    }

    private static System.Text.Encoding EncodingOf(string path)
    {
        using var reader = new StreamReader(path, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        reader.Peek();
        return reader.CurrentEncoding;
    }

    private static void AddCell(Grid grid, int row, int column, string text, IBrush background, FontFamily mono)
    {
        var block = new TextBlock
        {
            Text = text.Length == 0 ? " " : text,
            Background = background,
            FontFamily = mono,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(4, 1, 4, 1)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }
}
