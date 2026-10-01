using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

/// <summary>
/// Диалоги Avalonia-оболочки — перенос WPF <c>Dialogs</c> один в один: те же поля, подписи,
/// размеры и порядок элементов, собранные в коде без отдельных .axaml. Отличие одно — окна
/// открываются асинхронно (<see cref="Window.ShowDialog{TResult}(Window)"/>).
/// Разделы — в соседних partial-файлах: документы и ссылки, публикация.
/// </summary>
public sealed partial class AvaloniaDialogService : IDialogService
{
    private readonly Func<Window?> _owner;
    private readonly IFilePicker _files;

    public AvaloniaDialogService(Func<Window?> owner, IFilePicker files)
    {
        _owner = owner;
        _files = files;
    }

    // ------------------------------------------------------------ каркас окна

    /// <summary>
    /// Окно диалога. По умолчанию (<paramref name="autoHeight"/>) высота подбирается под содержимое: окно ровно такой высоты, чтобы
    /// были видны все поля и кнопки, но не выше рабочей области экрана (дальше — полоса прокрутки). <paramref name="width"/> — ширина;
    /// <paramref name="height"/> — высота только для окон с растягиваемым содержимым (<paramref name="autoHeight"/> = false: список или
    /// вкладки на всё окно — у них собственной высоты нет).
    /// </summary>
    private static Window Shell(string title, Control content, double width = 460, double height = 320, bool autoHeight = true)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            ShowInTaskbar = false
        };

        if (!autoHeight)
        {
            window.Height = height;
            window.Content = content;
            return window;
        }

        window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = 900;
        window.Content = content as ScrollViewer ?? new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        window.Opened += (_, _) =>
        {
            // Выше рабочей области экрана окно не бывает: лишнее прокручивается.
            if (window.Screens.ScreenFromWindow(window) is { } screen)
            {
                window.MaxHeight = Math.Max(240, screen.WorkingArea.Height / window.RenderScaling * 0.92);
            }
        };
        return window;
    }

    /// <summary>Показывает окно модально над главным; true — нажата основная кнопка.</summary>
    private async Task<bool> ShowAsync(Window window)
    {
        var owner = _owner();
        if (owner is null)
        {
            return false;
        }

        return await window.ShowDialog<bool?>(owner) == true;
    }

    /// <summary>Кнопки «ОК»/«Отмена» справа внизу; <paramref name="onOk"/> собирает результат.</summary>
    private static StackPanel Buttons(Window window, Action onOk, string okText = "ОК")
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var ok = new Button { Content = okText, Padding = new Thickness(18, 5, 18, 5), IsDefault = true };
        ok.Click += (_, _) =>
        {
            onOk();
            window.Close(true);
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(18, 5, 18, 5),
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) => window.Close(false);

        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    /// <summary>
    /// Двойной щелчок по строке списка = выбрать её и нажать «ОК» (кнопка по умолчанию из <paramref name="buttons"/>): не нужно
    /// сначала выделять строку, а потом тянуться к кнопке. Щелчок мимо строки (полоса прокрутки, пустое место) ничего не делает.
    /// </summary>
    private static void AcceptOnDoubleTap(ListBox list, Panel buttons)
    {
        list.DoubleTapped += (_, e) =>
        {
            if (list.SelectedItem is null || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            {
                return;
            }

            buttons.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        };
    }

    private static Button CloseButton(Window window)
    {
        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(18, 5, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => window.Close(false);
        return close;
    }

    private static TextBlock Label(string text) => Muted(new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 3),
        FontSize = 12
    });

    /// <summary>Приглушённый цвет текста из темы — следует за переключением темы.</summary>
    private static TextBlock Muted(TextBlock block) => WithBrush(block, "TextMuted");

    private static TextBlock WithBrush(TextBlock block, string key)
    {
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(key));
        return block;
    }

    private static TextBlock Wrapped(string text, double bottom = 8) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    private static TextBox Input(string text = "") => new() { Text = text, Padding = new Thickness(4, 3, 4, 3) };

    private static FontFamily Mono => (Application.Current?.FindResource("MonoFont") as FontFamily) ?? FontFamily.Default;

    // ------------------------------------------------------------ сообщения

    public async Task MessageAsync(string title, string message) =>
        await AskCoreAsync(title, message, new[] { ("ОК", AskResult.Yes) }, isWarning: false);

    public async Task<bool> ConfirmAsync(string title, string message) =>
        await AskAsync(title, message, AskButtons.YesNo) == AskResult.Yes;

    public Task<AskResult> AskAsync(string title, string message, AskButtons buttons, AskIcon icon = AskIcon.Question)
    {
        var choices = buttons == AskButtons.YesNo
            ? new[] { ("Да", AskResult.Yes), ("Нет", AskResult.No) }
            : new[] { ("Да", AskResult.Yes), ("Нет", AskResult.No), ("Отмена", AskResult.Cancel) };
        return AskCoreAsync(title, message, choices, icon == AskIcon.Warning);
    }

    /// <summary>Окно сообщения вместо системного MessageBox (в Avalonia его нет): текст с
    /// прокруткой и кнопки ответа. Закрытие крестиком — «Отмена» (или «Нет», если отмены нет).</summary>
    private async Task<AskResult> AskCoreAsync(string title, string message, (string Text, AskResult Result)[] choices, bool isWarning)
    {
        var owner = _owner();
        if (owner is null)
        {
            return AskResult.Cancel;
        }

        var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        var icon = new TextBlock
        {
            Text = isWarning ? "⚠" : "ℹ",
            FontSize = 22,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        WithBrush(icon, isWarning ? "Warning" : "Accent");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var body = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(icon, Dock.Left);
        body.Children.Add(buttons);
        body.Children.Add(icon);
        body.Children.Add(new ScrollViewer { Content = text, MaxHeight = 460 });

        var window = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = body
        };

        var fallback = choices.Any(c => c.Result == AskResult.Cancel) ? AskResult.Cancel
            : choices.Length == 1 ? choices[0].Result
            : AskResult.No;

        foreach (var (caption, result) in choices)
        {
            var button = new Button
            {
                Content = caption,
                Padding = new Thickness(18, 5, 18, 5),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = result == choices[0].Result,
                IsCancel = result == fallback
            };
            button.Click += (_, _) => window.Close(result);
            buttons.Children.Add(button);
        }

        var answer = await window.ShowDialog<AskResult?>(owner);
        return answer ?? fallback;
    }

    // ------------------------------------------------------------ выбор из списка

    public async Task<T?> PickOneAsync<T>(string title, string prompt, IReadOnlyList<T> items, Func<T, string> display) where T : class
    {
        if (items.Count == 0)
        {
            await MessageAsync(title, "Нет подключённых плагинов этого вида — положите .dll в папку plugins рядом с приложением.");
            return null;
        }

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped(prompt));
        var list = new ListBox { Height = 160, ItemsSource = items.Select(display).ToList(), SelectedIndex = 0 };
        panel.Children.Add(list);

        T? result = null;
        var window = Shell(title, panel, 420, 320);
        var buttons = Buttons(window, () => result = list.SelectedIndex >= 0 ? items[list.SelectedIndex] : null);
        panel.Children.Add(buttons);
        AcceptOnDoubleTap(list, buttons);
        return await ShowAsync(window) ? result : null;
    }

    // ------------------------------------------------------------ о программе

    public async Task AboutAsync()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "DITA Studio", FontSize = 20, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Редактор технической документации на DITA 1.3.\n" +
                   "Режимы «Автор», «Исходный код» и «Предпросмотр», карты публикации, " +
                   "проверка по контент-моделям, сборка HTML и PDF.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(Muted(new TextBlock
        {
            Text = $"Элементов в словаре: {DitaCatalog.Default.Elements.Count}",
            Margin = new Thickness(0, 14, 0, 0)
        }));

        var window = Shell("О программе", panel, 460, 280);
        panel.Children.Add(CloseButton(window));
        await ShowAsync(window);
    }

    // ------------------------------------------------------------ сравнение

    public async Task ShowDiffAsync(string leftPath, string rightPath)
    {
        var window = DiffWindow.Create(leftPath, rightPath, this);
        if (_owner() is { } owner)
        {
            window.Show(owner);
        }

        await Task.CompletedTask;
    }

    /// <summary>Для окна сравнения — сообщение об ошибке записи при слиянии.</summary>
    internal Task ReportAsync(string title, string message) => MessageAsync(title, message);

    // Кнопка-переключатель: доступность зависимых полей следует за флажком.
    private static void Bind(ToggleButton toggle, params Control[] dependents)
    {
        void Update()
        {
            foreach (var dependent in dependents)
            {
                dependent.IsEnabled = toggle.IsChecked == true;
            }
        }

        toggle.IsCheckedChanged += (_, _) => Update();
        Update();
    }
}
