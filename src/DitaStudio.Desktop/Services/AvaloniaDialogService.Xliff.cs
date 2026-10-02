using Avalonia;
using Avalonia.Controls;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Services;

public sealed partial class AvaloniaDialogService
{
    private static readonly string[] CommonLanguages = { "ru", "en", "de", "fr", "es", "it", "uk", "pl", "pt", "zh", "ja" };

    public async Task<(string Source, string Target)?> PickXliffLanguagesAsync(string source, string target)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var sourceBox = LanguageBox(source);
        var targetBox = LanguageBox(target);
        panel.Children.Add(Label(Loc.T("Dlg_SourceLanguage")));
        panel.Children.Add(sourceBox);
        panel.Children.Add(Label(Loc.T("Dlg_TargetLanguage")));
        panel.Children.Add(targetBox);
        panel.Children.Add(Muted(new TextBlock { Text = Loc.T("Dlg_XliffLanguagesHint"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }));

        (string Source, string Target)? result = null;
        var window = Shell(Loc.T("Dlg_XliffLanguages"), panel, 440, 300);
        panel.Children.Add(Buttons(window, () =>
        {
            var from = (sourceBox.Text ?? string.Empty).Trim();
            var to = (targetBox.Text ?? string.Empty).Trim();
            if (from.Length > 0 && to.Length > 0)
            {
                result = (from, to);
            }
        }));
        return await ShowAsync(window) ? result : null;
    }

    private static ComboBox LanguageBox(string value) => new()
    {
        IsEditable = true,
        ItemsSource = CommonLanguages,
        Text = value,
        Width = 200,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        Padding = new Thickness(4, 3, 4, 3),
        Margin = new Thickness(0, 0, 0, 8)
    };
}
