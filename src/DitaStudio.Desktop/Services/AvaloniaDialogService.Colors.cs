using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DitaStudio.Desktop.Services;

// Окно выбора цвета (маркер «Другой цвет…»).
public sealed partial class AvaloniaDialogService
{
    // Палитра: светлые, насыщенные и тёмные оттенки двенадцати тонов и ряд серых — по двенадцать в строке.
    private static readonly string[] ColorPalette =
    {
        "FFC7CE", "FFD9B3", "FFF2B3", "E2F0B3", "C6EFCE", "B3F2E0", "B3F0F2", "B3D9FF", "C9C9FF", "E0B3FF", "F2B3F2", "FFB3D9",
        "FF0000", "FF8000", "FFFF00", "80FF00", "00FF00", "00FF80", "00FFFF", "0080FF", "0000FF", "8000FF", "FF00FF", "FF0080",
        "800000", "804000", "808000", "408000", "008000", "008040", "008080", "004080", "000080", "400080", "800080", "800040",
        "FFFFFF", "E6E6E6", "CCCCCC", "B3B3B3", "999999", "808080", "666666", "4D4D4D", "333333", "1A1A1A", "000000", "F5DEB3"
    };

    /// <summary>«#RRGGBB» из текста вида «ff8800», «#FF8800»; null — не цвет.</summary>
    private static string? NormalizeHex(string? text)
    {
        var digits = (text ?? string.Empty).Trim().TrimStart('#');
        return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? "#" + digits.ToUpperInvariant() : null;
    }

    public async Task<string?> PickColorAsync(string title, string? initialHex)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped("Выберите цвет в палитре или введите код в виде #RRGGBB."));

        var hexBox = Input(NormalizeHex(initialHex) ?? "#FFFF00");
        hexBox.Watermark = "#RRGGBB";
        hexBox.Width = 120;
        hexBox.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(hexBox, "Код цвета");
        var preview = new Border { Width = 56, Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(preview, "Образец цвета");
        preview.Bind(Border.BorderBrushProperty, preview.GetResourceObservable("Line"));
        var status = Muted(new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        void Update()
        {
            var hex = NormalizeHex(hexBox.Text);
            preview.Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex));
            status.Text = hex is null ? "Нужен код из шести цифр" : string.Empty;
        }

        hexBox.TextChanged += (_, _) => Update();

        var grid = new WrapPanel { Width = 12 * 30, Margin = new Thickness(0, 10, 0, 10) };
        foreach (var hex in ColorPalette)
        {
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse("#" + hex)),
                BorderThickness = new Thickness(1)
            };
            swatch.Bind(Button.BorderBrushProperty, swatch.GetResourceObservable("Line"));
            ToolTip.SetTip(swatch, "#" + hex);
            AutomationProperties.SetName(swatch, "Цвет #" + hex);
            swatch.Click += (_, _) => hexBox.Text = "#" + hex;
            grid.Children.Add(swatch);
        }

        panel.Children.Add(grid);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { hexBox, preview, status } });
        Update();

        string? result = null;
        var window = Shell(title, panel, 420, 320);
        panel.Children.Add(Buttons(window, () => result = NormalizeHex(hexBox.Text)));
        return await ShowAsync(window) ? result : null;
    }
}
