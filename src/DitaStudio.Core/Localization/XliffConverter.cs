using System.Globalization;
using System.Xml.Linq;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Localization;

/// <summary>
/// Экспорт/импорт XLIFF 1.2 для перевода отдельного топика. Сегмент — блочный элемент с прямым
/// текстом (title, shortdesc, p, li...); фразовые элементы внутри сегмента становятся bpt/ept
/// (парные — переводчик видит и правит текст внутри) или ph (пустые — image, xref без текста).
/// Экспорт и импорт используют один и тот же обход дерева, поэтому номер сегмента (id) и номер
/// внутреннего placeholder'а совпадают без хранения отдельной карты соответствий — импорт просто
/// обходит СВЕЖУЮ версию того же документа тем же способом.
/// </summary>
public static class XliffConverter
{
    public static XDocument Export(DitaDocument document, string sourceLang, string targetLang)
    {
        var body = new XElement("body");
        var counter = 0;
        WalkForExport(document.Root, body, ref counter);

        var file = new XElement("file",
            new XAttribute("original", document.FilePath ?? document.Root.Name),
            new XAttribute("source-language", sourceLang),
            new XAttribute("target-language", targetLang),
            new XAttribute("datatype", "xml"),
            body);

        var xliff = new XElement("xliff", new XAttribute("version", "1.2"), file);
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), xliff);
    }

    private static void WalkForExport(DitaNode node, XElement body, ref int counter)
    {
        foreach (var child in node.ElementChildren())
        {
            if (IsPhraseInline(child))
            {
                continue;
            }

            WalkForExport(child, body, ref counter);
        }

        if (!HasDirectText(node))
        {
            return;
        }

        counter++;
        var source = new XElement("source");
        SerializeSegment(node, source);
        var target = new XElement("target");
        SerializeSegment(node, target);

        body.Add(new XElement("trans-unit",
            new XAttribute("id", counter.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("resname", node.Path),
            source,
            target));
    }

    /// <summary>Читает XLIFF и подставляет содержимое &lt;target&gt; на место исходного текста в
    /// document — сегменты сопоставляются с узлами тем же обходом, что и при экспорте. Возвращает
    /// число применённых сегментов; проблемы (несогласованные id, разошедшаяся структура) идут в
    /// warnings, не прерывая импорт остального документа.</summary>
    public static int Import(DitaDocument document, XDocument xliff, List<string> warnings)
    {
        var body = xliff.Root?.Element("file")?.Element("body");
        if (body is null)
        {
            warnings.Add("В XLIFF нет <file>/<body> — импортировать нечего.");
            return 0;
        }

        var units = body.Elements("trans-unit")
            .Where(u => u.Attribute("id") is not null)
            .ToDictionary(u => u.Attribute("id")!.Value, u => u);

        var counter = 0;
        var applied = 0;
        ApplyForImport(document.Root, units, warnings, ref counter, ref applied);
        return applied;
    }

    private static void ApplyForImport(DitaNode node, Dictionary<string, XElement> units, List<string> warnings, ref int counter, ref int applied)
    {
        foreach (var child in node.ElementChildren())
        {
            if (IsPhraseInline(child))
            {
                continue;
            }

            ApplyForImport(child, units, warnings, ref counter, ref applied);
        }

        if (!HasDirectText(node))
        {
            return;
        }

        counter++;
        var id = counter.ToString(CultureInfo.InvariantCulture);
        var context = $"сегмент #{id} ({node.Path})";

        if (!units.TryGetValue(id, out var unit))
        {
            warnings.Add($"{context}: в XLIFF нет такого сегмента — пропущен, документ мог измениться после экспорта.");
            return;
        }

        var target = unit.Element("target");
        if (target is null)
        {
            warnings.Add($"{context}: нет <target> — пропущен.");
            return;
        }

        var registry = BuildInlineRegistry(node);
        var newChildren = ReconstructChildren(target, registry, warnings, context);
        ReplaceDirectContent(node, newChildren);
        applied++;
    }

    /// <summary>Настоящий фразовый элемент (b/i/xref/uicontrol...) — при обходе за сегментами не
    /// нужно спускаться внутрь: его содержимое либо часть текущего сегмента (SerializeChildren),
    /// либо (для #PCDATA-элементов) само стало бы дублирующим сегментом.</summary>
    private static bool IsPhraseInline(DitaNode node) => DitaCatalog.Default.Get(node.Name) is { Display: DisplayKind.Inline };

    /// <summary>Место в потоке сегмента: фразовый элемент (Display=Inline) или пустой элемент,
    /// который может стоять внутри текста (Display=Empty — image, boolean, state...). В отличие
    /// от IsPhraseInline используется там, где решаем, что попадает в bpt/ept/ph текущего сегмента —
    /// пустые элементы не имеют прямого текста, поэтому не рискуют задвоить сегментацию, но обход
    /// за вложенными сегментами (WalkForExport/ApplyForImport) в них всё равно спускается — так
    /// находится, например, &lt;alt&gt; внутри &lt;image&gt;.</summary>
    private static bool IsSegmentPlaceholder(DitaNode node) =>
        DitaCatalog.Default.Get(node.Name) is { } def && def.Display is DisplayKind.Inline or DisplayKind.Empty;

    private static bool HasDirectText(DitaNode node) =>
        node.Children.Any(c => c.Kind == NodeKind.Text && !string.IsNullOrWhiteSpace(c.Value));

    // ------------------------------------------------------------------ экспорт сегмента

    private static void SerializeSegment(DitaNode node, XElement into)
    {
        var id = 0;
        SerializeChildren(node, into, ref id);
    }

    private static void SerializeChildren(DitaNode node, XElement into, ref int id)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == NodeKind.Text)
            {
                into.Add(new XText(child.Value));
                continue;
            }

            if (child.Kind != NodeKind.Element || !IsSegmentPlaceholder(child))
            {
                continue; // блочные дети — отдельные сегменты, в этот текст не входят
            }

            id++;
            var childId = id;
            if (child.Children.Count == 0)
            {
                into.Add(new XElement("ph", new XAttribute("id", childId)));
            }
            else
            {
                into.Add(new XElement("bpt", new XAttribute("id", childId)));
                SerializeChildren(child, into, ref id);
                into.Add(new XElement("ept", new XAttribute("id", childId)));
            }
        }
    }

    // ----------------------------------------------------------------- импорт сегмента

    /// <summary>Список фразовых детей узла в том же порядке, в котором SerializeChildren
    /// раздавала им id при экспорте — registry[id-1] это child с этим id.</summary>
    private static List<DitaNode> BuildInlineRegistry(DitaNode node)
    {
        var registry = new List<DitaNode>();
        Walk(node);
        return registry;

        void Walk(DitaNode n)
        {
            foreach (var child in n.Children)
            {
                if (child.Kind != NodeKind.Element || !IsSegmentPlaceholder(child))
                {
                    continue;
                }

                registry.Add(child);
                if (child.Children.Count > 0)
                {
                    Walk(child);
                }
            }
        }
    }

    private static List<DitaNode> ReconstructChildren(XElement target, List<DitaNode> registry, List<string> warnings, string context)
    {
        var root = new List<DitaNode>();
        var stack = new Stack<(int Id, DitaNode Clone, List<DitaNode> Children)>();
        List<DitaNode> Current() => stack.Count == 0 ? root : stack.Peek().Children;

        foreach (var node in target.Nodes())
        {
            switch (node)
            {
                case XText text:
                    Current().Add(DitaNode.Text(text.Value));
                    break;

                case XElement el when el.Name.LocalName == "ph":
                    if (TryResolve(el, registry, warnings, context) is { } phOriginal)
                    {
                        Current().Add(phOriginal.CloneDeep());
                    }

                    break;

                case XElement el when el.Name.LocalName == "bpt":
                    if (TryParseId(el, warnings, context) is { } bptId)
                    {
                        if (bptId < 1 || bptId > registry.Count)
                        {
                            warnings.Add($"{context}: placeholder id=\"{bptId}\" вне диапазона — тег и его содержимое пропущены.");
                        }
                        else
                        {
                            // CloneDeep, а не пустой элемент + атрибуты: у "пустых по модели" элементов
                            // вроде image могут быть свои непереводимые дети (alt, longdescref) —
                            // они не входят в bpt/ept-поток (см. IsSegmentPlaceholder) и должны дойти
                            // до результата как есть. ReplaceDirectContent на ept заменит только то,
                            // что реально было частью сегмента, остальное останется от клона нетронутым.
                            var clone = registry[bptId - 1].CloneDeep();
                            Current().Add(clone);
                            stack.Push((bptId, clone, new List<DitaNode>()));
                        }
                    }

                    break;

                case XElement el when el.Name.LocalName == "ept":
                    var eptId = TryParseId(el, warnings, context);
                    if (stack.Count > 0 && stack.Peek().Id == eptId)
                    {
                        var (_, clone, children) = stack.Pop();
                        ReplaceDirectContent(clone, children);
                    }
                    else
                    {
                        warnings.Add($"{context}: несогласованный </ept id=\"{eptId}\"> — правки внутри тега могли потеряться.");
                    }

                    break;
            }
        }

        while (stack.Count > 0)
        {
            // clone уже стоит в дереве результата (добавлен в Current() в момент bpt, до push) —
            // здесь только доносим накопленное содержимое незакрытого тега, без повторного Add.
            warnings.Add($"{context}: не закрыт тег (bpt id={stack.Peek().Id}) — содержимое сохранено как есть.");
            var (_, clone, children) = stack.Pop();
            ReplaceDirectContent(clone, children);
        }

        return root;
    }

    private static DitaNode? TryResolve(XElement el, List<DitaNode> registry, List<string> warnings, string context)
    {
        var id = TryParseId(el, warnings, context);
        if (id is null)
        {
            return null;
        }

        if (id < 1 || id > registry.Count)
        {
            warnings.Add($"{context}: placeholder id=\"{id}\" вне диапазона — пропущен.");
            return null;
        }

        return registry[id.Value - 1];
    }

    private static int? TryParseId(XElement el, List<string> warnings, string context)
    {
        var value = el.Attribute("id")?.Value;
        if (value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }

        warnings.Add($"{context}: у <{el.Name.LocalName}> нет корректного id — пропущен.");
        return null;
    }

    private static void ReplaceDirectContent(DitaNode node, List<DitaNode> newChildren)
    {
        var segmentChildren = node.Children
            .Where(c => c.Kind == NodeKind.Text || (c.Kind == NodeKind.Element && IsSegmentPlaceholder(c)))
            .ToList();
        var insertIndex = segmentChildren.Count > 0 ? node.IndexOf(segmentChildren[0]) : node.Children.Count;

        foreach (var c in segmentChildren)
        {
            node.Remove(c);
        }

        foreach (var newChild in newChildren)
        {
            node.Insert(insertIndex, newChild);
            insertIndex++;
        }
    }
}
