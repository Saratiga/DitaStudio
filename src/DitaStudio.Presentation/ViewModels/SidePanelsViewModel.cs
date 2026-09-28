using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>
/// Правая панель: атрибуты элемента под курсором, палитра допустимых для вставки элементов и
/// структура документа. Перестраивается при смене вкладки или положения курсора
/// (<see cref="Refresh"/>). Использует Avalonia-оболочка; WPF-оболочка пока строит панель сама
/// (MainWindow.SidePanels.cs).
/// </summary>
public partial class SidePanelsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SidePanelsViewModel(MainViewModel main)
    {
        _main = main;
    }

    // ------------------------------------------------------------ атрибуты

    [ObservableProperty]
    private string attributeContext = "Элемент не выбран";

    public ObservableCollection<AttributeRowViewModel> Attributes { get; } = new();

    /// <summary>Объявленные в каталоге, но ещё не заданные атрибуты — для «Добавить атрибут».</summary>
    public ObservableCollection<string> MissingAttributes { get; } = new();

    [ObservableProperty]
    private string? attributeToAdd;

    // ------------------------------------------------------------ палитра

    [ObservableProperty]
    private string paletteFilter = string.Empty;

    [ObservableProperty]
    private string paletteHint = "Поставьте курсор в текст, чтобы увидеть допустимые элементы.";

    public ObservableCollection<PaletteEntry> Palette { get; } = new();

    [ObservableProperty]
    private PaletteEntry? selectedPaletteEntry;

    // ------------------------------------------------------------ структура

    public ObservableCollection<OutlineNode> Outline { get; } = new();

    [ObservableProperty]
    private OutlineNode? selectedOutlineNode;

    /// <summary>Курсор сдвинулся или сменилась вкладка — атрибуты, палитра и путь в строке состояния.</summary>
    public void Refresh()
    {
        RefreshAttributes();
        RefreshPalette();
        _main.ContextText = _main.Current?.Author.CurrentNode?.Path ?? string.Empty;
    }

    public void RefreshAttributes()
    {
        Attributes.Clear();
        MissingAttributes.Clear();
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node is null)
        {
            AttributeContext = "Элемент не выбран";
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        AttributeContext = $"<{node.Name}>  {def?.Description ?? string.Empty}";

        foreach (var attribute in node.Attributes.ToList())
        {
            Attributes.Add(new AttributeRowViewModel(this, pane, node, attribute.Name, attribute.Value,
                def?.Attributes.GetValueOrDefault(attribute.Name)));
        }

        if (def is null)
        {
            return;
        }

        var shown = node.Attributes.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in def.Attributes.Keys.Where(a => !shown.Contains(a)).OrderBy(a => a, StringComparer.Ordinal))
        {
            MissingAttributes.Add(name);
        }
    }

    partial void OnAttributeToAddChanged(string? value)
    {
        var pane = _main.Current;
        var node = pane?.Author.CurrentNode;
        if (value is null || pane is null || node is null || DitaCatalog.Default.Get(node.Name) is not { } def ||
            !def.Attributes.TryGetValue(value, out var attributeDef))
        {
            return;
        }

        pane.PushUndo($"Атрибут @{value}");
        node.SetAttribute(value, attributeDef.DefaultValue ?? (attributeDef.Values.Count > 0 ? attributeDef.Values[0] : string.Empty));
        AfterAttributeEdit(pane);
        RefreshAttributes();
    }

    internal void AfterAttributeEdit(IDocumentView pane)
    {
        pane.Document.IsDirty = true;
        _main.Documents.RefreshAllTabTitles();
    }

    internal void SetStatus(string text) => _main.StatusText = text;

    // ------------------------------------------------------------ палитра

    partial void OnPaletteFilterChanged(string value) => RefreshPalette();

    public void RefreshPalette()
    {
        Palette.Clear();
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            PaletteHint = "Поставьте курсор в текст, чтобы увидеть допустимые элементы.";
            return;
        }

        var candidates = new List<(ElementDef Def, string Where)>();
        if (node.Parent is { } parent)
        {
            var index = EditCommands.ElementIndexOf(parent, node) + 1;
            candidates.AddRange(DitaCatalog.Default.InsertableAt(parent, index).Select(d => (d, $"после <{node.Name}>")));
        }

        foreach (var def in DitaCatalog.Default.InsertableAt(node, DitaCatalog.ChildNames(node).Count))
        {
            if (candidates.All(c => c.Def.Name != def.Name))
            {
                candidates.Add((def, $"внутрь <{node.Name}>"));
            }
        }

        var filter = PaletteFilter.Trim();
        if (filter.Length > 0)
        {
            candidates = candidates
                .Where(c => c.Def.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            c.Def.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        PaletteHint = $"Допустимо рядом с <{node.Name}>: {candidates.Count} элементов. Двойной щелчок — вставить.";
        foreach (var (def, where) in candidates.OrderBy(c => c.Def.Name, StringComparer.Ordinal))
        {
            Palette.Add(new PaletteEntry(def.Name,
                string.IsNullOrWhiteSpace(def.Description) ? where : $"{def.Description} · {where}"));
        }
    }

    [RelayCommand]
    private void InsertSelectedPaletteEntry()
    {
        if (SelectedPaletteEntry is { } entry)
        {
            _main.Insert.InsertElementCommand.Execute(entry.Name);
        }
    }

    // ------------------------------------------------------------ структура

    public void RefreshOutline()
    {
        Outline.Clear();
        if (_main.Current?.Document is { } document)
        {
            Outline.Add(BuildOutline(document.Root, 0));
        }
    }

    private static OutlineNode BuildOutline(DitaNode node, int depth)
    {
        var item = new OutlineNode(node, depth);
        foreach (var child in node.ElementChildren())
        {
            item.Children.Add(BuildOutline(child, depth + 1));
        }

        return item;
    }

    partial void OnSelectedOutlineNodeChanged(OutlineNode? value)
    {
        if (value is not null)
        {
            _main.Current?.FocusNode(value.Node);
            _main.StatusText = value.Node.Path;
        }
    }
}

