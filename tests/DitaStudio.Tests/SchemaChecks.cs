using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Каталог DITA, контент-модели, автомат допустимости (включая property-based).
internal static partial class CoreChecks
{
    internal static void CatalogTests()
    {
        Section("Каталог DITA 1.3");
        var catalog = DitaCatalog.Default;

        Check(catalog.Elements.Count > 250, $"в словаре загружено элементов: {catalog.Elements.Count}");

        foreach (var name in new[]
                 {
                     "topic", "concept", "task", "reference", "troubleshooting", "glossentry",
                     "map", "bookmap", "subjectScheme", "p", "ul", "li", "step", "cmd",
                     "table", "tgroup", "entry", "simpletable", "codeblock", "uicontrol",
                     "xref", "keydef", "topicref", "hazardstatement", "learningContent"
                 })
        {
            Check(catalog.IsKnown(name), $"известен элемент <{name}>");
        }

        Check(catalog.Get("p")!.IsMixed, "<p> допускает текст");
        Check(!catalog.Get("ul")!.IsMixed, "<ul> не допускает текст напрямую");
        Check(catalog.Get("b")!.IsInline, "<b> — фразовый элемент");
        Check(catalog.Get("concept")!.IsTopicType, "<concept> — тип топика");
        var mapDef = catalog.Get("map")!; Check(mapDef.IsMapType, $"<map> — карта (display={mapDef.Display}, class=[{mapDef.ClassAttr}])");
        Check(!catalog.Get("topicref")!.IsMapType, "<topicref> не считается корнем карты");
        Check(catalog.Get("note")!.Attributes.ContainsKey("type"), "у <note> есть атрибут @type");
        Check(catalog.Get("p")!.Attributes.ContainsKey("conref"), "универсальные атрибуты подставились в <p>");
        Check(catalog.PublicIdFor("task") is not null, "для task известен публичный идентификатор DOCTYPE");
    }

    internal static void DitaCatalogMiscTests()
    {
        Section("DitaCatalog: InsertableAt/ReplacementsFor/ElementIndexFor/DOCTYPE (по отчёту покрытия)");

        var catalog = DitaCatalog.Default;

        Check(catalog.SystemIdFor("task") == "task.dtd", "SystemIdFor для известного корня");
        Check(catalog.PublicIdFor("no-such-root") is null, "PublicIdFor для неизвестного корня — null");
        Check(catalog.SystemIdFor("no-such-root") is null, "SystemIdFor для неизвестного корня — null");

        Check(catalog.TopicTypes.Any(e => e.Name == "concept") && catalog.TopicTypes.Any(e => e.Name == "task"),
            "TopicTypes перечисляет типы топиков");
        Check(!catalog.TopicTypes.Any(e => e.Name == "p"), "TopicTypes не включает обычные блочные элементы");
        Check(catalog.MapTypes.Any(e => e.Name == "map") && catalog.MapTypes.Any(e => e.Name == "bookmap"),
            "MapTypes перечисляет типы карт");

        var doc = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>текст1</p><p>текст2</p></conbody></concept>");
        var conbody = doc.Root.FirstElement("conbody")!;

        var insertableAtStart = catalog.InsertableAt(conbody, 0).Select(e => e.Name).ToList();
        Check(insertableAtStart.Contains("p") && insertableAtStart.Contains("ul"), "InsertableAt(conbody, 0) предлагает p/ul");
        Check(!insertableAtStart.Contains("title"), "InsertableAt(conbody, 0) не предлагает title (недопустим в conbody)");

        Check(catalog.InsertableAt(conbody, -5).Count == catalog.InsertableAt(conbody, 0).Count,
            "InsertableAt: отрицательный индекс схлопывается к 0");
        Check(catalog.InsertableAt(conbody, 999).Count == catalog.InsertableAt(conbody, 2).Count,
            "InsertableAt: индекс за пределами числа детей схлопывается к их количеству");
        var unknownParent = DitaNode.Element("totally-unknown-element-xyz");
        Check(catalog.InsertableAt(unknownParent, 0).Count == 0, "InsertableAt для родителя, которого нет в каталоге, — пустой список, не падает");

        var firstP = conbody.FirstElement("p")!;
        var replacements = catalog.ReplacementsFor(firstP).Select(e => e.Name).ToList();
        Check(replacements.Contains("ul") || replacements.Contains("note") || replacements.Contains("lq"),
            $"ReplacementsFor(<p>) предлагает совместимые по отображению блочные альтернативы: [{string.Join(",", replacements)}]");
        Check(!replacements.Contains("p"), "ReplacementsFor не предлагает заменить элемент на самого себя");

        var bNode = DitaNode.Element("b");
        bNode.Add(DitaNode.Text("жирный"));
        firstP.Insert(0, bNode);
        var inlineReplacements = catalog.ReplacementsFor(bNode).Select(e => e.Name).ToList();
        Check(inlineReplacements.Count > 0 && inlineReplacements.All(n => catalog.Get(n)!.IsInline),
            $"ReplacementsFor(<b>) предлагает только фразовые альтернативы (совместимый Display): [{string.Join(",", inlineReplacements)}]");

        Check(catalog.ReplacementsFor(doc.Root).Count == 0, "ReplacementsFor для узла без родителя (корень документа) — пустой список");

        var detached = DitaNode.Element("p");
        Check(catalog.ReplacementsFor(detached).Count == 0, "ReplacementsFor для узла без родителя вообще — пустой список");

        // ElementIndexFor считает только элементы среди детей ДО childIndex (childIndex — индекс в
        // общем списке детей, включая текстовые узлы).
        var mixedParent = DitaDocument.Parse("<p>текст<b>a</b> ещё текст<i>b</i></p>").Root;
        Check(DitaCatalog.ElementIndexFor(mixedParent, 0) == 0, "ElementIndexFor(0) — перед первым ребёнком, элементов ещё не было");
        Check(DitaCatalog.ElementIndexFor(mixedParent, 2) == 1, "ElementIndexFor(2) — один элемент (<b>) уже пройден");
        Check(DitaCatalog.ElementIndexFor(mixedParent, mixedParent.Children.Count) == 2,
            "ElementIndexFor(число_детей) — все элементы пройдены (текстовые не считаются)");
    }

