using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using DitaStudio.Core.Localization;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// Всплывающая правка плашки (изображение, пустая ссылка): поля атрибутов под плашкой. Enter или щелчок мимо — записать,
/// Esc — отменить. Что именно записывать, решает <see cref="ChipEdit"/>; здесь только окно.
/// </summary>
public sealed class ChipEditPopup : Popup
{
    private readonly Dictionary<string, Control> _editors = new();
    private readonly Action<IReadOnlyDictionary<string, string>> _apply;
    private bool _finished;

    /// <summary>Ширина окна: длинный путь к файлу не растягивает его, а сокращается многоточием.</summary>
    public const double PopupWidth = 300;

    /// <summary>Сколько знаков пути видно в поле; длиннее — многоточие в начале, полный путь в подсказке.</summary>
    public const int ShownPathLength = 24;

    /// <param name="browse">Выбор файла для поля адреса (<c>href</c>) — путь от документа; null — кнопки «Обзор…» нет.</param>
    public ChipEditPopup(Control target, string title, IReadOnlyList<ChipField> fields, Action<IReadOnlyDictionary<string, string>> apply,
        Func<Task<string?>>? browse = null)
    {
        _apply = apply;
        PlacementTarget = target;
        Placement = PlacementMode.Bottom;
        IsLightDismissEnabled = true;

        var panel = new StackPanel { Spacing = 4, Width = PopupWidth };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock { Text = field.Label, FontSize = 11, Opacity = 0.75 });
            Control editor;
            if (field.Choices is { } choices)
            {
                var combo = new ComboBox { ItemsSource = choices, SelectedItem = field.Value, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                editor = combo;
            }
            else
            {
                editor = new TextBox { Text = field.Value, Padding = new Thickness(4, 3, 4, 3) };
            }

            _editors[field.Key] = editor;
            panel.Children.Add(field.Key == ChipEdit.Href && editor is TextBox pathBox ? PathRow(pathBox, browse) : editor);
        }

        var border = new Border { Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = panel };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SurfaceAlt"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Line"));
        border.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Child = border;

        Opened += (_, _) => (_editors.Values.OfType<TextBox>().FirstOrDefault(b => b.IsVisible) as Control)?.Focus();
        // Щелчок мимо закрывает окно — введённое записывается, как при уходе фокуса из поля.
        Closed += (_, _) => Commit();
    }

    /// <summary>Путь, сокращённый для показа: не длиннее <see cref="ShownPathLength"/> знаков, лишнее начало заменено многоточием.</summary>
    public static string ShortPath(string path) =>
        path.Length <= ShownPathLength ? path : "…" + path[^(ShownPathLength - 1)..];

    /// <summary>
    /// Адрес файла или ссылки: сокращённая строка с полным путём в подсказке (окно не растягивается); щелчок по ней открывает поле правки,
    /// «Обзор…» выбирает файл.
    /// </summary>
    private Control PathRow(TextBox box, Func<Task<string?>>? browse)
    {
        var shown = new TextBlock
        {
            Text = ShortPath(box.Text ?? string.Empty),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            Cursor = new Cursor(StandardCursorType.Ibeam),
            Tag = "path-short"
        };
        ToolTip.SetTip(shown, box.Text);
        var shownHost = new Border { Child = shown, Padding = new Thickness(4, 3, 4, 3), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(1) };
        shownHost.Bind(Border.BorderBrushProperty, shownHost.GetResourceObservable("Line"));
        box.IsVisible = false;
        box.TextChanged += (_, _) =>
        {
            shown.Text = ShortPath(box.Text ?? string.Empty);
            ToolTip.SetTip(shown, box.Text);
        };

        void StartEditing()
        {
            shownHost.IsVisible = false;
            box.IsVisible = true;
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
        }

        shownHost.PointerPressed += (_, e) =>
        {
            StartEditing();
            e.Handled = true;
        };

        var row = new DockPanel();
        if (browse is not null)
        {
            var button = new Button { Content = Loc.T("Dlg_Browse"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 0, 0, 0) };
            button.Click += async (_, _) =>
            {
                if (await browse() is { } chosen)
                {
                    box.Text = chosen;
                }
            };
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }

        row.Children.Add(new Panel { Children = { shownHost, box } });
        return row;
    }

    /// <summary>Редакторы полей по ключам <see cref="ChipField.Key"/>.</summary>
    public IReadOnlyDictionary<string, Control> Editors => _editors;

    /// <summary>Задаёт значение поля (для клавиатуры и тестов).</summary>
    public void SetValue(string key, string value)
    {
        switch (_editors[key])
        {
            case TextBox box:
                box.Text = value;
                break;
            case ComboBox combo:
                combo.SelectedItem = value;
                break;
        }
    }

    /// <summary>Записывает введённое и закрывает окно; повторный вызов ничего не делает.</summary>
    public void Commit()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        IsOpen = false;
        _apply(_editors.ToDictionary(e => e.Key, e => e.Value switch
        {
            TextBox box => box.Text ?? string.Empty,
            ComboBox combo => combo.SelectedItem as string ?? string.Empty,
            _ => string.Empty
        }));
    }

    /// <summary>Закрывает окно, ничего не записывая.</summary>
    public void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        IsOpen = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Окно закрыто по Esc (запись не выполнена).</summary>
    public event EventHandler? Cancelled;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
    }
}
