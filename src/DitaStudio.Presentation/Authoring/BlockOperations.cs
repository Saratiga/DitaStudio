using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Presentation.Authoring;

/// <summary>
/// Структурные правки режима «Автор» по клавишам (Enter, Tab, Shift+Tab) — над моделью, без UI.
/// Уровни списков — перенос из WPF AuthorView.Editing; разделение блока, в отличие от
/// <see cref="EditCommands.SplitBlock"/>, сохраняет фразовые элементы по обе стороны курсора.
/// </summary>
public static class BlockOperations
{
    /// <summary>
    /// Делит блок в позиции <paramref name="offset"/> редактора блока (<see cref="InlineContent"/>,
    /// плашка — один символ). Хвост уходит в новый соседний элемент того же имени (атрибуты
    /// копируются, кроме id). Null — второй такой элемент здесь не допускается контент-моделью.
    /// </summary>
    public static DitaNode? SplitBlock(DitaNode block, int offset) => SplitBlock(InlineContent.FromNode(block), offset);

    /// <summary>
    /// То же для участка смешанного содержимого: хвост участка и вложенные блоки после него
    /// переходят в новый элемент (Enter в тексте пункта перед вложенным списком уносит список
    /// в новый пункт вместе с хвостом).
    /// </summary>
    public static DitaNode? SplitBlock(InlineContent segment, int offset)
    {
        var block = segment.Node;
        if (block.Parent is not { } parent ||
            !DitaCatalog.Default.CanInsert(parent, block.Name, EditCommands.ElementIndexOf(parent, block) + 1))
        {
            return null;
        }

        var created = DitaNode.Element(block.Name);
        foreach (var attr in block.Attributes)
        {
            if (attr.Name != "id")
            {
                created.SetAttribute(attr.Name, attr.Value);
            }
        }

        if (!segment.SplitInto(offset, created))
        {
            return null;
        }

        parent.Insert(parent.IndexOf(block) + 1, created);
        return created;
    }

    /// <summary>Пункт списка или шаг, к которому относится блок (сам блок или его родитель).</summary>
    public static DitaNode? ListItemFor(DitaNode node)
    {
        var item = node.Name is "li" or "step" or "substep" ? node : node.Parent;
        return item is { Name: "li" or "step" or "substep", Parent: not null } ? item : null;
    }

    /// <summary>Переносит пункт в substeps/ul/ol предыдущего пункта (Tab).</summary>
    public static bool IndentItem(DitaNode item)
    {
        var list = item.Parent;
        var previous = EditCommands.PreviousElement(item);
        if (list is null || previous is null)
        {
            return false;
        }

        var nestedName = list.Name switch
        {
            "steps" or "steps-unordered" or "substeps" => "substeps",
            "ol" => "ol",
            _ => "ul"
        };

        var itemName = nestedName == "substeps" ? "substep" : "li";
        var nested = previous.ElementChildren().FirstOrDefault(c => c.Name == nestedName);
        if (nested is null)
        {
            nested = EditCommands.Append(previous, nestedName);
            if (nested is null)
            {
                return false;
            }

            foreach (var auto in nested.Children.ToList())
            {
                nested.Remove(auto);
            }

            // Append ставит элемент по контент-модели — перед текстом пункта; вложенный
            // список должен идти после него.
            nested.RemoveSelf();
            previous.Add(nested);
        }

        item.RemoveSelf();
        nested.Add(item);
        if (item.Name != itemName)
        {
            EditCommands.ChangeElementName(item, itemName);
        }

        return true;
    }

    /// <summary>Переносит пункт из вложенного списка на уровень внешнего (Shift+Tab).</summary>
    public static bool OutdentItem(DitaNode item)
    {
        var list = item.Parent;
        var grandItem = list?.Parent;
        if (list is null || grandItem is null || grandItem.Name is not ("li" or "step" or "substep") || grandItem.Parent is not { } outerList)
        {
            return false;
        }

        var index = outerList.IndexOf(grandItem) + 1;
        item.RemoveSelf();

        var outerItemName = outerList.Name is "steps" or "steps-unordered" ? "step" : "li";
        outerList.Insert(index, item);
        if (item.Name != outerItemName)
        {
            EditCommands.ChangeElementName(item, outerItemName);
        }

        if (list.Children.Count == 0)
        {
            list.RemoveSelf();
        }

        return true;
    }
}