    internal static void ContentModelTests()
    {
        Section("Контент-модели");
        var catalog = DitaCatalog.Default;

        var concept = catalog.Get("concept")!;
        Check(concept.Automaton.Validate(new[] { "title", "conbody" }, out _, out _),
            "concept: (title, conbody) — допустимо");
        Check(!concept.Automaton.Validate(new[] { "conbody", "title" }, out _, out _),
            "concept: (conbody, title) — недопустимо");
        Check(!concept.Automaton.Validate(new[] { "conbody" }, out _, out _),
            "concept без title — недопустимо");

        var task = catalog.Get("taskbody")!;
        Check(task.Automaton.Validate(new[] { "context", "steps", "result" }, out _, out _),
            "taskbody: context, steps, result — допустимо");
        Check(!task.Automaton.Validate(new[] { "steps", "context" }, out _, out _),
            "taskbody: порядок steps перед context — недопустимо");

        var step = catalog.Get("step")!;
        Check(step.Automaton.Validate(new[] { "cmd", "info", "stepresult" }, out _, out _),
            "step: cmd, info, stepresult — допустимо");
        Check(!step.Automaton.Validate(new[] { "info", "cmd" }, out _, out _),
            "step без cmd в начале — недопустимо");

        var ul = catalog.Get("ul")!;
        Check(ul.Automaton.Validate(new[] { "li", "li", "li" }, out _, out _), "ul из трёх li — допустимо");
        Check(!ul.Automaton.Validate(Array.Empty<string>(), out _, out _), "пустой ul — недопустимо");

        var body = catalog.Get("body")!;
        Check(body.Automaton.CanInsertAt(new[] { "p" }, 1, "ul"), "в body после p можно вставить ul");
        Check(!body.Automaton.CanInsertAt(new[] { "p" }, 1, "title"), "в body нельзя вставить title");

        var insertable = catalog.Get("taskbody")!.Automaton.InsertableAt(new[] { "context" }, 1);
        Check(insertable.Contains("steps"), "после context предлагается steps");
        Check(!insertable.Contains("prereq"), "после context не предлагается prereq");

        var p = catalog.Get("p")!;
        Check(p.Automaton.AllowedNames.Contains("uicontrol"), "внутри p допустим uicontrol");
        Check(p.Automaton.AllowedNames.Contains("ul"), "внутри p допустим вложенный список");

        var ulNode = catalog.Get("ul")!;
        Check(ulNode.Automaton.CanRemoveAt(new[] { "li", "li" }, 0), "из двух li можно удалить один — ul всё ещё валиден");
        Check(!ulNode.Automaton.CanRemoveAt(new[] { "li" }, 0), "нельзя удалить единственный li — ul опустеет и станет невалидным");
    }

