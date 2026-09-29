using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Editing;

/// <summary>Куда переносится строка карты относительно строки-цели.</summary>
public enum DropPosition
{
    /// <summary>Перед целью, на её уровне.</summary>
    Before,

    /// <summary>После цели, на её уровне.</summary>
    After,

    /// <summary>Внутрь цели последней дочерней строкой.</summary>
    Child
}

/// <summary>
/// Перенос строк карты (topicref, topichead, chapter…) — перетаскиванием в дереве карты: на другой
/// уровень и в другого родителя. Перенос выполняется, только если результат допустим по контент-модели
/// каталога (в <c>chapter</c> нельзя положить <c>keydef</c> перед обязательными частями и т. п.),
/// строка не оказывается внутри самой себя и обе строки лежат в одном файле карты.
/// </summary>
public static class MapMoves
{
    /// <summary>Можно ли перенести <paramref name="node"/> в указанное место; иначе — причина по-русски.</summary>
    public static bool CanMove(DitaNode node, DitaNode target, DropPosition position, out string reason)
    {
        reason = string.Empty;
        if (node.Parent is null || node.Name is "map" or "bookmap")
        {
            reason = "Корень карты перенести нельзя.";
            return false;
        }

        if (ReferenceEquals(node, target))
        {
            reason = "Строку нельзя перенести на саму себя.";
            return false;
        }

        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, node))
            {
                reason = "Строку нельзя перенести в её собственную ветку.";
                return false;
            }
        }

        if (!ReferenceEquals(RootOf(node), RootOf(target)))
        {
            reason = "Строки из разных файлов карты переносятся через «Вырезать» и «Вставить».";
            return false;
        }

        var newParent = position == DropPosition.Child ? target : target.Parent;
        if (newParent is null)
        {
            reason = "Перед корнем карты и после него строку поставить нельзя.";
            return false;
        }

        var names = newParent.ElementChildren().Where(child => !ReferenceEquals(child, node)).Select(child => child.Name).ToList();
        var index = position switch
        {
            DropPosition.Child => names.Count,
            DropPosition.Before => IndexAmong(newParent, target, node),
            _ => IndexAmong(newParent, target, node) + 1
        };
        names.Insert(Math.Clamp(index, 0, names.Count), node.Name);

        var definition = DitaCatalog.Default.Get(newParent.Name);
        if (definition is not null && !definition.Automaton.Validate(names, out _, out _))
        {
            reason = $"«{node.Name}» нельзя поместить в «{newParent.Name}» на это место.";
            return false;
        }

        return true;
    }

    /// <summary>Переносит строку. false — перенос недопустим (см. <see cref="CanMove"/>), карта не изменена.</summary>
    public static bool Move(DitaNode node, DitaNode target, DropPosition position)
    {
        if (!CanMove(node, target, position, out _))
        {
            return false;
        }

        node.RemoveSelf();
        if (position == DropPosition.Child)
        {
            target.Add(node);
        }
        else
        {
            target.Parent!.Insert(target.Parent.IndexOf(target) + (position == DropPosition.Before ? 0 : 1), node);
        }

        return true;
    }

    // Номер цели среди элементов родителя без переносимой строки.
    private static int IndexAmong(DitaNode parent, DitaNode target, DitaNode moved) =>
        parent.ElementChildren().Where(child => !ReferenceEquals(child, moved)).ToList().FindIndex(child => ReferenceEquals(child, target));

    private static DitaNode RootOf(DitaNode node)
    {
        while (node.Parent is not null)
        {
            node = node.Parent;
        }

        return node;
    }
}
