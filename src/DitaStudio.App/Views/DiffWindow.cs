using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DitaStudio.Core.Diff;

namespace DitaStudio.App.Views;

/// <summary>Построчное сравнение двух файлов с посточным слиянием (→/← на группе различий).
/// Работает с текстом файлов как есть, без разбора DOM — как обычный текстовый diff.</summary>
public static class DiffWindow
{
    public static void Show(string leftPath, string rightPath)
    {
        var window = new Window
        {
            Title = $"Сравнение: {System.IO.Path.GetFileName(leftPath)} ↔ {System.IO.Path.GetFileName(rightPath)}",
            Width = 1100,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        ThemeManager.ApplyTitleBar(window);

        var outer = new DockPanel();

        var header = new Grid { Margin = new Thickness(10, 10, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var leftHeader = new TextBlock { Text = leftPath, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var rightHeader = new TextBlock { Text = rightPath, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(rightHeader, 2);
        var arrowHeader = new TextBlock { Text = "  ↔  ", HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(arrowHeader, 1);
        header.Children.Add(leftHeader);
        header.Children.Add(arrowHeader);
        header.Children.Add(rightHeader);
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);

        var status = new TextBlock { Margin = new Thickness(10, 0, 10, 6), Foreground = System.Windows.Media.Brushes.Gray };
        DockPanel.SetDock(status, Dock.Top);
        outer.Children.Add(status);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        outer.Children.Add(scroll);

        var equalBrush = Brushes.Transparent;
        var removedBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x22, 0x22));
        var addedBrush = new SolidColorBrush(Color.FromRgb(0x1f, 0x4d, 0x24));

        void Rebuild()
        {
            var leftText = File.ReadAllText(leftPath);
            var rightText = File.ReadAllText(rightPath);
            var diff = XmlDiff.Compare(leftText, rightText);
            var leftLines = XmlDiff.Normalize(leftText);
            var rightLines = XmlDiff.Normalize(rightText);

            var added = diff.Count(d => d.Kind == DiffKind.Added);
            var removed = diff.Count(d => d.Kind == DiffKind.Removed);
            status.Text = added == 0 && removed == 0
                ? "Файлы идентичны."
                : $"Добавлено строк: {added}, удалено: {removed}.";

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < diff.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var leftIndex = 0;
            var rightIndex = 0;
            var row = 0;
            while (row < diff.Count)
            {
                var line = diff[row];
                if (line.Kind == DiffKind.Equal)
                {
                    AddCell(grid, row, 0, line.Left!, equalBrush);
                    AddCell(grid, row, 2, line.Right!, equalBrush);
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
                    AddCell(grid, row, 0, h.Left ?? string.Empty, h.Kind == DiffKind.Removed ? removedBrush : equalBrush);
                    AddCell(grid, row, 2, h.Right ?? string.Empty, h.Kind == DiffKind.Added ? addedBrush : equalBrush);
                    if (h.Left is not null) { leftIndex++; leftCount++; }
                    if (h.Right is not null) { rightIndex++; rightCount++; }
                    row++;
                }

                var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var toRight = new Button { Content = "→", Width = 26, Margin = new Thickness(0, 0, 0, 2), ToolTip = "Скопировать слева направо" };
                var toLeft = new Button { Content = "←", Width = 26, ToolTip = "Скопировать справа налево" };

                toRight.Click += (_, _) =>
                {
                    var replacement = leftLines.Skip(leftStart).Take(leftCount);
                    var merged = rightLines.Take(rightStart).Concat(replacement).Concat(rightLines.Skip(rightStart + rightCount));
                    File.WriteAllText(rightPath, string.Join("\n", merged));
                    Rebuild();
                };
                toLeft.Click += (_, _) =>
                {
                    var replacement = rightLines.Skip(rightStart).Take(rightCount);
                    var merged = leftLines.Take(leftStart).Concat(replacement).Concat(leftLines.Skip(leftStart + leftCount));
                    File.WriteAllText(leftPath, string.Join("\n", merged));
                    Rebuild();
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

        Rebuild();
        window.Content = outer;
        window.Show();
    }

    private static void AddCell(Grid grid, int row, int column, string text, Brush background)
    {
        var block = new TextBlock
        {
            Text = text.Length == 0 ? " " : text,
            Background = background,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(4, 1, 4, 1)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }
}
