using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Schema.Dtd;

public sealed record DtdLoadResult(IReadOnlyList<ElementDef> Elements, IReadOnlyList<string> Warnings);

/// <summary>
/// Строит ElementDef из настоящего .dtd-файла (плюс модули, подключённые через SYSTEM/PUBLIC
/// параметрические сущности) — для проектов с кастомной специализацией DITA, которую нет смысла
/// (или возможности) вписывать в встроенный CatalogSource. Контент-модель разбирает уже
/// существующий ModelParser — синтаксис DTD и внутреннего DSL каталога для моделей совпадает.
/// DisplayKind (как элемент рисуется в режиме «Автор») в самом DTD не объявлен — выводится из
/// атрибута @class по конвенции специализации DITA (класс перечисляет всю цепочку предков вида
/// "module/name"), а если class не задан — из формы контент-модели.
/// </summary>
public static class DtdCatalogLoader
{
    public static DtdLoadResult Load(string dtdFilePath)
    {
        var schema = DtdReader.Read(dtdFilePath);
        var elements = new List<ElementDef>();

        foreach (var (name, modelText) in schema.ElementModels)
        {
            var attributes = schema.AttlistText.TryGetValue(name, out var attrText)
                ? DtdAttributeListParser.Parse(attrText)
                : new List<AttributeDef>();

            var classAttr = attributes.FirstOrDefault(a => a.Name == "class")?.DefaultValue ?? string.Empty;

            ContentModel model;
            try
            {
                model = ModelParser.Parse(modelText);
            }
            catch (Exception ex)
            {
                schema.Warnings.Add($"Элемент \"{name}\": не удалось разобрать контент-модель \"{modelText}\": {ex.Message}");
                continue;
            }

            var display = InferDisplay(classAttr, model);
            var attrDict = attributes.ToDictionary(a => a.Name, StringComparer.Ordinal);

            elements.Add(new ElementDef(name, classAttr, display, modelText, attrDict, "Из внешнего DTD")
            {
                Domain = "external"
            });
        }

        return new DtdLoadResult(elements, schema.Warnings);
    }

    /// <summary>Есть ли в @class токен "module/name" — конвенция специализации DITA: класс
    /// перечисляет всю цепочку предков через пробел, например "+ topic/ph ui-d/uicontrol ".</summary>
    private static bool ClassContains(string classAttr, string moduleSlashName) =>
        classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(moduleSlashName, StringComparer.Ordinal);

    private static DisplayKind InferDisplay(string classAttr, ContentModel model)
    {
        if (model is ContentModel.Empty)
        {
            return DisplayKind.Empty;
        }

        if (ClassContains(classAttr, "map/map"))
        {
            return DisplayKind.Map;
        }

        if (ClassContains(classAttr, "topic/topic"))
        {
            return DisplayKind.Topic;
        }

        if (ClassContains(classAttr, "topic/table") || ClassContains(classAttr, "topic/tgroup") ||
            ClassContains(classAttr, "topic/entry") || ClassContains(classAttr, "topic/row"))
        {
            return DisplayKind.Table;
        }

        if (ClassContains(classAttr, "topic/ph"))
        {
            return DisplayKind.Inline;
        }

        if (ClassContains(classAttr, "topic/pre") || ClassContains(classAttr, "topic/codeblock"))
        {
            return DisplayKind.Preformatted;
        }

        // @class не задан или не по конвенции DITA — приблизительно по форме модели: элемент с
        // прямым текстом (и без @class-подсказки на контейнер) считаем блочным, иначе контейнером.
        return model.AllowsText() ? DisplayKind.Block : DisplayKind.Container;
    }
}
