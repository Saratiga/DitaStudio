using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Presentation.Authoring;

/// <summary>
/// Цепочка фразовых элементов, внутри которых стоит символ блока: от внешнего к внутреннему
/// (<c>[b, codeph]</c> для текста в <c>&lt;b&gt;&lt;codeph&gt;…</c>). Плашка — атомарный узел
/// (пустой фразовый элемент, картинка, комментарий, вложенный блочный элемент), занимающий
/// один символ <see cref="InlineContent.ChipChar"/>; тогда <see cref="Chip"/> — сам узел.
/// </summary>
public sealed class InlineChain
{
    public static readonly InlineChain Empty = new(Array.Empty<DitaNode>(), null);

    public InlineChain(IReadOnlyList<DitaNode> nodes, DitaNode? chip)
    {
        Nodes = nodes;
        Chip = chip;
    }

    /// <summary>Фразовые элементы-предки, от внешнего к внутреннему.</summary>
    public IReadOnlyList<DitaNode> Nodes { get; }

    /// <summary>Узел плашки или null для обычного текста.</summary>
    public DitaNode? Chip { get; }

    public bool IsChip => Chip is not null;

    /// <summary>Та же цепочка без плашки — оформление, которое наследует набранный рядом текст.</summary>
    public InlineChain TextChain => Chip is null ? this : Nodes.Count == 0 ? Empty : new InlineChain(Nodes, null);

    /// <summary>Одинаковые предки (по ссылке) и одна и та же плашка.</summary>
    public bool SameAs(InlineChain other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!ReferenceEquals(Chip, other.Chip) || Nodes.Count != other.Nodes.Count)
        {
            return false;
        }

