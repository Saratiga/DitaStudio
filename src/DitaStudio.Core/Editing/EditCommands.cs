using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Editing;

/// <summary>
/// Операции редактирования структуры документа. Каждая проверяет контент-модель
/// каталога, поэтому редактор не позволяет получить невалидный DITA.
/// </summary>
public static class EditCommands
{
    private static DitaCatalog Catalog => DitaCatalog.Default;

    /// <summary>Вставляет новый элемент внутрь родителя после указанного узла.</summary>
    public static DitaNode? InsertAfter(DitaNode reference, string elementName)
    {
        var parent = reference.Parent;
        if (parent is null)
        {
            return null;
        }

        var elementIndex = ElementIndexOf(parent, reference) + 1;
        return InsertInto(parent, elementName, elementIndex);
    }

    public static DitaNode? InsertBefore(DitaNode reference, string elementName)
    {
        var parent = reference.Parent;
        if (parent is null)
        {
            return null;
        }

        var elementIndex = ElementIndexOf(parent, reference);
        return InsertInto(parent, elementName, elementIndex);
    }

    /// <summary>Вставляет элемент в конец родителя.</summary>
    public static DitaNode? Append(DitaNode parent, string elementName) =>
        InsertInto(parent, elementName, DitaCatalog.ChildNames(parent).Count);

    public static DitaNode? InsertInto(DitaNode parent, string elementName, int elementIndex)
    {
        if (!Catalog.CanInsert(parent, elementName, elementIndex))
        {
            return null;
        }

        var node = Catalog.CreateElement(elementName);
        var childIndex = ChildIndexForElementIndex(parent, elementIndex);
        parent.Insert(childIndex, node);
        return node;
    }

    /// <summary>Оборачивает диапазон детей в новый элемент (например, абзацы в note).</summary>
    public static DitaNode? Wrap(DitaNode parent, int firstChildIndex, int lastChildIndex, string elementName)
    {
        if (firstChildIndex < 0 || lastChildIndex >= parent.Children.Count || firstChildIndex > lastChildIndex)
        {
            return null;
        }

        var wrapper = Catalog.CreateElement(elementName);
        foreach (var child in wrapper.Children.ToList())
        {
            wrapper.Remove(child);
        }

        var moved = new List<DitaNode>();
        for (var i = firstChildIndex; i <= lastChildIndex; i++)
        {
            moved.Add(parent.Children[i]);
        }

        var insertAt = firstChildIndex;
        foreach (var node in moved)
        {
            parent.Remove(node);
            wrapper.Add(node);
        }

        parent.Insert(insertAt, wrapper);
        return wrapper;
    }

    /// <summary>Снимает элемент, перенося его детей на место самого элемента.</summary>
    public static bool Unwrap(DitaNode node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        var index = parent.IndexOf(node);
        var children = node.Children.ToList();
        parent.Remove(node);
        for (var i = 0; i < children.Count; i++)
        {
            parent.Insert(index + i, children[i]);
        }

        return true;
    }

    public static bool Delete(DitaNode node)
    {
        if (node.Parent is null)
        {
            return false;
        }

        node.RemoveSelf();
        return true;
    }

    public static bool MoveUp(DitaNode node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        var previous = PreviousElement(node);
        if (previous is null)
        {
            return false;
        }

        var targetIndex = parent.IndexOf(previous);
        parent.Remove(node);
        parent.Insert(targetIndex, node);
        return true;
    }

    public static bool MoveDown(DitaNode node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        var next = NextElement(node);
        if (next is null)
        {
            return false;
        }

        var targetIndex = parent.IndexOf(next);
        parent.Remove(node);
        parent.Insert(targetIndex, node);
        return true;
    }

    /// <summary>Меняет имя элемента, сохраняя содержимое и допустимые атрибуты.</summary>
    public static bool ChangeElementName(DitaNode node, string newName)
    {
        var def = Catalog.Get(newName);
        if (def is null || node.Parent is null)
        {
            return false;
        }

        node.Name = newName;

        foreach (var attr in node.Attributes.ToList())
        {
            if (!def.Attributes.ContainsKey(attr.Name))
            {
                node.RemoveAttribute(attr.Name);
            }
        }

        return true;
    }

