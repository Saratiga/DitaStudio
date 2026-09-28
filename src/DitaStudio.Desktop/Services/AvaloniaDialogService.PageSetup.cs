using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Desktop.Services;

// Диалог «Параметры страницы»: размер бумаги, ориентация и поля — для DOCX и PDF, со схемой листа.
public sealed partial class AvaloniaDialogService
{
    private const string PaperFromCss = "Как в CSS проекта (обычно A4)";

    public async Task<DocxLayout?> PageSetupAsync(DitaProject project)
    {
        var current = project.DocxLayout.Clone();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Muted(Wrapped(
            "Размер бумаги, ориентация и поля действуют на экспорт в DOCX и PDF. Пустое поле — значение " +
            "из CSS проекта (если оно там не задано — 20 мм).")));

        var paper = new ComboBox
        {
            ItemsSource = new[] { PaperFromCss }.Concat(DocxLayout.PaperSizesMm.Keys).ToList(),
            SelectedItem = current.PaperSize.Length > 0 ? current.PaperSize : PaperFromCss,
            MinWidth = 220
        };
        AutomationProperties.SetName(paper, "Размер бумаги");
        panel.Children.Add(Label("Размер бумаги"));
        panel.Children.Add(paper);

        var portrait = new RadioButton { Content = "Книжная", GroupName = "orientation", IsChecked = !current.Landscape };
        var landscape = new RadioButton { Content = "Альбомная", GroupName = "orientation", IsChecked = current.Landscape, Margin = new Thickness(16, 0, 0, 0) };
        panel.Children.Add(Label("Ориентация"));
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { portrait, landscape } });

        TextBox Margin(double? value, string name)
        {
            var box = new TextBox
            {
                Text = value?.ToString("0.#", CultureInfo.CurrentCulture) ?? string.Empty,
                Watermark = "по CSS",
                Width = 80,
                Padding = new Thickness(4, 3, 4, 3)
            };
            AutomationProperties.SetName(box, name);
            return box;
        }

        var top = Margin(current.MarginTopMm, "Поле сверху, мм");
        var bottom = Margin(current.MarginBottomMm, "Поле снизу, мм");
        var left = Margin(current.MarginLeftMm, "Поле слева, мм");
        var right = Margin(current.MarginRightMm, "Поле справа, мм");
        var margins = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,16,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,6,Auto")
        };

        void Place(string caption, Control box, int row, int column)
        {
            var label = new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, column + 1);
            margins.Children.Add(label);
            margins.Children.Add(box);
        }

        Place("Сверху", top, 0, 0);
        Place("Снизу", bottom, 2, 0);
        Place("Слева", left, 0, 3);
        Place("Справа", right, 2, 3);
        panel.Children.Add(Label("Поля, мм"));
        panel.Children.Add(margins);

        // Схема листа: пропорции бумаги и область текста внутри полей — видно, что выбрано.
        var textArea = new Rectangle { StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 } };
        textArea.Bind(Shape.StrokeProperty, textArea.GetResourceObservable("Accent"));
        var sheet = new Border { BorderThickness = new Thickness(1), Child = textArea, HorizontalAlignment = HorizontalAlignment.Center };
        sheet.Bind(Border.BackgroundProperty, sheet.GetResourceObservable("Surface"));
        sheet.Bind(Border.BorderBrushProperty, sheet.GetResourceObservable("TextMuted"));
        var caption = Muted(new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 });
        panel.Children.Add(new StackPanel { Margin = new Thickness(0, 16, 0, 0), Children = { new Border { Height = 170, Child = sheet }, caption } });

        double? Parse(TextBox box) =>
            double.TryParse((box.Text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : null;

        void Update()
        {
            var name = paper.SelectedItem as string ?? PaperFromCss;
            var (width, height) = DocxLayout.PaperSizesMm.TryGetValue(name, out var size) ? size : DocxLayout.PaperSizesMm["A4"];
            if (landscape.IsChecked == true)
            {
                (width, height) = (height, width);
            }

            var scale = 160 / Math.Max(width, height);
            sheet.Width = width * scale;
            sheet.Height = height * scale;
            sheet.VerticalAlignment = VerticalAlignment.Center;
            double M(TextBox box) => Math.Clamp(Parse(box) ?? 20, 0, 100) * scale;
            textArea.Margin = new Thickness(M(left), M(top), M(right), M(bottom));
            caption.Text = $"{(name == PaperFromCss ? "A4" : name)}, {(landscape.IsChecked == true ? "альбомная" : "книжная")}: " +
                           $"{width:0.#} × {height:0.#} мм";
        }

        paper.SelectionChanged += (_, _) => Update();
        portrait.IsCheckedChanged += (_, _) => Update();
        landscape.IsCheckedChanged += (_, _) => Update();
        foreach (var box in new[] { top, bottom, left, right })
        {
            box.TextChanged += (_, _) => Update();
        }

        Update();

        DocxLayout? result = null;
        var window = Shell("Параметры страницы", new ScrollViewer { Content = panel }, 460, 540);
        panel.Children.Add(Buttons(window, () =>
        {
            result = current.Clone();
            result.PaperSize = paper.SelectedItem as string is { } selected && selected != PaperFromCss ? selected : string.Empty;
            result.Landscape = landscape.IsChecked == true;
            result.MarginTopMm = Parse(top);
            result.MarginBottomMm = Parse(bottom);
            result.MarginLeftMm = Parse(left);
            result.MarginRightMm = Parse(right);
            result.Normalize();
        }));

        return await ShowAsync(window) ? result : null;
    }
}
