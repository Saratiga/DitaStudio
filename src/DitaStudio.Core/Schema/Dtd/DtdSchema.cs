namespace DitaStudio.Core.Schema.Dtd;

/// <summary>Накопитель деклараций, извлечённых из .dtd/.ent/.mod файлов — параметрические
/// сущности, сырые (уже с раскрытыми %entity;) тексты контент-моделей и списков атрибутов
/// по имени элемента. ATTLIST для одного элемента может встречаться несколько раз (обычная
/// практика модульных DTD) — тексты просто конкатенируются.</summary>
public sealed class DtdSchema
{
    public Dictionary<string, string> Entities { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ElementModels { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> AttlistText { get; } = new(StringComparer.Ordinal);

    public List<string> Warnings { get; } = new();
}
