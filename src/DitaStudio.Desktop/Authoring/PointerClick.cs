using Avalonia;
using Avalonia.Controls;

namespace DitaStudio.Desktop.Authoring;

/// <summary>Щелчок левой кнопкой без протаскивания — отличает «нажали» от «потянули» (маркер размера, выделение).</summary>
internal static class PointerClick
{
    public static void Attach(Control control, Action clicked)
    {
        Point? pressed = null;
        control.PointerPressed += (_, e) => pressed = e.GetCurrentPoint(control).Properties.IsLeftButtonPressed ? e.GetPosition(control) : null;
        control.PointerReleased += (_, e) =>
        {
            if (pressed is { } start)
            {
                var delta = e.GetPosition(control) - start;
                if (delta.X * delta.X + delta.Y * delta.Y < 16)
                {
                    clicked();
                }
            }

            pressed = null;
        };
    }
}
