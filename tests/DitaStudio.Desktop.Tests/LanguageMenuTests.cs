using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Localization;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Меню «Язык»: выбор запоминается, подписи в разметке ({loc:Tr …}) меняются без перезапуска.</summary>
public sealed class LanguageMenuTests
{

    [AvaloniaFact]
    public void MenuListsSystemEnglishRussian_AndMarksUserChoice()
    {
        Loc.Instance.SetUserLanguage("ru");
        var window = new MainWindow();
        window.Show();

        var items = LanguageItems(window);

        Assert.Equal(new[] { "Как в системе", "English", "Русский" }, items.Select(i => i.Header?.ToString()));
        Assert.Equal(new[] { false, false, true }, items.Select(i => i.IsChecked));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingEnglish_SwitchesLabelsAtOnce_AndRemembersChoice()
    {
        Loc.Instance.SetUserLanguage("ru");
        var window = new MainWindow();
        window.Show();
        var menu = window.FindControl<MenuItem>("LanguageMenu")!;
        Assert.Equal("_Язык", menu.Header);

        LanguageItems(window)[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("en", Loc.Instance.Language);
        Assert.Equal("en", Loc.Instance.UserChoice);
        Assert.Equal("_Language", menu.Header); // привязка обновилась без перезапуска
        var items = LanguageItems(window);
        Assert.Equal("System default", items[0].Header);
        Assert.Equal(new[] { false, true, false }, items.Select(i => i.IsChecked));
        Assert.Equal("en", LanguageSettings.Load());
        Loc.Instance.SetUserLanguage("ru"); // остальные тесты идут на русском
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingSystemDefault_ForgetsUserChoice()
    {
        Loc.Instance.SetUserLanguage("en");
        var window = new MainWindow();
        window.Show();

        LanguageItems(window)[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Loc.Instance.UserChoice);
        Assert.Null(LanguageSettings.Load());
        Loc.Instance.SetUserLanguage("ru");
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaFact]
    public void EnglishUi_MenusToolbarAndPanels_HaveNoRussianText()
    {
        Loc.Instance.SetUserLanguage("en");
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = new List<string>();
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Concat(new Control[] { window }))
        {
            switch (control)
            {
                case MenuItem item:
                    texts.Add(item.Header?.ToString() ?? string.Empty);
                    foreach (var sub in item.Items.OfType<MenuItem>())
                    {
                        texts.Add(sub.Header?.ToString() ?? string.Empty);
                    }

                    break;
                case TabItem tab:
                    texts.Add(tab.Header?.ToString() ?? string.Empty);
                    break;
                case TextBlock block:
                    texts.Add(block.Text ?? string.Empty);
                    break;
                case ContentControl content when content.Content is string text:
                    texts.Add(text);
                    break;
            }

            if (ToolTip.GetTip(control) is string tip)
            {
                texts.Add(tip);
            }
        }

        var russian = texts.Where(t => Regex.IsMatch(t, "[А-Яа-яЁё]")).Distinct().ToList();
        Loc.Instance.SetUserLanguage("ru");
        Dispatcher.UIThread.RunJobs();
        window.Close();

        Assert.True(texts.Count > 100, $"собрано подписей: {texts.Count}");
        Assert.True(russian.Count == 0, "русский текст в английском интерфейсе: " + string.Join(" | ", russian.Take(10)));
    }

    private static List<MenuItem> LanguageItems(MainWindow window) =>
        window.FindControl<MenuItem>("LanguageMenu")!.ItemsSource!.Cast<MenuItem>().ToList();
}
