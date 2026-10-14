using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// Картинка «Автора» с маркером размера в правом нижнем углу: маркер виден при наведении,
/// перетаскивание меняет ширину (высота — по пропорциям), отпускание сообщает новую ширину —
/// её пишут в стандартный @width (px). Так размер меняется мышью, без правки атрибутов.
/// </summary>
public sealed class ResizableImage : Panel
{
    private const double Smallest = 16;
    private readonly Image _image;
    private readonly Border _thumb;
    private readonly Action<double> _resized;
    private readonly double _aspect;
    private Point? _dragStart;
    private double _startWidth;

    public ResizableImage(Bitmap bitmap, double? width, double maxWidth, double maxHeight, Action<double> resized)
    {
        _resized = resized;
        var natural = bitmap.Size;
        _aspect = natural.Width > 0 ? natural.Height / natural.Width : 1;
        var shown = width ?? Math.Min(natural.Width, Math.Min(maxWidth, _aspect > 0 ? maxHeight / _aspect : maxWidth));
        _image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Width = Math.Max(Smallest, shown) };

        _thumb = new Border
        {
            Width = 10,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            IsVisible = false
        };
        _thumb.Bind(Border.BorderBrushProperty, _thumb.GetResourceObservable("Accent"));
        ToolTip.SetTip(_thumb, Loc.T("Author_DragToResize"));

        Children.Add(_image);
        Children.Add(_thumb);
        HorizontalAlignment = HorizontalAlignment.Left;
        Background = Brushes.Transparent; // наведение ловится по всей площади, а не только по пикселям картинки
        UpdateTip();

        PointerEntered += (_, _) => _thumb.IsVisible = true;
        PointerExited += (_, _) => _thumb.IsVisible = _dragStart is not null;
        _thumb.PointerPressed += OnThumbPressed;
        _thumb.PointerMoved += OnThumbMoved;
        // Отпускание кнопки редактор текста вокруг (AvaloniaEdit) помечает обработанным раньше маркера — без handledEventsToo ширина не записалась бы.
        _thumb.AddHandler(PointerReleasedEvent, OnThumbReleased, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _thumb.AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_dragStart is not null)
            {
                FinishDrag(); // захват потерян посреди протаскивания — то, что уже видно, записывается
            }
        });
    }

    /// <summary>Сколько места у картинки в её блоке (ячейке, абзаце, примечании): шире протащить нельзя.</summary>
    private double AvailableWidth()
    {
        // Ближайший блок «Автора» — редактор текста или рамка блока; без них — прежний предел.
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor is BlockEditor || ancestor is Border { Tag: DitaStudio.Core.Model.DitaNode })
            {
                return ancestor.Bounds.Width > 0 ? ancestor.Bounds.Width - 8 : 4000;
            }
        }

        return 4000;
    }

    /// <summary>Источник события — маркер размера (а не сама картинка).</summary>
    public bool IsThumb(object? source) => ReferenceEquals(source, _thumb) || (source is Visual visual && visual.GetVisualAncestors().Contains(_thumb));

    /// <summary>Показанная ширина, px.</summary>
    public double ShownWidth => _image.Width;

    /// <summary>Меняет ширину и сообщает о ней — то же, что перетаскивание маркера.</summary>
    public void ResizeTo(double width)
    {
        _image.Width = Math.Clamp(width, Smallest, 4000);
        UpdateTip();
        _resized(Math.Round(_image.Width));
    }

    /// <summary>Ширина из @width/@height DITA в px (px, pt, pc, in, cm, mm, em; без единиц — px).</summary>
    public static double? WidthFromAttributes(string? width, string? height, Bitmap bitmap)
    {
        if (ToPixels(width) is { } w)
        {
            return w;
        }

        var size = bitmap.Size;
        return ToPixels(height) is { } h && size.Height > 0 ? h * size.Width / size.Height : null;
    }

    private static double? ToPixels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        var unitStart = text.TakeWhile(c => char.IsDigit(c) || c is '.' or ',').Count();
        if (!double.TryParse(text[..unitStart].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return null;
        }

        return text[unitStart..].Trim() switch
        {
            "" or "px" => number,
            "pt" => number * 96 / 72,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54,
            "mm" => number * 96 / 25.4,
            "em" => number * 16,
            _ => null
        };
    }

    private void UpdateTip() =>
        ToolTip.SetTip(_image, Loc.T("Author_01PxDragTheCorner", Math.Round(_image.Width), Math.Round(_image.Width * _aspect)));

    private void OnThumbPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _startWidth = _image.Width;
        e.Pointer.Capture(_thumb);
        e.Handled = true;
    }

    private void OnThumbMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        _image.Width = Math.Clamp(_startWidth + Math.Max(delta.X, delta.Y / Math.Max(_aspect, 0.01)), Smallest, Math.Max(Smallest, AvailableWidth()));
        UpdateTip();
        e.Handled = true;
    }

    private void OnThumbReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        e.Handled = true;
        FinishDrag();
        e.Pointer.Capture(null);
    }

    private void FinishDrag()
    {
        _dragStart = null;
        _thumb.IsVisible = IsPointerOver;
        if (Math.Abs(_image.Width - _startWidth) >= 1)
        {
            // После того, как событие мыши дойдёт до конца: запись ширины перерисовывает строку текста, в которой стоит сама картинка,
            // а перерисовка посреди обработки отпускания кнопки ломает раскладку редактора (AvaloniaEdit).
            var width = Math.Round(_image.Width);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _resized(width));
        }
    }
}