    internal static void ContentModelMiscTests()
    {
        Section("ContentModel: EMPTY/ANY, ToString(), CanRemoveAt (по отчёту покрытия)");

        var empty = ModelParser.Parse("EMPTY");
        Check(empty is ContentModel.Empty, "ключевое слово EMPTY разобрано как ContentModel.Empty");
        Check(empty.ToString() == "EMPTY", "ContentModel.Empty.ToString() == \"EMPTY\"");
        Check(!empty.AllowsText(), "EMPTY не допускает текст");
        var emptyAutomaton = new ModelAutomaton(empty);
        Check(!emptyAutomaton.AllowsAny, "автомат по EMPTY: AllowsAny == false");
        Check(emptyAutomaton.Validate(Array.Empty<string>(), out _, out _), "автомат по EMPTY принимает пустую последовательность детей");
        Check(!emptyAutomaton.Validate(new[] { "p" }, out _, out _), "автомат по EMPTY отклоняет любого ребёнка");

        var any = ModelParser.Parse("ANY");
        Check(any is ContentModel.Any, "ключевое слово ANY разобрано как ContentModel.Any");
        Check(any.ToString() == "ANY", "ContentModel.Any.ToString() == \"ANY\"");
        Check(any.AllowsText(), "ANY допускает текст");
        var anyAutomaton = new ModelAutomaton(any);
        Check(anyAutomaton.AllowsAny, "автомат по ANY: AllowsAny == true");
        Check(anyAutomaton.Validate(new[] { "p", "совершенно-неизвестный-элемент", "ul" }, out _, out _),
            "автомат по ANY принимает любую последовательность произвольных имён");
        Check(anyAutomaton.CanInsertAt(new[] { "p" }, 1, "что-угодно"), "CanInsertAt при ANY всегда true");
        Check(anyAutomaton.CanRemoveAt(Array.Empty<string>(), 0), "CanRemoveAt при ANY всегда true (даже на пустом списке/некорректном индексе)");
        Check(anyAutomaton.InsertableAt(new[] { "p" }, 0).Count == 0,
            "InsertableAt при ANY возвращает пустой список (вставить можно что угодно, перечислять нечего)");

        Check(new ContentModel.Name("xref").ToString() == "xref", "ContentModel.Name.ToString() возвращает само имя");
        Check(ContentModel.Pcdata.Instance.ToString() == "#PCDATA", "ContentModel.Pcdata.ToString() == \"#PCDATA\"");

        var seq = new ContentModel.Sequence(new ContentModel[] { new ContentModel.Name("a"), new ContentModel.Name("b") });
        Check(seq.ToString() == "(a, b)", $"ContentModel.Sequence.ToString(): '{seq}'");

        var choice = new ContentModel.Choice(new ContentModel[] { new ContentModel.Name("a"), new ContentModel.Name("b") });
        Check(choice.ToString() == "(a | b)", $"ContentModel.Choice.ToString(): '{choice}'");

        Check(new ContentModel.Repeat(new ContentModel.Name("a"), 0, 1).ToString() == "a?", "Repeat(0,1).ToString() == \"a?\"");
        Check(new ContentModel.Repeat(new ContentModel.Name("a"), 0, -1).ToString() == "a*", "Repeat(0,-1).ToString() == \"a*\"");
        Check(new ContentModel.Repeat(new ContentModel.Name("a"), 1, -1).ToString() == "a+", "Repeat(1,-1).ToString() == \"a+\"");
        // Кардинальность, которую ModelParser никогда не строит сам (нет DTD-синтаксиса под неё),
        // но тип это допускает — резервная ветка ToString() на случай ручного построения модели.
        Check(new ContentModel.Repeat(new ContentModel.Name("a"), 2, 5).ToString() == "a{2,5}",
            "Repeat с произвольной кардинальностью использует резервный формат {min,max}");
    }

    // ------------------------------------------------- property-based: модели

    /// <summary>Собственное (не catalog'а) дерево контент-модели для генерации случайных, но
    /// заведомо валидных DTD-выражений — используется как в этом тесте, так и в property-тесте
    /// DtdCatalogLoader, чтобы гонять один и тот же генератор через оба независимых пути.</summary>
    private abstract record ModelSpec;
    private sealed record LeafSpec(string Name) : ModelSpec;
    private sealed record SeqSpec(List<ModelSpec> Items) : ModelSpec;
    private sealed record ChoiceSpec(List<ModelSpec> Items) : ModelSpec;
    private sealed record RepeatSpec(ModelSpec Item, int Min, int Max) : ModelSpec;

