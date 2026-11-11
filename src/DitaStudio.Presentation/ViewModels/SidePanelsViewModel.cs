using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>
/// Правая панель: атрибуты элемента под курсором, палитра допустимых для вставки элементов и
/// структура документа. Перестраивается при смене вкладки или положения курсора
/// (<see cref="Refresh"/>).
/// </summary>
public partial class SidePanelsViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly IDocumentHost _docs;
    private readonly InsertViewModel _insert;

    public SidePanelsViewModel(ShellContext context, IDocumentHost docs, InsertViewModel insert)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _docs = docs;
        _insert = insert;
        _platform = context.Ui.Platform;
    }

    private readonly IUiPlatform _platform;

    /// <summary>Имена продуктов проекта — для выпадающего списка значения <c>@product</c>.</summary>
    public IReadOnlyList<string> ProductNames => _workspace.Project?.Products.Select(p => p.Name).ToList() ?? new List<string>();

    /// <summary>Открыт ли проект: тогда <c>@product</c> выбирается из выпадающего списка продуктов, а не набирается.</summary>
    public bool HasProject => _workspace.Project is not null;

    // ------------------------------------------------------------ атрибуты

    [ObservableProperty]
    private string attributeContext = Loc.T("Msg_NoElementSelected");

    public ObservableCollection<AttributeRowViewModel> Attributes { get; } = new();

    /// <summary>Объявленные в каталоге, но ещё не заданные атрибуты — для «Добавить атрибут».</summary>
    public ObservableCollection<string> MissingAttributes { get; } = new();

    [ObservableProperty]
    private string? attributeToAdd;

    // ------------------------------------------------------------ палитра

    [ObservableProperty]
    private string paletteFilter = string.Empty;

    [ObservableProperty]
    private string paletteHint = Loc.T("Msg_PutTheCursorInTheText3");

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
        _shell.ContextText = _docs.Current?.Author.CurrentNode?.Path ?? string.Empty;
    }

    public void RefreshAttributes()
    {
        Attributes.Clear();
        MissingAttributes.Clear();
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node is null)
        {
            AttributeContext = Loc.T("Msg_NoElementSelected");
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
        // @class — технический класс специализации из каталога: вручную его не ставят (оформление — outputclass).
        foreach (var name in def.Attributes.Keys.Where(a => !shown.Contains(a) && a != "class").OrderBy(a => a, StringComparer.Ordinal))
        {
            MissingAttributes.Add(name);
        }
    }

    partial void OnAttributeToAddChanged(string? value)
    {
        if (value is null)
        {
            return;
        }

        // Выбор в списке «Добавить атрибут» приходит из обработчика SelectionChanged самого списка. Пересобрать
        // MissingAttributes прямо здесь нельзя: список ещё перебирает выбранные элементы, и Avalonia падает
        // с «Index was out of range». Поэтому атрибут добавляется следующим шагом очереди интерфейса.
        _platform.Post(() => AddAttribute(value));
    }

    /// <summary>Добавляет атрибут <paramref name="value"/> к элементу под курсором (значение по умолчанию из каталога).</summary>
    public void AddAttribute(string value)
    {
        AttributeToAdd = null;
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        if (value is null || pane is null || node is null || DitaCatalog.Default.Get(node.Name) is not { } def ||
            !def.Attributes.TryGetValue(value, out var attributeDef))
        {
            return;
        }

        pane.PushUndo(Loc.T("Msg_Attribute0", value));
        node.SetAttribute(value, attributeDef.DefaultValue ?? (attributeDef.Values.Count > 0 ? attributeDef.Values[0] : string.Empty));
        AfterAttributeEdit(pane, node);
        RefreshAttributes();
    }

    internal void AfterAttributeEdit(IDocumentView pane, DitaNode? node = null)
    {
        pane.Document.IsDirty = true;
        if (node is not null)
        {
            pane.Author.RefreshAttributeNotes(node);
        }

        _docs.RefreshAllTabTitles();
    }

    internal void SetStatus(string text) => _shell.StatusText = text;

    // ------------------------------------------------------------ палитра

    partial void OnPaletteFilterChanged(string value) => RefreshPalette();

    public void RefreshPalette()
    {
        Palette.Clear();
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            PaletteHint = Loc.T("Msg_PutTheCursorInTheText3");
            return;
        }

        var candidates = new List<(ElementDef Def, string Where)>();
        if (node.Parent is { } parent)
        {
            var index = EditCommands.ElementIndexOf(parent, node) + 1;
            candidates.AddRange(DitaCatalog.Default.InsertableAt(parent, index).Select(d => (d, Loc.T("Author_After02", node.Name))));
        }

        foreach (var def in DitaCatalog.Default.InsertableAt(node, DitaCatalog.ChildNames(node).Count))
        {
            if (candidates.All(c => c.Def.Name != def.Name))
            {
                candidates.Add((def, Loc.T("Author_Inside0", node.Name)));
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

        PaletteHint = Loc.T("Msg_AllowedNextTo01Elements", node.Name, candidates.Count);
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
            _insert.InsertElementCommand.Execute(entry.Name);
        }
    }

    // ------------------------------------------------------------ структура

    public void RefreshOutline()
    {
        Outline.Clear();
        if (_docs.Current?.Document is { } document)
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
            _docs.Current?.FocusNode(value.Node);
            _shell.StatusText = value.Node.Path;
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

        // @product — значения выбираются из выпадающего списка продуктов проекта (можно несколько), а не набираются руками.
        // Значения из документа, которых нет в списке, тоже показываются — их можно снять.
        if (name == "product" && owner.HasProject)
        {
            IsProductPicker = true;
            var tokens = Tokens(value);
            foreach (var product in owner.ProductNames.Concat(tokens).Distinct(StringComparer.Ordinal))
            {
                ProductChoices.Add(new ProductChoice(product, tokens.Contains(product, StringComparer.Ordinal), OnChoiceToggled));
            }

            RefilterProducts();
        }
    }

    /// <summary>Продукты проекта с флажками (пусто — не @product или список проекта пуст).</summary>
    public ObservableCollection<ProductChoice> ProductChoices { get; } = new();

    public bool HasProductChoices => ProductChoices.Count > 0;

    /// <summary>@product при открытом проекте: вместо поля ввода — выпадающий список продуктов с поиском.</summary>
    public bool IsProductPicker { get; }

    /// <summary>Обычное поле ввода: не перечисление и не @product.</summary>
    public bool IsPlainText => !IsEnumeration && !IsProductPicker;

    /// <summary>
    /// Текст, набранный в выпадающем списке продуктов, — только поиск по списку: значение атрибута им не задаётся,
    /// оно меняется флажками. Пусто — в поле видно текущее значение.
    /// </summary>
    [ObservableProperty]
    private string productSearch = string.Empty;

    /// <summary>Открыт ли выпадающий список продуктов.</summary>
    [ObservableProperty]
    private bool isProductDropDownOpen;

    /// <summary>Продукты, подходящие под <see cref="ProductSearch"/> (без учёта регистра, по вхождению).</summary>
    public ObservableCollection<ProductChoice> FilteredProductChoices { get; } = new();

    /// <summary>Подсказка выпадающего списка: список пуст или поиск ничего не нашёл.</summary>
    public string ProductListHint => ProductChoices.Count == 0
        ? Loc.T("Attr_ProductListEmpty")
        : FilteredProductChoices.Count == 0 ? Loc.T("Attr_ProductNotFound", ProductSearch.Trim()) : string.Empty;

    public bool HasProductListHint => ProductListHint.Length > 0;

    /// <summary>Текущее значение в поле, пока ничего не набрано для поиска.</summary>
    public bool ShowsProductValue => ProductSearch.Length == 0;

    /// <summary>Что видно в поле: выбранные продукты или «не выбрано».</summary>
    public string ProductDisplay => string.IsNullOrWhiteSpace(Value) ? Loc.T("Attr_ProductNotSelected") : Value;

    public bool HasProductValue => !string.IsNullOrWhiteSpace(Value);

    partial void OnProductSearchChanged(string value)
    {
        RefilterProducts();
        OnPropertyChanged(nameof(ShowsProductValue));
        if (value.Length > 0)
        {
            IsProductDropDownOpen = true;
        }
    }

    partial void OnIsProductDropDownOpenChanged(bool value)
    {
        if (!value && ProductSearch.Length > 0)
        {
            ProductSearch = string.Empty; // список закрыт — поиск сброшен, в поле снова значение
        }
    }

    private void RefilterProducts()
    {
        var filter = ProductSearch.Trim();
        FilteredProductChoices.Clear();
        foreach (var choice in ProductChoices.Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredProductChoices.Add(choice);
        }

        OnPropertyChanged(nameof(ProductListHint));
        OnPropertyChanged(nameof(HasProductListHint));
    }

    /// <summary>Enter в поле поиска: найден один продукт (или имя совпало точно) — флажок переключается.</summary>
    [RelayCommand]
    private void ToggleFoundProduct()
    {
        var filter = ProductSearch.Trim();
        var match = FilteredProductChoices.FirstOrDefault(c => string.Equals(c.Name, filter, StringComparison.OrdinalIgnoreCase))
                    ?? (FilteredProductChoices.Count == 1 ? FilteredProductChoices[0] : null);
        if (match is not null)
        {
            match.IsSelected = !match.IsSelected;
            ProductSearch = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenProductDropDown() => IsProductDropDownOpen = true;

    [RelayCommand]
    private void CloseProductDropDown() => IsProductDropDownOpen = false;

    private bool _syncingChoices;

    private static string[] Tokens(string? text) => (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Флажок продукта: значение атрибута — выбранные продукты через пробел (значения вне списка сохраняются).</summary>
    private void OnChoiceToggled()
    {
        if (_syncingChoices)
        {
            return;
        }

        var inList = ProductChoices.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var selected = ProductChoices.Where(c => c.IsSelected).Select(c => c.Name);
        Value = string.Join(' ', Tokens(Value).Where(t => !inList.Contains(t)).Concat(selected));
        Apply();
    }

    partial void OnValueChanged(string value)
    {
        // Значение изменено в текстовом поле — флажки следуют за ним.
        _syncingChoices = true;
        var tokens = Tokens(value);
        foreach (var choice in ProductChoices)
        {
            choice.IsSelected = tokens.Contains(choice.Name, StringComparer.Ordinal);
        }

        _syncingChoices = false;
        OnPropertyChanged(nameof(ProductDisplay));
        OnPropertyChanged(nameof(HasProductValue));
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

        _pane.PushUndo(Loc.T("Msg_Change0", Name));
        _node.SetAttribute(Name, Value);
        _owner.AfterAttributeEdit(_pane, _node);
        _owner.SetStatus($"@{Name} = {Value}");
    }

    [RelayCommand]
    private void Remove()
    {
        _pane.PushUndo(Loc.T("Msg_Delete02", Name));
        _node.RemoveAttribute(Name);
        _owner.AfterAttributeEdit(_pane, _node);
        _owner.RefreshAttributes();
    }
}

/// <summary>Продукт проекта с флажком в панели атрибутов: отметка добавляет или убирает его из значения <c>@product</c>.</summary>
public sealed partial class ProductChoice : ObservableObject
{
    private readonly Action _changed;

    public ProductChoice(string name, bool selected, Action changed)
    {
        Name = name;
        isSelected = selected;
        _changed = changed;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => _changed();
}