/// <summary>Элемент палитры вставки.</summary>
public sealed record PaletteEntry(string Name, string Description);

/// <summary>Одна строка панели «Атрибуты»: значение правится на месте, изменение идёт в модель
/// с точкой отмены (как в WPF-версии).</summary>
public sealed partial class AttributeRowViewModel : ObservableObject
{
    private readonly SidePanelsViewModel _owner;
    private readonly IDocumentView _pane;
    private readonly DitaNode _node;

    public AttributeRowViewModel(SidePanelsViewModel owner, IDocumentView pane, DitaNode node, string name, string value, AttributeDef? def)
    {
        _owner = owner;
        _pane = pane;
        _node = node;
        Name = name;
        this.value = value;
        Values = def is { Type: AttrType.Enumeration } ? def.Values : Array.Empty<string>();
        Description = def?.Description ?? string.Empty;
    }

    public string Name { get; }

    public string Label => "@" + Name;

    /// <summary>Допустимые значения перечисления — поле тогда выпадающий список с вводом.</summary>
    public IReadOnlyList<string> Values { get; }

    public bool IsEnumeration => Values.Count > 0;

    public string Description { get; }

    public bool HasDescription => Description.Length > 0;

    [ObservableProperty]
    private string value;

    /// <summary>Записывает значение в модель (по Enter, уходу фокуса или выбору из списка).</summary>
    [RelayCommand]
    private void Apply()
    {
        if (_node.GetAttribute(Name) == Value)
        {
            return;
        }

        _pane.PushUndo($"Изменение @{Name}");
        _node.SetAttribute(Name, Value);
        _owner.AfterAttributeEdit(_pane);
        _owner.SetStatus($"@{Name} = {Value}");
    }

    [RelayCommand]
    private void Remove()
    {
        _pane.PushUndo($"Удаление @{Name}");
        _node.RemoveAttribute(Name);
        _owner.AfterAttributeEdit(_pane);
        _owner.RefreshAttributes();
    }
}
