using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Localization;

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
            reason = Loc.T("Core_TheMapRootCannotBeMoved");
            return false;
        }

        if (ReferenceEquals(node, target))
        {
            reason = Loc.T("Core_ARowCannotBeMovedOnto");
            return false;
        }

        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, node))
            {
                reason = Loc.T("Core_ARowCannotBeMovedInto");
                return false;
            }
        }

        if (!ReferenceEquals(RootOf(node), RootOf(target)))
        {
            reason = Loc.T("Core_RowsFromDifferentMapFilesAre");
            return false;
        }

        return CanPlace(node.Name, target, position, node, out reason);
    }

    /// <summary>
    /// Можно ли поставить элемент <paramref name="elementName"/> в указанное место относительно
    /// <paramref name="target"/> — по контент-модели каталога. <paramref name="moving"/> — переносимая
    /// строка: она из последовательности убирается (для вставки нового элемента — null).
    /// </summary>
    public static bool CanPlace(string elementName, DitaNode target, DropPosition position, DitaNode? moving, out string reason)
    {
        reason = string.Empty;
        var newParent = position == DropPosition.Child ? target : target.Parent;
        if (newParent is null)
        {
            reason = Loc.T("Core_ARowCannotBePlacedBefore");
            return false;
        }

        var names = newParent.ElementChildren().Where(child => !ReferenceEquals(child, moving)).Select(child => child.Name).ToList();
        var index = position switch
        {
            DropPosition.Child => names.Count,
            DropPosition.Before => IndexAmong(newParent, target, moving),
            _ => IndexAmong(newParent, target, moving) + 1
        };
        names.Insert(Math.Clamp(index, 0, names.Count), elementName);

        var definition = DitaCatalog.Default.Get(newParent.Name);
        if (definition is not null && !definition.Automaton.Validate(names, out _, out _))
        {
            reason = Loc.T("Core_0CannotBePlacedIn1", elementName, newParent.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Пробное выполнение операции над строкой карты на копии всей карты: операция считается
    /// допустимой, если она выполнилась и оба затронутых родителя (прежний и новый) по-прежнему
    /// соответствуют контент-модели. Настоящая карта не меняется — отказ не оставляет следов в
    /// истории отмены. Так «Вложить», «Переместить», «Дублировать» и «Вырезать» не могут дать
    /// недопустимую карту (вторая <c>booktitle</c>, глава в главе, строка выше <c>title</c>).
    /// </summary>
    public static bool Try(DitaNode node, Func<DitaNode, bool> operation, out string reason)
    {
        reason = string.Empty;
        var path = new List<int>();
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            path.Insert(0, current.Parent.IndexOf(current));
        }

        var root = RootOf(node).CloneDeep();
        var copy = root;
        foreach (var index in path)
        {
            copy = copy.Children[index];
        }

        var oldParent = copy.Parent;
        if (!operation(copy))
        {
            reason = Loc.T("Core_TheOperationIsNotAvailableHere");
            return false;
        }

        foreach (var container in new[] { oldParent, copy.Parent }.Where(c => c is not null).Distinct())
        {
            if (!ChildrenValid(container!, out var message))
            {
                reason = message;
                return false;
            }
        }

        return true;
    }

    private static bool ChildrenValid(DitaNode container, out string reason)
    {
        reason = string.Empty;
        var definition = DitaCatalog.Default.Get(container.Name);
        if (definition is null)
        {
            return true;
        }

        var names = container.ElementChildren().Select(child => child.Name).ToList();
        if (definition.Automaton.Validate(names, out var errorIndex, out var expected))
        {
            return true;
        }

        reason = errorIndex < names.Count
            ? Loc.T("Core_NotAllowed0CannotStandHere", names[errorIndex], container.Name)
            : Loc.T("Core_NotAllowed0LacksARequired", container.Name, string.Join(", ", expected.Take(3)));
        return false;
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
    private static int IndexAmong(DitaNode parent, DitaNode target, DitaNode? moved) =>
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