        for (var i = 0; i < Nodes.Count; i++)
        {
            if (!ReferenceEquals(Nodes[i], other.Nodes[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Непрерывный участок блока с одной цепочкой (для оформления в редакторе).</summary>
public readonly record struct InlineSpan(int Start, int Length, InlineChain Chain)
{
    public int End => Start + Length;
}

/// <summary>
/// Смешанное содержимое одного элемента DITA (абзац, заголовок, ячейка, cmd…) в плоском виде
/// для редактора блока: строка текста и для каждого символа — цепочка фразовых элементов.
/// Редактор меняет текст (<see cref="Replace"/>), фразовые элементы следуют за символами;
/// <see cref="WriteBack"/> восстанавливает исходную вложенность в модели. Не зависит от
/// UI-библиотеки — замена WPF RichTextBox с тегами прогонов (InlineEditor) для Avalonia.
/// </summary>
public sealed class InlineContent
{
    /// <summary>Символ-заместитель плашки в тексте (U+FFFC OBJECT REPLACEMENT CHARACTER).</summary>
    public const char ChipChar = '￼';

    private readonly StringBuilder _text = new();
    private readonly List<InlineChain> _chains = new();

    private InlineContent(DitaNode node, DitaNode? after, DitaNode? before)
    {
        Node = node;
        After = after;
        Before = before;
    }

    /// <summary>Элемент, содержимое которого редактируется.</summary>
    public DitaNode Node { get; }

    /// <summary>Вложенный блок, после которого начинается участок (null — с начала элемента).</summary>
    public DitaNode? After { get; }

    /// <summary>Вложенный блок, перед которым участок кончается (null — до конца элемента).</summary>
    public DitaNode? Before { get; }

    /// <summary>Участок — начало элемента (Backspace в его начале объединяет с предыдущим блоком).</summary>
    public bool IsFirstSegment => After is null;

    /// <summary>Участок — конец элемента (Delete в его конце присоединяет следующий блок).</summary>
    public bool IsLastSegment => Before is null;

    public string Text => _text.ToString();

    public int Length => _text.Length;

    /// <summary>Всё содержимое элемента (вложенные блоки, если есть, — плашками).</summary>
    public static InlineContent FromNode(DitaNode node) => Segment(node, null, null);

    /// <summary>Участок содержимого между вложенными блоками <paramref name="after"/> и <paramref name="before"/>.</summary>
    public static InlineContent Segment(DitaNode node, DitaNode? after, DitaNode? before)
    {
        var content = new InlineContent(node, after, before);
        content.Load();
        return content;
    }

    // ---------------------------------------------------------------- смешанное содержимое

    // Блочные по каталогу, но «плавающие» внутри текста элементы — остаются плашками.
    private static readonly HashSet<string> FloatingElements = new(StringComparer.Ordinal)
    {
        "draft-comment", "required-cleanup", "data", "data-about", "foreign", "unknown"
    };

    /// <summary>
    /// Вложенный блок внутри смешанного содержимого (список в пункте списка, абзац в заметке,
    /// таблица в ячейке) — показывается отдельным блоком, а не плашкой в строке текста.
    /// </summary>
    public static bool IsStructuralChild(DitaNode child) =>
        child.Kind == NodeKind.Element &&
        !FloatingElements.Contains(child.Name) &&
        DitaCatalog.Default.Get(child.Name)?.Display is DisplayKind.Block or DisplayKind.Container or DisplayKind.Table or DisplayKind.Preformatted;

    public static bool HasStructuralChildren(DitaNode node) => node.Children.Any(IsStructuralChild);

    /// <summary>
    /// Текстовые участки элемента между вложенными блоками — (после какого блока, перед каким).
    /// Участки из одних пробелов пропускаются; у элемента без вложенных блоков — один участок
    /// (даже пустой: в пустой абзац должно быть куда писать).
    /// </summary>
    public static IReadOnlyList<(DitaNode? After, DitaNode? Before)> Segments(DitaNode node)
    {
        var result = new List<(DitaNode?, DitaNode?)>();
        DitaNode? after = null;
        var meaningful = false;
        foreach (var child in node.Children)
        {
            if (IsStructuralChild(child))
            {
                if (meaningful)
                {
                    result.Add((after, child));
                }

                after = child;
                meaningful = false;
            }
            else
            {
                meaningful |= child.Kind != NodeKind.Text || !string.IsNullOrWhiteSpace(child.Value);
            }
        }

        if (meaningful || result.Count == 0 && after is null)
        {
            result.Add((after, null));
        }

        return result;
    }

    /// <summary>Индексы детей участка [start, end); null — граница больше не ребёнок элемента.</summary>
    private (int Start, int End)? Range()
    {
        var start = After is null ? 0 : Node.IndexOf(After) + 1;
        var end = Before is null ? Node.Children.Count : Node.IndexOf(Before);
        return start <= 0 && After is not null || end < 0 || end < start ? null : (start, end);
    }

    /// <summary>Перечитывает содержимое из модели (после записи — чтобы цепочки ссылались на живые узлы).</summary>
    public void Load()
    {
        _text.Clear();
        _chains.Clear();
        if (Range() is { } range)
        {
            var path = new List<DitaNode>();
            for (var i = range.Start; i < range.End; i++)
            {
                AppendChild(Node.Children[i], path);
            }
        }

        CollapseLayoutWhitespace();
    }

    /// <summary>
    /// Отступы из форматированного XML (перевод строки + пробелы) — незначащие: такой пробельный
    /// участок сворачивается в один пробел, а в начале и конце блока убирается совсем. Обычные
    /// пробелы, набранные в строке, не трогаются. В модель это попадает только при правке блока.
    /// </summary>
    private void CollapseLayoutWhitespace()
    {
        var i = 0;
        while (i < _text.Length)
        {
            if (!char.IsWhiteSpace(_text[i]))
            {
                i++;
                continue;
            }

            var end = i;
            var layout = false;
            while (end < _text.Length && char.IsWhiteSpace(_text[end]))
            {
                layout |= _text[end] is '\r' or '\n' or '\t';
                end++;
            }

            if (!layout)
            {
                i = end;
                continue;
            }

            var keep = i > 0 && end < _text.Length ? 1 : 0;
            if (keep == 1)
            {
                _text[i] = ' ';
            }

            _text.Remove(i + keep, end - i - keep);
            _chains.RemoveRange(i + keep, end - i - keep);
            i += keep;
        }
    }

    private void AppendChildren(DitaNode parent, List<DitaNode> path)
    {
        foreach (var child in parent.Children)
        {
            AppendChild(child, path);
        }
    }

    private void AppendChild(DitaNode child, List<DitaNode> path)
    {
        switch (child.Kind)
        {
            case NodeKind.Text:
                AppendText(child.Value, path.Count == 0 ? InlineChain.Empty : new InlineChain(path.ToArray(), null));
                break;

            case NodeKind.Element when IsInline(child) && child.Children.Count > 0:
                // В непустой фразовый элемент спускаемся на любую глубину — каждый лист (текст,
                // картинка, чужая плашка) получает своё представление (как в WPF InlineEditor).
                path.Add(child);
                AppendChildren(child, path);
                path.RemoveAt(path.Count - 1);
                break;

            case NodeKind.Element:
            case NodeKind.Comment:
                // Пустой фразовый элемент, картинка, комментарий — одна атомарная плашка,
                // иначе он потерялся бы при записи.
                _text.Append(ChipChar);
                _chains.Add(new InlineChain(path.ToArray(), child));
                break;
        }
    }

    private static bool IsInline(DitaNode node) => DitaCatalog.Default.Get(node.Name)?.IsInline ?? false;

    private void AppendText(string value, InlineChain chain)
    {
        foreach (var ch in value)
        {
            // Символ-заместитель в настоящем тексте спутался бы с плашкой.
            _text.Append(ch == ChipChar ? ' ' : ch);
            _chains.Add(chain);
        }
    }

    // ---------------------------------------------------------------- чтение

    public InlineChain ChainAt(int offset) => _chains[offset];

    /// <summary>Узел плашки в позиции или null.</summary>
    public DitaNode? ChipAt(int offset) => offset >= 0 && offset < _chains.Count ? _chains[offset].Chip : null;

    /// <summary>Участки с одинаковой цепочкой; каждая плашка — отдельный участок.</summary>
    public IEnumerable<InlineSpan> Spans()
    {
        var start = 0;
        for (var i = 1; i <= _chains.Count; i++)
        {
            if (i == _chains.Count || _chains[i].IsChip || _chains[start].IsChip || !_chains[i].SameAs(_chains[start]))
            {
                yield return new InlineSpan(start, i - start, _chains[start]);
                start = i;
            }
        }
    }

    // ---------------------------------------------------------------- правка текста

    /// <summary>
    /// Замена участка текста — то же, что правка документа редактора. Вставленный текст
    /// наследует оформление символа слева (в начале блока — первого символа), как набор в
    /// RichTextBox; рядом с плашкой — оформление её предков, а не саму плашку.
    /// </summary>
    public void Replace(int offset, int removedLength, string inserted)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        removedLength = Math.Clamp(removedLength, 0, _text.Length - offset);

        // Редактор может заменить кусок шире настоящей правки (выделение поверх границы
        // элемента, вставка, автозамена). Совпадающие начало и конец остаются со своим
        // оформлением — иначе весь кусок получил бы одну цепочку и, например, <fn> внутри
        // него исчезла бы, а её текст слился с абзацем.
        if (removedLength > 0 && inserted.Length > 0)
        {
            var max = Math.Min(removedLength, inserted.Length);
            var prefix = 0;
            while (prefix < max && _text[offset + prefix] == inserted[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < max - prefix && _text[offset + removedLength - 1 - suffix] == inserted[inserted.Length - 1 - suffix])
            {
                suffix++;
            }

            if (prefix + suffix > 0)
            {
                Replace(offset + prefix, removedLength - prefix - suffix, inserted.Substring(prefix, inserted.Length - prefix - suffix));
                return;
            }
        }

        // Замена выделения (исправление слова, набор поверх) сохраняет его оформление.
        var replacedChain = removedLength > 0 ? _chains[offset].TextChain : null;
        if (removedLength > 0)
        {
            Remember(_text.ToString(offset, removedLength), _chains.GetRange(offset, removedLength));
            _text.Remove(offset, removedLength);
            _chains.RemoveRange(offset, removedLength);
        }

        if (inserted.Length == 0)
        {
            return;
        }

        // Возврат только что удалённого куска (отмена в редакторе, вырезать-вставить) —
        // с его оформлением и плашками, а не как простой текст.
        var restored = inserted.Length > 1 || inserted.Contains(ChipChar) ? _removed.FindLastIndex(r => r.Text == inserted) : -1;
        if (restored >= 0)
        {
            _text.Insert(offset, inserted);
            _chains.InsertRange(offset, _removed[restored].Chains);
            return;
        }

        var chain = replacedChain is not null ? replacedChain
            : offset > 0 ? _chains[offset - 1].TextChain
            : _chains.Count > 0 ? _chains[0].TextChain
            : InlineChain.Empty;

        _text.Insert(offset, inserted);
        _chains.InsertRange(offset, Enumerable.Repeat(chain, inserted.Length));
    }

    private readonly List<(string Text, List<InlineChain> Chains)> _removed = new();

    private void Remember(string text, List<InlineChain> chains)
    {
        if (!chains.Any(c => c.IsChip || c.Nodes.Count > 0))
        {
            return;
        }

        _removed.Add((text, chains));
        if (_removed.Count > 16)
        {
            _removed.RemoveAt(0);
        }
    }

    /// <summary>Вставляет узел плашкой (ссылка, картинка, сноска) в позицию.</summary>
    public void InsertChip(int offset, DitaNode node)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        var nodes = offset > 0 ? _chains[offset - 1].Nodes : Array.Empty<DitaNode>();
        _text.Insert(offset, ChipChar);
        _chains.Insert(offset, new InlineChain(nodes, node));
    }

    /// <summary>
    /// Оборачивает участок во фразовый элемент. Новый элемент встаёт сразу под общими для всего
    /// участка предками, поэтому частично оформленный текст вкладывается в него целиком
    /// (<c>&lt;w&gt;x&lt;b&gt;y&lt;/b&gt;&lt;/w&gt;</c>, а не два отдельных <c>w</c>).
    /// </summary>
    public bool Wrap(int start, int length, string elementName)
    {
        if (length <= 0 || start < 0 || start + length > _text.Length)
        {
            return false;
        }

        var common = _chains[start].Nodes.Count;
        for (var i = start + 1; i < start + length && common > 0; i++)
        {
            var nodes = _chains[i].Nodes;
            var same = 0;
            while (same < common && same < nodes.Count && ReferenceEquals(nodes[same], _chains[start].Nodes[same]))
            {
                same++;
            }

            common = same;
        }

        var wrapper = DitaNode.Element(elementName);
        var mapped = new Dictionary<InlineChain, InlineChain>(ReferenceEqualityComparer.Instance);
        for (var i = start; i < start + length; i++)
        {
            var old = _chains[i];
            if (!mapped.TryGetValue(old, out var updated))
            {
                var nodes = new List<DitaNode>(old.Nodes.Count + 1);
                nodes.AddRange(old.Nodes.Take(common));
                nodes.Add(wrapper);
                nodes.AddRange(old.Nodes.Skip(common));
                updated = new InlineChain(nodes, old.Chip);
                mapped[old] = updated;
            }

            _chains[i] = updated;
        }

        return true;
    }

    /// <summary>Снимает фразовое оформление с участка; плашки остаются, но выходят из фразовых элементов.</summary>
    public void ClearFormatting(int start, int length)
    {
        for (var i = Math.Max(start, 0); i < Math.Min(start + length, _chains.Count); i++)
        {
            _chains[i] = _chains[i].Chip is { } chip ? new InlineChain(Array.Empty<DitaNode>(), chip) : InlineChain.Empty;
        }
    }

    // ---------------------------------------------------------------- запись в модель

    /// <summary>Заменяет детей участка содержимым редактора и перечитывает цепочки.</summary>
    public void WriteBack()
    {
        if (Range() is not { } range)
        {
            // Граница участка ушла из элемента (правка структуры) — писать некуда.
            return;
        }

        var nodes = BuildNodes(0, _text.Length);
        for (var i = range.End - 1; i >= range.Start; i--)
        {
            Node.Remove(Node.Children[i]);
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            Node.Insert(range.Start + i, nodes[i]);
        }

        Load();
    }

    /// <summary>
    /// Делит элемент в позиции <paramref name="offset"/> участка: голова участка остаётся на
    /// месте, хвост участка и всё, что после него (вложенные блоки, другие участки), уходит
    /// в <paramref name="target"/>.
    /// </summary>
    public bool SplitInto(int offset, DitaNode target)
    {
        if (Range() is not { } range)
        {
            return false;
        }

        offset = Math.Clamp(offset, 0, _text.Length);
        var head = BuildNodes(0, offset);
        var tail = BuildNodes(offset, _text.Length);
        var following = Node.Children.Skip(range.End).ToList();

        for (var i = Node.Children.Count - 1; i >= range.Start; i--)
        {
            Node.Remove(Node.Children[i]);
        }

        head.ForEach(Node.Add);
        tail.ForEach(target.Add);
        following.ForEach(target.Add);
        return true;
    }

    /// <summary>
    /// Узлы DITA для участка [start, end): фразовые элементы цепочек клонируются (с атрибутами)
    /// и вкладываются в исходном порядке, плашки копируются целиком.
    /// </summary>
    public List<DitaNode> BuildNodes(int start, int end)
    {
        var roots = new List<DitaNode>();
        var originalPath = new List<DitaNode>();
        var builtPath = new List<DitaNode>();

        void Append(DitaNode child)
        {
            if (builtPath.Count > 0)
            {
                builtPath[^1].Add(child);
            }
            else
            {
                roots.Add(child);
            }
        }

        void Descend(IReadOnlyList<DitaNode> chain)
        {
            var common = 0;
            while (common < originalPath.Count && common < chain.Count && ReferenceEquals(originalPath[common], chain[common]))
            {
                common++;
            }

            originalPath.RemoveRange(common, originalPath.Count - common);
            builtPath.RemoveRange(common, builtPath.Count - common);

            for (var i = common; i < chain.Count; i++)
            {
                var source = chain[i];
                var clone = DitaNode.Element(source.Name);
                foreach (var attr in source.Attributes)
                {
                    clone.SetAttribute(attr.Name, attr.Value);
                }

                Append(clone);
                originalPath.Add(source);
                builtPath.Add(clone);
            }
        }

        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, start, _text.Length);
        var text = new StringBuilder();
        IReadOnlyList<DitaNode>? textChain = null;

        void FlushText()
        {
            if (text.Length > 0 && textChain is not null)
            {
                Descend(textChain);
                Append(DitaNode.Text(text.ToString()));
            }

            text.Clear();
            textChain = null;
        }

        for (var i = start; i < end; i++)
        {
            var chain = _chains[i];
            if (chain.Chip is { } chip)
            {
                FlushText();
                Descend(chain.Nodes);
                Append(chip.CloneDeep());
                continue;
            }

            if (_text[i] == ChipChar)
            {
                // Символ-заместитель без плашки (например, вставлен из буфера) — не текст.
                continue;
            }

            if (textChain is not null && !SameNodes(textChain, chain.Nodes))
            {
                FlushText();
            }

            textChain = chain.Nodes;
            text.Append(_text[i]);
        }

        FlushText();
        return roots;
    }

    private static bool SameNodes(IReadOnlyList<DitaNode> a, IReadOnlyList<DitaNode> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- подписи плашек

    /// <summary>Подпись плашки — как в WPF-версии.</summary>
    public static string DescribeChip(DitaNode node)
    {
        if (node.Kind == NodeKind.Comment)
        {
            return "комментарий";
        }

        switch (node.Name)
        {
            case "image":
            {
                var href = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "?";
                return $"🖼 {System.IO.Path.GetFileName(href)}";
            }

            case "xref":
            case "link":
            {
                var target = node.GetAttribute("href") ?? node.GetAttribute("keyref") ?? "?";
                return $"🔗 {target}";
            }

            case "fn":
                return "ˣ сноска";

            case "indexterm":
            {
                var parts = new List<string>();
                CollectIndextermText(node, parts);
                return $"☰ {string.Join(" / ", parts)}";
            }

            case "abbreviated-form":
                return $"◆ {node.GetAttribute("keyref")}";

            case "ph" when node.HasAttribute("conref") || node.HasAttribute("conkeyref"):
                return $"⇗ {node.GetAttribute("conref") ?? node.GetAttribute("conkeyref")}";
        }

        if (node.HasAttribute("conref") || node.HasAttribute("conkeyref"))
        {
            return $"⇗ {node.Name}";
        }

        var text = node.InnerText.Trim();
        return text.Length > 0 ? $"<{node.Name}> {Shorten(text)}" : $"<{node.Name}/>";
    }

    private static string Shorten(string value) => value.Length <= 24 ? value : value[..24] + "…";

    /// <summary>Термин indexterm и вложенные подпункты — отдельными кусками, а не InnerText одной строкой.</summary>
    private static void CollectIndextermText(DitaNode node, List<string> parts)
    {
        var direct = string.Concat(node.Children.Where(c => c.Kind == NodeKind.Text).Select(c => c.Value)).Trim();
        if (direct.Length > 0)
        {
            parts.Add(direct);
        }

        foreach (var child in node.ElementChildren().Where(c => c.Name == "indexterm"))
        {
            CollectIndextermText(child, parts);
        }
    }
}
