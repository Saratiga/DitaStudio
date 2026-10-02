using Avalonia;
using Avalonia.Data;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Localization;

/// <summary>Строка интерфейса для элемента, собранного в коде: свойство следует за языком (то же, что <c>{loc:Tr}</c> в XAML).</summary>
public static class TrBinding
{
    public static T Tr<T>(this T target, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        target.Bind(property, new Binding(nameof(LocEntry.Value), BindingMode.OneWay) { Source = Loc.Instance.Entry(key) });
        return target;
    }
}
