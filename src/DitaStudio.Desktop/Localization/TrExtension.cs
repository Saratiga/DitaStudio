using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Localization;

/// <summary>
/// Строка интерфейса в XAML: <c>Header="{loc:Tr Menu_File}"</c>. Привязка к <see cref="LocEntry"/> этого ключа, поэтому при смене языка
/// подпись обновляется без перезапуска.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocEntry.Value), BindingMode.OneWay) { Source = Loc.Instance.Entry(Key) };
}
