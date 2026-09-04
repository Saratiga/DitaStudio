namespace DitaStudio.Core.Schema;

public enum AttrType
{
    CData,
    Enumeration,
    Id,
    IdRef,
    NmToken
}

public sealed class AttributeDef
{
    public AttributeDef(string name, AttrType type, IReadOnlyList<string> values, bool required, string? defaultValue, string description)
    {
        Name = name;
        Type = type;
        Values = values;
        Required = required;
        DefaultValue = defaultValue;
        Description = description;
    }

    public string Name { get; }

    public AttrType Type { get; }

    public IReadOnlyList<string> Values { get; }

    public bool Required { get; }

    public string? DefaultValue { get; }

    public string Description { get; }

    public override string ToString() => Name;
}

/// <summary>Как элемент показывается в режиме «Автор».</summary>
public enum DisplayKind
{
    /// <summary>Абзацный блок с редактируемым текстом.</summary>
    Block,

    /// <summary>Фразовый элемент внутри строки текста.</summary>
    Inline,

    /// <summary>Контейнер блоков (body, section, li, note...).</summary>
    Container,

    /// <summary>Корень топика.</summary>
    Topic,

    /// <summary>Корень или узел карты.</summary>
    Map,

    /// <summary>Таблица и её части.</summary>
    Table,

    /// <summary>Метаданные — по умолчанию скрыты в режиме «Автор».</summary>
    Meta,

    /// <summary>Пустой элемент (image, xref без текста и т.п.).</summary>
    Empty,

    /// <summary>Блок с моноширинным дословным содержимым.</summary>
    Preformatted
}

public sealed class ElementDef
{
    private ContentModel? _model;
    private ModelAutomaton? _automaton;

    public ElementDef(
        string name,
        string classAttr,
        DisplayKind display,
        string modelText,
        IReadOnlyDictionary<string, AttributeDef> attributes,
        string description)
    {
        Name = name;
        ClassAttr = classAttr;
        Display = display;
        ModelText = modelText;
        Attributes = attributes;
        Description = description;
    }

    public string Name { get; }

    /// <summary>Значение атрибута @class, например "- topic/p ".</summary>
    public string ClassAttr { get; }

    public DisplayKind Display { get; }

    public string ModelText { get; }

    public IReadOnlyDictionary<string, AttributeDef> Attributes { get; }

    public string Description { get; }

    public ContentModel Model => _model ??= ModelParser.Parse(ModelText);

    public ModelAutomaton Automaton => _automaton ??= new ModelAutomaton(Model);

    /// <summary>Элемент допускает текст непосредственно внутри себя.</summary>
    public bool IsMixed => Model.AllowsText();

    public bool IsEmpty => Model is ContentModel.Empty;

    public bool IsInline => Display == DisplayKind.Inline;

    /// <summary>Корневой элемент топика (topic, concept, task, reference...).</summary>
    public bool IsTopicType => Display == DisplayKind.Topic;

    /// <summary>Корень карты: первая пара «модуль/элемент» в @class — map/map.</summary>
    public bool IsMapType => ClassAttr.Contains("map/map", StringComparison.Ordinal);

    /// <summary>Модуль/домен, к которому относится элемент — для группировки в палитре.</summary>
    public string Domain { get; init; } = "topic";

    public override string ToString() => Name;
}
