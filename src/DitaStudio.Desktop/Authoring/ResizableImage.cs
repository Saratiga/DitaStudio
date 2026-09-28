using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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
        ToolTip.SetTip(_thumb, "Потяните, чтобы изменить размер");

        Children.Add(_image);
        Children.Add(_thumb);
        HorizontalAlignment = HorizontalAlignment.Left;
        Background = Brushes.Transparent; // наведение ловится по всей площади, а не только по пикселям картинки
        UpdateTip();

        PointerEntered += (_, _) => _thumb.IsVisible = true;
        PointerExited += (_, _) => _thumb.IsVisible = _dragStart is not null;
        _thumb.PointerPressed += OnThumbPressed;
        _thumb.PointerMoved += OnThumbMoved;
        _thumb.PointerReleased += OnThumbReleased;
    }

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
        ToolTip.SetTip(_image, $"{Math.Round(_image.Width)} × {Math.Round(_image.Width * _aspect)} px — потяните за угол, чтобы изменить");

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
        _image.Width = Math.Clamp(_startWidth + Math.Max(delta.X, delta.Y / Math.Max(_aspect, 0.01)), Smallest, 4000);
        UpdateTip();
        e.Handled = true;
    }

    private void OnThumbReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        _dragStart = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (Math.Abs(_image.Width - _startWidth) >= 1)
        {
            _resized(Math.Round(_image.Width));
        }
    }
}
