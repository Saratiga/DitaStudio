using System.Text;

namespace DitaStudio.Core.Model;

public enum NodeKind
{
    Element,
    Text,
    Comment,
    ProcessingInstruction
}

/// <summary>
/// Узел DITA-документа. Лёгкая изменяемая DOM-модель: сохраняет порядок атрибутов,
/// умеет отдавать позицию в исходном XML и восстанавливаться в текст без потерь.
/// </summary>
public sealed class DitaNode
{
    private readonly List<DitaNode> _children = new();
    private readonly List<DitaAttribute> _attributes = new();

    public DitaNode(NodeKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public static DitaNode Element(string name) => new(NodeKind.Element, name);

    public static DitaNode Text(string value) => new(NodeKind.Text, "#text") { Value = value };

    public static DitaNode Comment(string value) => new(NodeKind.Comment, "#comment") { Value = value };

    public static DitaNode Pi(string target, string value) =>
        new(NodeKind.ProcessingInstruction, target) { Value = value };

    public NodeKind Kind { get; }

    /// <summary>Имя элемента (для текста — "#text").</summary>
    public string Name { get; set; }

    /// <summary>Текст для текстовых узлов, комментариев и PI.</summary>
    public string Value { get; set; } = string.Empty;

    public DitaNode? Parent { get; private set; }

    public IReadOnlyList<DitaNode> Children => _children;

    public IReadOnlyList<DitaAttribute> Attributes => _attributes;

    /// <summary>Номер строки в исходном файле (1-based, 0 — неизвестно).</summary>
    public int Line { get; set; }

    public int Column { get; set; }

    /// <summary>Служебная метка для привязки к элементам интерфейса.</summary>
    public object? Tag { get; set; }

    // ---------------------------------------------------------------- атрибуты

    public string? GetAttribute(string name)
    {
        foreach (var a in _attributes)
        {
            if (string.Equals(a.Name, name, StringComparison.Ordinal))
            {
                return a.Value;
            }
        }

        return null;
    }

    public bool HasAttribute(string name) => GetAttribute(name) is not null;

    public void SetAttribute(string name, string? value)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (string.Equals(_attributes[i].Name, name, StringComparison.Ordinal))
            {
                if (value is null)
                {
                    _attributes.RemoveAt(i);
                }
                else
                {
                    _attributes[i] = new DitaAttribute(name, value);
                }

                return;
            }
        }