    /// <summary>Оборачивает часть текстового узла во фразовый элемент (b, i, uicontrol...).</summary>
    public static DitaNode? WrapTextRange(DitaNode textNode, int start, int length, string elementName)
    {
        if (textNode.Kind != NodeKind.Text || textNode.Parent is null || length <= 0)
        {
            return null;
        }

        var text = textNode.Value;
        if (start < 0 || start + length > text.Length)
        {
            return null;
        }

        var parent = textNode.Parent;
        var index = parent.IndexOf(textNode);
        var before = text[..start];
        var middle = text.Substring(start, length);
        var after = text[(start + length)..];

        parent.Remove(textNode);

        var insertAt = index;
        if (before.Length > 0)
        {
            parent.Insert(insertAt++, DitaNode.Text(before));
        }

        var wrapper = DitaNode.Element(elementName);
        wrapper.Add(DitaNode.Text(middle));
        parent.Insert(insertAt++, wrapper);

        if (after.Length > 0)
        {
            parent.Insert(insertAt, DitaNode.Text(after));
        }

        return wrapper;
    }

    /// <summary>Разделяет блок на два по позиции в тексте (Enter в режиме «Автор»).</summary>
    public static DitaNode? SplitBlock(DitaNode block, int caretOffset)
    {
        var parent = block.Parent;
        if (parent is null)
        {
            return null;
        }

        var elementIndex = ElementIndexOf(parent, block) + 1;
        if (!Catalog.CanInsert(parent, block.Name, elementIndex))
        {
            return null;
        }

        var text = block.InnerText;
        caretOffset = Math.Clamp(caretOffset, 0, text.Length);

        var tail = text[caretOffset..];
        block.SetText(text[..caretOffset]);

        var newBlock = DitaNode.Element(block.Name);
        foreach (var attr in block.Attributes)
        {
            if (attr.Name != "id")
            {
                newBlock.SetAttribute(attr.Name, attr.Value);
            }
        }

        newBlock.SetText(tail);
        parent.Insert(parent.IndexOf(block) + 1, newBlock);
        return newBlock;
    }

    /// <summary>Объединяет блок с предыдущим соседом того же типа (Backspace в начале блока).</summary>
    public static DitaNode? MergeWithPrevious(DitaNode block)
    {
        var previous = PreviousElement(block);
        if (previous is null || previous.Name != block.Name)
        {
            return null;
        }

        foreach (var child in block.Children.ToList())
        {
            block.Remove(child);
            previous.Add(child);
        }

        block.RemoveSelf();
        return previous;
    }

    /// <summary>Уникальный идентификатор для элемента внутри документа.</summary>
    public static string GenerateId(DitaDocument document, string prefix)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in document.Root.DescendantsAndSelf())
        {
            var id = node.GetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                used.Add(id!);
            }
        }

        var counter = 1;
        string candidate;
        do
        {
            candidate = $"{prefix}_{counter++}";
        }
        while (used.Contains(candidate));

        return candidate;
    }

    // ------------------------------------------------------------- служебное

    public static DitaNode? PreviousElement(DitaNode node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return null;
        }

        DitaNode? previous = null;
        foreach (var child in parent.ElementChildren())
        {
            if (ReferenceEquals(child, node))
            {
                return previous;
            }

            previous = child;
        }

        return null;
    }

    public static DitaNode? NextElement(DitaNode node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return null;
        }

        var found = false;
        foreach (var child in parent.ElementChildren())
        {
            if (found)
            {
                return child;
            }

            if (ReferenceEquals(child, node))
            {
                found = true;
            }
        }

        return null;
    }

    public static int ElementIndexOf(DitaNode parent, DitaNode child)
    {
        var index = 0;
        foreach (var candidate in parent.ElementChildren())
        {
            if (ReferenceEquals(candidate, child))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public static int ChildIndexForElementIndex(DitaNode parent, int elementIndex)
    {
        if (elementIndex <= 0)
        {
            return 0;
        }

        var seen = 0;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i].Kind != NodeKind.Element)
            {
                continue;
            }

            seen++;
            if (seen == elementIndex)
            {
                return i + 1;
            }
        }

        return parent.Children.Count;
    }
}