    private static ModelSpec GenerateModelNode(Random rnd, string[] alphabet, int depth)
    {
        if (depth <= 0 || rnd.NextDouble() < 0.4)
        {
            return new LeafSpec(alphabet[rnd.Next(alphabet.Length)]);
        }

        var itemCount = 2 + rnd.Next(2); // 2..3
        var items = Enumerable.Range(0, itemCount)
            .Select(_ => GenerateModelNode(rnd, alphabet, depth - 1))
            .ToList();

        return rnd.Next(3) switch
        {
            0 => new SeqSpec(items),
            1 => new ChoiceSpec(items),
            _ => new RepeatSpec(items[0], rnd.Next(3) switch { 0 => 0, 1 => 0, _ => 1 }, rnd.Next(2) == 0 ? -1 : 1)
        };
    }

    private static string SerializeModelNode(ModelSpec spec) => spec switch
    {
        LeafSpec l => l.Name,
        SeqSpec s => "(" + string.Join(",", s.Items.Select(SerializeModelNode)) + ")",
        ChoiceSpec c => "(" + string.Join("|", c.Items.Select(SerializeModelNode)) + ")",
        RepeatSpec { Min: 0, Max: 1 } r => SerializeRepeatItem(r.Item) + "?",
        RepeatSpec { Min: 0 } r => SerializeRepeatItem(r.Item) + "*",
        RepeatSpec r => SerializeRepeatItem(r.Item) + "+",
        _ => throw new InvalidOperationException("неизвестный узел ModelSpec")
    };

    /// <summary>DTD не допускает два суффикса подряд на голом имени ("c++" не значит ничего и
    /// молча коверкает разбор) — если повторяемый элемент сам оказался Repeat, оборачиваем его в
    /// группу, как это и делают настоящие DTD: "(c+)*", а не "c+*".</summary>
    private static string SerializeRepeatItem(ModelSpec item) =>
        item is RepeatSpec ? "(" + SerializeModelNode(item) + ")" : SerializeModelNode(item);

    /// <summary>Строит одну заведомо валидную последовательность имён по той же логике, по которой
    /// ModelAutomaton интерпретирует Sequence/Choice/Repeat — если автомат не примет то, что
    /// сгенерировал этот метод, значит разошлось построение НКА или разбор синтаксиса.</summary>
    private static List<string> GenerateValidSequence(ModelSpec spec, Random rnd)
    {
        switch (spec)
        {
            case LeafSpec l:
                return new List<string> { l.Name };
            case SeqSpec s:
                return s.Items.SelectMany(item => GenerateValidSequence(item, rnd)).ToList();
            case ChoiceSpec c:
                return GenerateValidSequence(c.Items[rnd.Next(c.Items.Count)], rnd);
            case RepeatSpec r:
                var count = (r.Min, r.Max) switch
                {
                    (0, 1) => rnd.Next(2),
                    (0, -1) => rnd.Next(4),
                    _ => 1 + rnd.Next(3)
                };
                var result = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    result.AddRange(GenerateValidSequence(r.Item, rnd));
                }

                return result;
            default:
                throw new InvalidOperationException("неизвестный узел ModelSpec");
        }
    }

    internal static void ModelAutomatonPropertyTests()
    {
        Section("Property-based: ModelParser + ModelAutomaton (случайные контент-модели)");

        // Фиксированный сид — падение воспроизводимо без отдельного лога сида.
        var rnd = new Random(12345);
        var alphabet = new[] { "a", "b", "c", "d", "e" };
        const int iterations = 300;
        var failures = 0;

        for (var i = 0; i < iterations; i++)
        {
            try
            {
                var spec = GenerateModelNode(rnd, alphabet, depth: 3);
                var text = SerializeModelNode(spec);
                var model = ModelParser.Parse(text);
                var automaton = new ModelAutomaton(model);

                var validSequence = GenerateValidSequence(spec, rnd);
                if (!automaton.Validate(validSequence, out _, out _))
                {
                    failures++;
                    Failures.Add($"ModelAutomaton: сгенерированная валидная последовательность [{string.Join(",", validSequence)}] отклонена для модели '{text}' (итерация {i})");
                    continue;
                }

                // Автомат построен только на буквах алфавита — токен вне алфавита обязан рвать
                // любую последовательность, независимо от формы модели (ANY здесь не встречается).
                var withGarbage = validSequence.Append("unknown-token").ToList();
                if (automaton.Validate(withGarbage, out _, out _))
                {
                    failures++;
                    Failures.Add($"ModelAutomaton: последовательность с посторонним токеном [{string.Join(",", withGarbage)}] принята для модели '{text}' (итерация {i})");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Failures.Add($"ModelAutomaton: исключение на итерации {i}: {ex.Message}");
            }
        }

        Check(failures == 0,
            $"{iterations} случайных контент-моделей: валидная последовательность принимается, с посторонним токеном отклоняется ({iterations - failures}/{iterations})");
    }
}