        if (value is not null)
        {
            _attributes.Add(new DitaAttribute(name, value));
        }
    }

    public void RemoveAttribute(string name) => SetAttribute(name, null);

    public void ClearAttributes() => _attributes.Clear();

    // ------------------------------------------------------------------ дерево

    public void Add(DitaNode child) => Insert(_children.Count, child);

    public void Insert(int index, DitaNode child)
    {
        if (child.Parent is not null)
        {
            child.Parent.Remove(child);
        }

        if (index < 0)
        {
            index = 0;
        }

        if (index > _children.Count)
        {
            index = _children.Count;
        }

        _children.Insert(index, child);
        child.Parent = this;
    }

    public bool Remove(DitaNode child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            return true;
        }

        return false;
    }

    public void RemoveSelf() => Parent?.Remove(this);

    public void ReplaceWith(DitaNode replacement)
    {
        if (Parent is null)
        {
            return;
        }

        var parent = Parent;
        var index = parent.IndexOf(this);
        parent.Remove(this);
        parent.Insert(index, replacement);
    }

    public int IndexOf(DitaNode child) => _children.IndexOf(child);

    public int IndexInParent => Parent?.IndexOf(this) ?? -1;

    public DitaNode? PreviousSibling
    {
        get
        {
            var i = IndexInParent;
            return i > 0 ? Parent!.Children[i - 1] : null;
        }
    }

    public DitaNode? NextSibling
    {
        get
        {
            var i = IndexInParent;
            return i >= 0 && i + 1 < Parent!.Children.Count ? Parent.Children[i + 1] : null;
        }
    }

    public IEnumerable<DitaNode> Ancestors()
    {
        var p = Parent;
        while (p is not null)
        {
            yield return p;
            p = p.Parent;
        }
    }

    public IEnumerable<DitaNode> Descendants()
    {
        foreach (var c in _children)
        {
            yield return c;
            foreach (var d in c.Descendants())
            {
                yield return d;
            }
        }
    }

    public IEnumerable<DitaNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var d in Descendants())
        {
            yield return d;
        }
    }

    public IEnumerable<DitaNode> ElementChildren()
    {
        foreach (var c in _children)
        {
            if (c.Kind == NodeKind.Element)
            {
                yield return c;
            }
        }
    }

    public DitaNode? FirstElement(string name)
    {
        foreach (var c in _children)
        {
            if (c.Kind == NodeKind.Element && c.Name == name)
            {
                return c;
            }
        }

        return null;
    }

    public DitaNode? FindDescendant(string name)
    {
        foreach (var d in Descendants())
        {
            if (d.Kind == NodeKind.Element && d.Name == name)
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>Ищет ближайшего предка (или сам узел) с указанным именем.</summary>
    public DitaNode? Closest(string name)
    {
        foreach (var n in DescendantsAndSelfUp())
        {
            if (n.Kind == NodeKind.Element && n.Name == name)
            {
                return n;
            }
        }

        return null;

        IEnumerable<DitaNode> DescendantsAndSelfUp()
        {
            var cur = this;
            while (cur is not null)
            {
                yield return cur;
                cur = cur.Parent;
            }
        }
    }

    // ------------------------------------------------------------------- текст

    /// <summary>Весь текст поддерева.</summary>
    public string InnerText
    {
        get
        {
            var sb = new StringBuilder();
            AppendText(this, sb);
            return sb.ToString();

            static void AppendText(DitaNode n, StringBuilder sb)
            {
                if (n.Kind == NodeKind.Text)
                {
                    sb.Append(n.Value);
                    return;
                }

                foreach (var c in n.Children)
                {
                    AppendText(c, sb);
                }
            }
        }
    }

    /// <summary>Заменяет всё содержимое одним текстовым узлом.</summary>
    public void SetText(string text)
    {
        foreach (var c in _children.ToList())
        {
            Remove(c);
        }

        if (text.Length > 0)
        {
            Add(Text(text));
        }
    }

    public DitaNode CloneDeep()
    {
        var copy = new DitaNode(Kind, Name) { Value = Value, Line = Line, Column = Column };
        foreach (var a in _attributes)
        {
            copy._attributes.Add(a);
        }

        foreach (var c in _children)
        {
            copy.Add(c.CloneDeep());
        }

        return copy;
    }

    /// <summary>Путь вида /concept/conbody/p[2] — используется в сообщениях валидатора.</summary>
    public string Path
    {
        get
        {
            if (Kind != NodeKind.Element)
            {
                return Parent?.Path ?? "/";
            }

            var parts = new List<string>();
            var cur = this;
            while (cur is not null && cur.Kind == NodeKind.Element)
            {
                var index = 1;
                if (cur.Parent is not null)
                {
                    index = 0;
                    foreach (var sib in cur.Parent.ElementChildren())
                    {
                        if (sib.Name == cur.Name)
                        {
                            index++;
                        }

                        if (ReferenceEquals(sib, cur))
                        {
                            break;
                        }
                    }
                }

                var same = cur.Parent is null
                    ? 1
                    : cur.Parent.ElementChildren().Count(x => x.Name == cur.Name);
                parts.Add(same > 1 ? $"{cur.Name}[{index}]" : cur.Name);
                cur = cur.Parent;
            }

            parts.Reverse();
            return "/" + string.Join("/", parts);
        }
    }

    public override string ToString() => Kind switch
    {
        NodeKind.Element => $"<{Name}>",
        NodeKind.Text => $"\"{Value}\"",
        NodeKind.Comment => "<!-- ... -->",
        _ => $"<?{Name}?>"
    };
}

public readonly struct DitaAttribute
{
    public DitaAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    public override string ToString() => $"{Name}=\"{Value}\"";
}
