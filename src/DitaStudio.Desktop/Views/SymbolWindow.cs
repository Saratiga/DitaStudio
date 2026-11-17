using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop;

/// <summary>
/// Окно «Другие символы» (как «Символ» в Word): знаки по группам, название выбранного знака и его код; поле «Код знака» вставляет
/// любой символ Юникода по шестнадцатеричному коду. Двойной щелчок по знаку или «Вставить» — вставить и закрыть.
/// </summary>
public sealed class SymbolWindow : Window
{
    private readonly TextBlock _info = new() { FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _code = new() { Width = 90 };
    private string? _selected;

    public SymbolWindow()
    {
        Title = Loc.T("Sym_Title");
        Width = 560;
        Height = 470;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Surface"));

        var tabs = new TabControl();
        foreach (var group in Symbols.Groups)
        {
            var grid = new WrapPanel();
            foreach (var symbol in group.Symbols)
            {
                var button = new Button
                {
                    Content = symbol.Text == " " ? "⍽" : symbol.Text,
                    Width = 38,
                    Height = 36,
                    Margin = new Thickness(1),
                    Padding = new Thickness(0),
                    FontSize = 18,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = symbol
                };
                ToolTip.SetTip(button, symbol.Name);
                button.Click += (_, _) => Select(symbol);
                button.DoubleTapped += (_, _) =>
                {
                    Select(symbol);
                    Close(_selected);
                };
                grid.Children.Add(button);
            }

            tabs.Items.Add(new TabItem
            {
                Header = group.Name,
                Content = new ScrollViewer { Content = grid, Padding = new Thickness(4) }
            });
        }

        _code.TextChanged += (_, _) =>
        {
            if (_code.IsFocused && Symbols.FromCode(_code.Text) is { } fromCode)
            {
                _selected = fromCode;
                _info.Text = $"{fromCode}  U+{char.ConvertToUtf32(fromCode, 0):X4}";
            }
        };

        var insert = new Button { Content = Loc.T("Sym_Insert"), IsDefault = true, Padding = new Thickness(14, 4) };
        insert.Click += (_, _) => Close(_selected);
        var cancel = new Button { Content = Loc.T("Dlg_Cancel"), IsCancel = true, Padding = new Thickness(14, 4), Margin = new Thickness(6, 0, 0, 0) };
        cancel.Click += (_, _) => Close(null);

        var codeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new TextBlock { Text = Loc.T("Sym_Code"), VerticalAlignment = VerticalAlignment.Center }, _code }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { insert, cancel } };
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(codeRow);

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_info, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_info);
        root.Children.Add(tabs);
        Content = root;
    }

    private void Select(SymbolEntry symbol)
    {
        _selected = symbol.Text;
        var code = char.ConvertToUtf32(symbol.Text, 0);
        _info.Text = $"{(symbol.Text == " " ? "⍽" : symbol.Text)}  {symbol.Name}  ·  U+{code:X4}";
        _code.Text = code.ToString("X4");
    }

    /// <summary>Показывает окно; выбранный символ или null (окно закрыто без выбора).</summary>
    public static Task<string?> PickAsync(Window owner) => new SymbolWindow().ShowDialog<string?>(owner);
}
