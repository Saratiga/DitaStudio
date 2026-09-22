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

/// <summary>
/// Проверки ядра без внешних библиотек: запускаются командой
/// <c>dotnet run --project tests/DitaStudio.Tests</c>.
/// </summary>
public static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        CatalogTests();
        DitaCatalogMiscTests();
        ContentModelTests();
        ContentModelMiscTests();
        ModelAutomatonPropertyTests();
        RoundTripTests();
        ValidationTests();
        DitaValidatorMiscTests();
        ValidationIssueTests();
        TemplateTests();
        EditingTests();
        EditCommandsExtraTests();
        UndoStackExtraTests();
        ProjectTests();
        KeyScopeTests();
        MultiProjectWorkspaceTests();
        ValidationAndAnchorFixTests();
        RelTableTests();
        MapTreeMiscTests();
        RevChangeTests();
        TrackChangesTests();
        XliffTests();
        XliffConverterPropertyTests();
        DtdAttributeListParserTests();
        DtdCatalogLoaderTests();
        DtdCatalogLoaderPropertyTests();
        PluginLoaderTests();
        DitaProjectValidateAllWithPluginsTests();
        DitavalFlagTests();
        DitavalPropertyTests();
        RefResolverTests();
        RefactorTests();
        ExtractToConrefTests();
        PdfExporterTests();
        DiffTests();
        GitHistoryTests();
        SvnHistoryTests();
        DocxTests();
        AdvancedRenderingTests();
        ImageSizeFormatsTests();
        HtmlRendererMiscTests();
        ListAndStepsDispatchTests();

        Console.WriteLine();
        Console.WriteLine($"Пройдено проверок: {_passed}");
        if (Failures.Count == 0)
        {
            Console.WriteLine("Все проверки пройдены.");
            return 0;
        }

        Console.WriteLine($"Не пройдено: {Failures.Count}");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  ✗ " + failure);
        }

        return 1;
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("  ✓ " + description);
        }
        else
        {
            Failures.Add(description);
            Console.WriteLine("  ✗ " + description);
        }
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    // ------------------------------------------------------------- каталог

    private static void CatalogTests()
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

    private static void DitaCatalogMiscTests()
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

    private static void ContentModelTests()
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

    private static void ContentModelMiscTests()
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

    private static void ModelAutomatonPropertyTests()
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

    // ------------------------------------------------------- разбор и запись

    private static void RoundTripTests()
    {
        Section("Разбор и запись XML");

        const string xml = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE task PUBLIC "-//OASIS//DTD DITA Task//EN" "task.dtd">
<task id="install" xml:lang="ru-RU">
  <title>Установка</title>
  <shortdesc>Как установить <keyword>Продукт</keyword> на сервер.</shortdesc>
  <taskbody>
    <steps>
      <step><cmd>Откройте <uicontrol>Параметры</uicontrol> и нажмите <b>Далее</b>.</cmd></step>
    </steps>
  </taskbody>
</task>
""";

        var document = DitaDocument.Parse(xml);
        Check(document.Root.Name == "task", "корневой элемент разобран");
        Check(document.Id == "install", "атрибут @id прочитан");
        Check(document.Title == "Установка", "заголовок получен");
        Check(document.DoctypePublicId == "-//OASIS//DTD DITA Task//EN", "DOCTYPE сохранён");

        var cmd = document.Root.FindDescendant("cmd")!;
        Check(cmd.InnerText.Contains("Откройте") && cmd.InnerText.Contains("Далее"),
            "смешанное содержимое сохранило текст");
        Check(cmd.ElementChildren().Count() == 2, "внутри cmd два фразовых элемента");

        var serialized = document.ToXmlString();
        var reparsed = DitaDocument.Parse(serialized);
        Check(reparsed.Root.FindDescendant("cmd")!.InnerText == cmd.InnerText,
            "текст не изменился после повторного разбора");
        Check(serialized.Contains("<uicontrol>Параметры</uicontrol>"),
            "фразовые элементы записаны в одну строку");
        Check(serialized.Contains("<!DOCTYPE task PUBLIC"), "DOCTYPE записан обратно");

        var escaped = DitaDocument.Parse("<topic id=\"t\"><title>a &amp; b &lt; c</title></topic>");
        Check(escaped.Root.FirstElement("title")!.InnerText == "a & b < c", "сущности раскрыты при чтении");
        Check(escaped.ToXmlString().Contains("a &amp; b &lt; c"), "спецсимволы экранированы при записи");
    }

    // ------------------------------------------------------------- проверка

    private static void ValidationTests()
    {
        Section("Валидация");
        var validator = new DitaValidator { CheckStyleRules = false };

        var good = DitaDocument.Parse(
            "<concept id=\"c1\"><title>Заголовок</title><conbody><p>Текст</p></conbody></concept>");
        Check(validator.Validate(good).Count == 0, "корректный concept проходит проверку");

        var wrongOrder = DitaDocument.Parse(
            "<concept id=\"c1\"><conbody><p>Текст</p></conbody><title>Заголовок</title></concept>");
        Check(validator.Validate(wrongOrder).Any(i => i.Severity == IssueSeverity.Error),
            "нарушенный порядок элементов найден");

        var unknown = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><paragraph>Текст</paragraph></conbody></concept>");
        Check(validator.Validate(unknown).Any(i => i.Message.Contains("paragraph")),
            "неизвестный элемент найден");

        var badEnum = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><note type=\"неизвестно\">Текст</note></conbody></concept>");
        Check(validator.Validate(badEnum).Any(i => i.Message.Contains("@type")),
            "недопустимое значение перечисления найдено");

        var duplicateIds = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p id=\"x\">A</p><p id=\"x\">B</p></conbody></concept>");
        Check(duplicateIds is not null && validator.Validate(duplicateIds).Any(i => i.Message.Contains("уже используется")),
            "повторяющийся @id найден");

        var textInContainer = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody>Просто текст</conbody></concept>");
        Check(validator.Validate(textInContainer).Any(i => i.Message.Contains("не может содержать текст")),
            "текст в блочном контейнере найден");

        var conrefSkipped = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p conref=\"other.dita#t/p1\"/></conbody></concept>");
        Check(validator.Validate(conrefSkipped).Count == 0, "элемент с conref не проверяется по модели");
    }

    private static void DitaValidatorMiscTests()
    {
        Section("DitaValidator: атрибуты/EMPTY/незакрытая модель/стилевые правила (по отчёту покрытия)");

        var structural = new DitaValidator { CheckStyleRules = false };

        var unusualRoot = DitaDocument.Parse("<p>Просто абзац как корень.</p>");
        Check(structural.Validate(unusualRoot).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("обычно не используется как корень")),
            "известный, но не topic/map элемент в корне — предупреждение, не ошибка");

        var undeclaredAttr = DitaDocument.Parse("<concept id=\"c\" совершенно-незнакомый-атрибут=\"x\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(structural.Validate(undeclaredAttr).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("не объявлен")),
            "необъявленный атрибут элемента — предупреждение");

        var xmlnsIgnored = DitaDocument.Parse("<concept id=\"c\" xmlns:ditaarch=\"http://dita.oasis-open.org/architecture/2005/\" ditaarch:DITAArchVersion=\"1.3\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(!structural.Validate(xmlnsIgnored).Any(i => i.Message.Contains("не объявлен")),
            "xmlns:*/ditaarch:* атрибуты не считаются необъявленными (пропускаются явно)");

        var missingRequiredAttr = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст <abbreviated-form/> текст</p></conbody></concept>");
        Check(structural.Validate(missingRequiredAttr).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("обязательный атрибут @keyref")),
            "отсутствующий обязательный атрибут (keyref! у abbreviated-form) — ошибка");

        var emptyWithChildren = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст <abbreviated-form keyref=\"k\"><b>x</b></abbreviated-form></p></conbody></concept>");
        Check(structural.Validate(emptyWithChildren).Any(i => i.Message.Contains("должен быть пустым")),
            "содержимое у элемента с моделью EMPTY — ошибка");

        var incompleteContent = DitaDocument.Parse("<task id=\"t\"><title>T</title><taskbody><steps><step></step></steps></taskbody></task>");
        Check(structural.Validate(incompleteContent).Any(i => i.Message.Contains("неполное") && i.Message.Contains("cmd")),
            "пустой <step/> (нужен обязательный cmd) — 'содержимое неполное', а не 'недопустим в этой позиции'");

        // --- стилевые правила (CheckStyleRules по умолчанию true — во всех остальных тестах их
        // намеренно выключают, поэтому здесь единственное прямое покрытие ValidateStyle/*).
        var styled = new DitaValidator();

        var noId = DitaDocument.Parse("<concept><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(styled.Validate(noId).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("нет атрибута @id")),
            "топик без @id — предупреждение стиля");

        var glossTitleFallback = DitaDocument.Parse("<glossentry id=\"g\"><glossterm></glossterm><glossdef>Определение.</glossdef></glossentry>");
        Check(styled.Validate(glossTitleFallback).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("Пустой заголовок")),
            "glossentry без текста в glossterm — пустой заголовок (тот же путь, что title)");

        var withAbstractNoShortdesc = DitaDocument.Parse("<concept id=\"c\"><title>T</title><abstract><p>Реферат.</p></abstract><conbody><p>Текст</p></conbody></concept>");
        Check(!styled.Validate(withAbstractNoShortdesc).Any(i => i.Message.Contains("shortdesc")),
            "abstract присутствует — предупреждение про отсутствие shortdesc не выдаётся, даже если самого shortdesc нет");

        var noShortdescNoAbstract = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(styled.Validate(noShortdescNoAbstract).Any(i => i.Severity == IssueSeverity.Info && i.Message.Contains("shortdesc")),
            "ни shortdesc, ни abstract — информационная подсказка");

        var mapRootSkipsTopicStyle = DitaDocument.Parse("""
<map>
  <title>Карта</title>
  <topicref href="x.dita"><linktext></linktext></topicref>
</map>
""");
        var mapIssues = styled.Validate(mapRootSkipsTopicStyle);
        Check(!mapIssues.Any(i => i.Message.Contains("Пустой заголовок топика") || i.Message.Contains("shortdesc")),
            "карта (не topic-тип) — ValidateTopicStyle не запускается вовсе");

        var emptyStyleElements = DitaDocument.Parse("""
<concept id="c">
  <title>T</title>
  <conbody>
    <p></p>
    <table><tgroup cols="1"><tbody><row><entry></entry></row></tbody></tgroup></table>
  </conbody>
</concept>
""");
        var emptyIssues = styled.Validate(emptyStyleElements);
        Check(emptyIssues.Count(i => i.Message.Contains("Пустой элемент")) == 2,
            $"пустые p и entry — по одному предупреждению на каждый: {emptyIssues.Count(i => i.Message.Contains("Пустой элемент"))}");

        var imageNoRefNoAlt = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image/></p></conbody></concept>");
        var imgIssues = styled.Validate(imageNoRefNoAlt);
        Check(imgIssues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("ни @href, ни @keyref")),
            "image без href и keyref — ошибка");
        Check(imgIssues.Any(i => i.Severity == IssueSeverity.Info && i.Message.Contains("альтернативного текста")),
            "image без alt (и без href/keyref) — тоже отдельная информационная подсказка");

        var imageWithAltAttr = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"x.png\" alt=\"описание\"/></p></conbody></concept>");
        Check(!styled.Validate(imageWithAltAttr).Any(i => i.Message.Contains("альтернативного текста")),
            "image с @alt — подсказки про alt нет");

        var imageWithAltChild = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"x.png\"><alt>описание</alt></image></p></conbody></concept>");
        Check(!styled.Validate(imageWithAltChild).Any(i => i.Message.Contains("альтернативного текста")),
            "image с дочерним <alt> тоже гасит подсказку (не только атрибут)");
    }

    private static void ValidationIssueTests()
    {
        Section("ValidationIssue: SeverityText/Location/ToString (по отчёту покрытия)");

        var doc = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        var node = doc.Root.FindDescendant("p")!;
        node.Line = 7;

        var error = new ValidationIssue(IssueSeverity.Error, "ошибка", node, "file.dita");
        Check(error.SeverityText == "Ошибка", "SeverityText для Error");
        Check(error.Line == 7, "Line берётся из узла");
        Check(error.Location == node.Path, "Location берётся из Path узла");
        Check(error.ToString() == $"Ошибка: ошибка ({node.Path}, строка 7)", $"ToString() с узлом и строкой: '{error}'");

        var warning = new ValidationIssue(IssueSeverity.Warning, "предупреждение", node);
        Check(warning.SeverityText == "Предупреждение", "SeverityText для Warning");
        Check(warning.FilePath is null, "FilePath не задан по умолчанию");

        var info = new ValidationIssue(IssueSeverity.Info, "инфо", null);
        Check(info.SeverityText == "Сведения", "SeverityText для Info (значение по умолчанию switch)");
        Check(info.Line == 0 && info.Location == string.Empty, "Node == null — Line и Location по умолчанию");
        Check(info.ToString() == "Сведения: инфо ()", $"ToString() без узла и без строки не добавляет ', строка N': '{info}'");
    }

    private static void TemplateTests()
    {
        Section("Заготовки документов");
        var validator = new DitaValidator { CheckStyleRules = false };

        foreach (var template in DocumentTemplates.All)
        {
            var document = DocumentTemplates.Create(template.Key, "Проверка");
            var issues = validator.Validate(document);
            Check(issues.Count == 0,
                $"заготовка «{template.DisplayName}» валидна" +
                (issues.Count == 0 ? string.Empty : ": " + issues[0].Message));
        }

        Check(DocumentTemplates.SuggestId("Установка сервера", "task") == "ustanovka_servera",
            "идентификатор транслитерируется из русского заголовка");

        Check(DocumentTemplates.Find("TASK") is { Key: "task" }, "Find регистронезависим");
        Check(DocumentTemplates.Find("no-such-template") is null, "Find возвращает null для неизвестного ключа");

        var fallback = DocumentTemplates.Create("no-such-template", "Заголовок");
        Check(fallback.Root.Name == DocumentTemplates.All[0].RootElement,
            $"Create с неизвестным ключом использует первую заготовку из All: {fallback.Root.Name}");

        var topic = DocumentTemplates.Create("topic", "Заголовок универсального топика");
        Check(topic.Root.Name == "topic", "Create('topic') идёт по ветке default switch (Topic())");

        var t1 = DocumentTemplates.All[0];
        var t2 = t1 with { };
        Check(t1 == t2 && t1.Equals(t2), "DocumentTemplate — record со структурным равенством");
        Check(t1.ToString() == t1.DisplayName, "DocumentTemplate.ToString() возвращает DisplayName");

        Check(DocumentTemplates.SuggestId("...", "topic") == "topic",
            "SuggestId: заголовок без букв/цифр целиком — используется префикс");
        Check(DocumentTemplates.SuggestId("123 сервер", "topic") == "topic_123_server",
            $"SuggestId: результат, начинающийся с цифры, получает префикс спереди: {DocumentTemplates.SuggestId("123 сервер", "topic")}");
        Check(DocumentTemplates.SuggestId("a  --  b", "topic") == "a_b",
            $"SuggestId: подряд идущие пробелы/дефисы схлопываются в один '_': {DocumentTemplates.SuggestId("a  --  b", "topic")}");
        Check(DocumentTemplates.SuggestId("中文 текст", "topic") == "tekst",
            $"SuggestId: символы вне транслит-таблицы и вне ASCII молча пропускаются (вместе с последующим пробелом, раз до него ничего не накопилось): {DocumentTemplates.SuggestId("中文 текст", "topic")}");
        var longTitle = string.Concat(Enumerable.Repeat("word ", 20));
        Check(DocumentTemplates.SuggestId(longTitle, "topic").Length <= 60,
            $"SuggestId обрезает результат до 60 символов: {DocumentTemplates.SuggestId(longTitle, "topic").Length}");
    }

    private static void EditingTests()
    {
        Section("Редактирование");

        var document = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p>Первый абзац</p></conbody></concept>");
        var conbody = document.Root.FirstElement("conbody")!;
        var paragraph = conbody.FirstElement("p")!;

        var second = EditCommands.SplitBlock(paragraph, 6);
        Check(second is not null && paragraph.InnerText == "Первый" && second.InnerText == " абзац",
            "разделение абзаца по позиции курсора");

        var merged = EditCommands.MergeWithPrevious(second!);
        Check(merged is not null && merged.InnerText == "Первый абзац", "объединение абзацев");

        var inserted = EditCommands.InsertAfter(paragraph, "ul");
        Check(inserted is not null && inserted.Name == "ul" && inserted.FirstElement("li") is not null,
            "вставка списка с обязательным li");

        Check(EditCommands.InsertAfter(paragraph, "title") is null,
            "недопустимый элемент не вставляется");

        var textNode = paragraph.Children.First(c => c.Kind == NodeKind.Text);
        var wrapped = EditCommands.WrapTextRange(textNode, 0, 6, "b");
        Check(wrapped is not null && wrapped.InnerText == "Первый" && paragraph.InnerText == "Первый абзац",
            "оформление части текста фразовым элементом");

        var undo = new UndoStack();
        undo.Push(document, "проверка");
        conbody.RemoveSelf();
        Check(document.Root.FirstElement("conbody") is null, "элемент удалён");
        undo.Undo(document);
        Check(document.Root.FirstElement("conbody") is not null, "отмена вернула элемент");

        var title = document.Root.FirstElement("title")!;
        var enabled = EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(enabled && title.GetAttribute("outputclass") == "page-break-before",
            "класс вывода добавлен на заголовок");
        var disabled = EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(!disabled && title.GetAttribute("outputclass") is null,
            "повторное переключение снимает класс вывода");

        EditCommands.ToggleOutputClassToken(title, "existing");
        EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(title.GetAttribute("outputclass") == "existing page-break-before",
            "класс вывода добавляется рядом с уже существующим, не заменяя его");

        TableMergeTests();
    }

    private static void TableMergeTests()
    {
        var tableDoc = DitaDocument.Parse("""
<reference id="r1"><title>Р</title><refbody><table><tgroup cols="3">
<colspec colname="c1" colnum="1"/><colspec colname="c2" colnum="2"/><colspec colname="c3" colnum="3"/>
<tbody>
<row><entry>A1</entry><entry>B1</entry><entry>C1</entry></row>
<row><entry>A2</entry><entry>B2</entry><entry>C2</entry></row>
</tbody>
</tgroup></table></refbody></reference>
""");
        var rows = tableDoc.Root.FindDescendant("tbody")!.ElementChildren().Where(r => r.Name == "row").ToList();
        var row1Entries = rows[0].ElementChildren().Where(e => e.Name == "entry").ToList();

        var mergedRight = EditCommands.MergeTableCellRight(row1Entries[0]);
        Check(mergedRight is not null && mergedRight.GetAttribute("namest") == "c1" && mergedRight.GetAttribute("nameend") == "c2",
            "объединение ячеек по горизонтали проставляет namest/nameend");
        Check(rows[0].ElementChildren().Count(e => e.Name == "entry") == 2, "соседняя ячейка справа удалена после объединения");
        Check(mergedRight!.InnerText == "A1 B1", "содержимое объединённых ячеек сохранено");

        var row2Entries = rows[1].ElementChildren().Where(e => e.Name == "entry").ToList();
        var thirdColumnEntry = row2Entries.Single(e => e.InnerText == "C2");
        var mergedDown = EditCommands.MergeTableCellDown(row1Entries[2]);
        Check(mergedDown is not null && mergedDown.GetAttribute("morerows") == "1",
            "объединение ячеек по вертикали проставляет morerows");
        Check(rows[1].ElementChildren().Count(e => e.Name == "entry") == 2,
            "поглощённая нижняя ячейка удалена из строки");
        Check(mergedDown!.InnerText == "C1 C2", "содержимое объединённой по вертикали ячейки сохранено");
        _ = thirdColumnEntry;

        // Строку нельзя опустошить целиком объединением по вертикали.
        var lastRowSingleEntry = DitaDocument.Parse("""
<reference id="r2"><title>Р</title><refbody><table><tgroup cols="1">
<colspec colname="c1" colnum="1"/>
<tbody><row><entry>X1</entry></row><row><entry>X2</entry></row></tbody>
</tgroup></table></refbody></reference>
""");
        var soleRows = lastRowSingleEntry.Root.FindDescendant("tbody")!.ElementChildren().Where(r => r.Name == "row").ToList();
        var soleEntry = soleRows[0].FirstElement("entry")!;
        Check(EditCommands.MergeTableCellDown(soleEntry) is null,
            "объединение по вертикали не опустошает строку целиком");
    }

    private static void EditCommandsExtraTests()
    {
        Section("EditCommands: остальные операции (по отчёту покрытия)");

        var doc = DitaDocument.Parse("""
<concept id="c1"><title>T</title><conbody>
  <p id="p1">Первый</p>
  <p id="p2">Второй</p>
  <p id="p3">Третий</p>
</conbody></concept>
""");
        var conbody = doc.Root.FirstElement("conbody")!;
        var ps = () => conbody.ElementChildren().Where(e => e.Name == "p").ToList();
        var p1 = ps()[0];
        var p2 = ps()[1];
        var p3 = ps()[2];

        var beforeP2 = EditCommands.InsertBefore(p2, "p");
        Check(beforeP2 is not null && ps().IndexOf(beforeP2) == 1, "InsertBefore вставляет непосредственно перед узлом");
        beforeP2!.RemoveSelf();

        var appended = EditCommands.Append(conbody, "p");
        Check(appended is not null && ReferenceEquals(ps()[^1], appended), "Append вставляет в конец родителя");
        appended!.RemoveSelf();

        var wrapped = EditCommands.Wrap(conbody, 0, 1, "note");
        Check(wrapped is not null && wrapped.Name == "note", "Wrap создаёт обёртку заданного имени");
        Check(wrapped!.ElementChildren().Select(e => e.Name == "p" ? e.InnerText : e.Name).SequenceEqual(new[] { "Первый", "Второй" }),
            "Wrap переносит внутрь обёртки именно указанный диапазон детей, в исходном порядке");
        Check(conbody.ElementChildren().Count() == 2 && ReferenceEquals(conbody.ElementChildren().First(), wrapped),
            "обёрнутые дети убраны из родителя, на их месте — обёртка");

        Check(EditCommands.Unwrap(wrapped) && conbody.ElementChildren().Count() == 3,
            "Unwrap возвращает детей обёртки на её место и убирает саму обёртку");
        Check(ps().Select(p => p.InnerText).SequenceEqual(new[] { "Первый", "Второй", "Третий" }),
            "после Wrap→Unwrap порядок и содержимое узлов не изменились");

        Check(EditCommands.Wrap(conbody, 1, 0, "note") is null, "Wrap отклоняет диапазон с first > last");
        Check(EditCommands.Wrap(conbody, 0, 99, "note") is null, "Wrap отклоняет диапазон за пределами числа детей");

        Check(!EditCommands.Unwrap(doc.Root), "Unwrap корневого узла (нет родителя) возвращает false");

        Check(EditCommands.MoveDown(p1) && ps()[0] == p2 && ps()[1] == p1, "MoveDown меняет местами с следующим элементом");
        Check(EditCommands.MoveUp(p1) && ps()[0] == p1 && ps()[1] == p2, "MoveUp возвращает элемент обратно наверх");
        Check(!EditCommands.MoveUp(p1), "MoveUp первого элемента возвращает false");
        Check(!EditCommands.MoveDown(p3), "MoveDown последнего элемента возвращает false");

        p1.SetAttribute("totally-fake-attr", "x");
        Check(EditCommands.ChangeElementName(p1, "note"), "ChangeElementName переименовывает известный элемент");
        Check(p1.Name == "note", "имя узла обновлено");
        Check(p1.GetAttribute("totally-fake-attr") is null,
            "атрибут, недопустимый для нового имени элемента, снят при переименовании");
        Check(!EditCommands.ChangeElementName(p1, "no-such-element-xyz"), "ChangeElementName отклоняет неизвестное целевое имя");
        Check(!EditCommands.ChangeElementName(doc.Root, "task"), "ChangeElementName корневого узла (нет родителя) возвращает false");

        Check(EditCommands.MergeWithPrevious(p2) is null, "MergeWithPrevious не объединяет узлы разного имени (сосед p1 теперь note)");
        Check(!EditCommands.Delete(doc.Root), "Delete корневого узла (нет родителя) возвращает false");
        Check(EditCommands.Delete(p3) && conbody.ElementChildren().Count() == 2, "Delete убирает узел из дерева");

        var id1 = EditCommands.GenerateId(doc, "fig");
        Check(id1 == "fig_1", $"GenerateId выдаёт первый свободный номер: {id1}");
        doc.Root.FirstElement("title")!.SetAttribute("id", "fig_1");
        var id2 = EditCommands.GenerateId(doc, "fig");
        Check(id2 == "fig_2", $"GenerateId пропускает уже занятый id: {id2}");

        // --- механические защитные проверки MergeTableCellRight/Down на входах не по форме
        var notEntry = DitaDocument.Parse("<reference id=\"r\"><title>Р</title><refbody><p>Абзац</p></refbody></reference>")
            .Root.FindDescendant("p")!;
        Check(EditCommands.MergeTableCellRight(notEntry) is null, "MergeTableCellRight отклоняет узел, который не является entry в row");
        Check(EditCommands.MergeTableCellDown(notEntry) is null, "MergeTableCellDown отклоняет узел, который не является entry в row");

        var singleEntryRow = DitaDocument.Parse("""
<reference id="r3"><title>Р</title><refbody><table><tgroup cols="2">
<colspec colname="c1" colnum="1"/><colspec colname="c2" colnum="2"/>
<tbody><row><entry>only</entry></row></tbody>
</tgroup></table></refbody></reference>
""");
        var lastInRow = singleEntryRow.Root.FindDescendant("row")!.FirstElement("entry")!;
        Check(EditCommands.MergeTableCellRight(lastInRow) is null, "MergeTableCellRight отклоняет последнюю ячейку строки (нет соседа справа)");
        Check(EditCommands.MergeTableCellDown(lastInRow) is null, "MergeTableCellDown отклоняет ячейку, если строки снизу нет");

        var detachedRow = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><row><entry>X</entry><entry>Y</entry></row></conbody></concept>");
        var detachedEntry = detachedRow.Root.FindDescendant("row")!.FirstElement("entry")!;
        Check(EditCommands.MergeTableCellRight(detachedEntry) is null, "MergeTableCellRight отклоняет entry вне tgroup");
    }

    private static void UndoStackExtraTests()
    {
        Section("UndoStack: Redo, лимит, событие Changed (по отчёту покрытия)");

        var document = DitaDocument.Parse("<concept id=\"c1\"><title>T</title><conbody><p>A</p></conbody></concept>");
        var undo = new UndoStack(limit: 3);
        var changedCount = 0;
        undo.Changed += (_, _) => changedCount++;

        Check(!undo.CanUndo && !undo.CanRedo, "новый UndoStack пуст");
        Check(!undo.Undo(document), "Undo на пустом стеке возвращает false");
        Check(!undo.Redo(document), "Redo на пустом стеке возвращает false");
        Check(changedCount == 0, "Undo/Redo впустую не генерируют событие Changed");

        undo.Push(document, "шаг 1");
        document.Root.FirstElement("conbody")!.FirstElement("p")!.SetText("B");
        Check(undo.CanUndo && undo.NextUndoDescription == "шаг 1", "Push запоминает описание снимка");
        Check(changedCount == 1, "Push генерирует событие Changed");

        undo.Push(document, "шаг 2");
        document.Root.FirstElement("conbody")!.FirstElement("p")!.SetText("C");
        Check(undo.NextUndoDescription == "шаг 2", "второй Push — следующий кандидат на отмену");

        Check(undo.Undo(document) && document.Root.FirstElement("conbody")!.FirstElement("p")!.InnerText == "B",
            "Undo восстанавливает состояние до шага 2");
        Check(undo.CanRedo && undo.NextRedoDescription == "шаг 2", "после Undo появляется кандидат на Redo с тем же описанием");

        Check(undo.Redo(document) && document.Root.FirstElement("conbody")!.FirstElement("p")!.InnerText == "C",
            "Redo возвращает состояние вперёд");
        Check(!undo.CanRedo, "после Redo стек повтора снова пуст");

        undo.Undo(document);
        undo.Push(document, "шаг 2b — новая ветка после отмены");
        Check(!undo.CanRedo, "новый Push после Undo очищает стек Redo (старая ветка истории отброшена)");

        undo.Clear();
        Check(!undo.CanUndo && !undo.CanRedo, "Clear опустошает оба стека");

        // Лимит истории: limit=3, пятая запись должна вытеснить самую старую.
        var limited = new UndoStack(limit: 3);
        for (var i = 1; i <= 5; i++)
        {
            limited.Push(document, $"запись {i}");
        }

        var descriptions = new List<string>();
        while (limited.CanUndo)
        {
            descriptions.Add(limited.NextUndoDescription!);
            limited.Undo(document);
        }

        Check(descriptions.Count == 3, $"история обрезана по лимиту: {descriptions.Count} записей вместо 5");
        Check(descriptions[0] == "запись 5" && descriptions[^1] == "запись 3",
            $"вытеснены самые старые записи, остались последние {string.Join(",", descriptions)}");
    }

    // -------------------------------------------------------------- проект

    private static void ProjectTests()
    {
        Section("Проект и публикация");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="intro">
  <title>Введение</title>
  <shortdesc>Коротко о продукте.</shortdesc>
  <conbody>
    <p id="reusable">Общий фрагмент.</p>
    <p>Смотрите <xref href="install.dita#install">установку</xref>.
      Термин<indexterm>Ключевое слово<indexterm>Подраздел</indexterm></indexterm>.</p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "install.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="install">
  <title>Установка</title>
  <taskbody>
    <context><p conref="intro.dita#intro/reusable"/></context>
    <steps>
      <step><cmd>Запустите <uicontrol keyref="setup-button"/>.</cmd></step>
      <step><cmd>Нажмите <b>Готово</b>.</cmd></step>
    </steps>
  </taskbody>
</task>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Руководство</title>
  <keydef keys="setup-button">
    <topicmeta><keywords><keyword>Установить</keyword></keywords></topicmeta>
  </keydef>
  <topicref href="intro.dita">
    <topicref href="install.dita"/>
  </topicref>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            Check(project.Files.Count == 3, $"проект нашёл файлы: {project.Files.Count}");
            Check(project.Maps.Count() == 1, "карта распознана");
            Check(project.Topics.Count() == 2, "топики распознаны");
            Check(project.ResolveKey("setup-button")?.KeyText == "Установить", "ключ разрешается в текст");

            var tree = MapTree.Build(project, Path.Combine(root, "guide.ditamap"));
            var order = tree.PublicationOrder.ToList();
            Check(order.Count == 2, $"в публикацию попало топиков: {order.Count}");
            Check(order[0].Title == "Введение", "первый топик карты — Введение");
            Check(order[1].Level == 2, "вложенность топика определена");

            var installDoc = project.GetDocument(Path.Combine(root, "install.dita"));
            var expanded = RefResolver.ExpandConrefs(project, installDoc);
            Check(expanded.Root.FindDescendant("context")!.InnerText.Contains("Общий фрагмент"),
                "conref раскрыт при публикации");
            Check(installDoc.Root.FindDescendant("context")!.InnerText.Trim().Length == 0,
                "исходный документ не изменился при раскрытии conref");

            var issues = project.ValidateAll();
            Check(!issues.Any(i => i.Severity == IssueSeverity.Error),
                "в тестовом проекте нет ошибок: " + string.Join("; ", issues
                    .Where(i => i.Severity == IssueSeverity.Error)
                    .Select(i => i.Message)));

            var outputDirectory = Path.Combine(root, "out");
            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions
            {
                OutputDirectory = outputDirectory
            });

            Check(File.Exists(result.EntryFile), "публикация создала входной файл");
            var indexHtml = File.ReadAllText(result.EntryFile);
            Check(indexHtml.Contains("Руководство"), "заголовок карты попал в публикацию");

            var installHtml = File.ReadAllText(Path.Combine(outputDirectory, "install.html"));
            Check(installHtml.Contains("Порядок действий"), "сгенерирована подпись раздела шагов");
            Check(installHtml.Contains("<ol class=\"steps\""), "шаги оформлены нумерованным списком");
            Check(installHtml.Contains("Общий фрагмент"), "conref попал в HTML");
            Check(installHtml.Contains(">Установить<"), "keyref заменён текстом ключа");
            Check(installHtml.Contains("<strong>Готово</strong>"), "полужирный перенесён в HTML");

            var introHtml = File.ReadAllText(Path.Combine(outputDirectory, "intro.html"));
            Check(introHtml.Contains("href=\"install.html#install\""), "перекрёстная ссылка ведёт на страницу топика");
            Check(introHtml.Contains("class=\"shortdesc\""), "краткое описание оформлено");

            var single = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-single"),
                SingleFile = true
            });
            Check(File.Exists(single.EntryFile), "сборка в один файл выполнена");
            var singleHtml = File.ReadAllText(single.EntryFile);
            Check(singleHtml.Contains("Введение") && singleHtml.Contains("Установка"),
                "оба топика вошли в единый файл");

            // Указатель: термин с подпунктом собирается в конце публикации, ссылка ведёт на
            // реальный id топика (а не на "intro--install", которого нет в разметке).
            Check(singleHtml.Contains("class=\"index-terms\""), "секция указателя добавлена в публикацию");
            Check(singleHtml.Contains("Ключевое слово") && singleHtml.Contains("Подраздел"),
                "термин и подпункт указателя попали в публикацию");
            var indexHref = System.Text.RegularExpressions.Regex.Match(singleHtml, "Ключевое слово ?<a href=\"#([^\"]+)\"").Groups[1].Value;
            Check(indexHref.Length > 0 && singleHtml.Contains($"id=\"{indexHref}\""),
                $"ссылка указателя ведёт на существующий id: {indexHref}");

            // Тот же самый баг ломал обычные xref с "#id", повторяющим id корня топика.
            var xrefHref = System.Text.RegularExpressions.Regex.Match(singleHtml, "установку</a>").Success
                ? System.Text.RegularExpressions.Regex.Match(singleHtml, "href=\"#([^\"]+)\">установку</a>").Groups[1].Value
                : string.Empty;
            Check(xrefHref.Length > 0 && singleHtml.Contains($"id=\"{xrefHref}\""),
                $"перекрёстная ссылка с #id топика ведёт на существующий id: {xrefHref}");

            var filtered = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-filtered"),
                SingleFile = true,
                ExcludeConditions = { ["audience"] = new HashSet<string> { "expert" } }
            });
            Check(File.Exists(filtered.EntryFile), "сборка с условиями выполняется");

            // Разрыв страницы перед заголовком — через outputclass, ставится в редакторе,
            // проверяем, что доходит до опубликованного HTML.
            var introPath = Path.Combine(root, "intro.dita");
            var introDoc = project.GetDocument(introPath);
            EditCommands.ToggleOutputClassToken(introDoc.Root.FirstElement("title")!, "page-break-before");
            introDoc.Save(introPath);
            project.Scan();

            var withBreak = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-break"),
                SingleFile = true
            });
            var breakHtml = File.ReadAllText(withBreak.EntryFile);
            Check(breakHtml.Contains("class=\"page-break-before\""),
                "класс разрыва страницы перед заголовком попал в публикацию");

            // Пользовательский CSS: подключение, сохранение между запусками, попадание в вывод.
            var cssPath = Path.Combine(root, "custom.css");
            File.WriteAllText(cssPath, "h1.custom-marker { color: red; }");
            project.SetCustomCssPath("custom.css");
            Check(project.CustomCssPath == "custom.css", "путь к пользовательскому CSS сохранён в проекте");

            var reopened = new DitaProject(root);
            Check(reopened.CustomCssPath == "custom.css", "путь к пользовательскому CSS переживает переоткрытие проекта");

            var withCss = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-css"),
                SingleFile = true
            });
            var cssHtml = File.ReadAllText(withCss.EntryFile);
            Check(cssHtml.Contains("h1.custom-marker { color: red; }"), "пользовательский CSS подключён к публикации");

            // RenderPreview: extraCss (режим предпросмотра "как в DOCX" у DocumentPane) довешивается
            // ПОСЛЕ пользовательского CSS проекта — значит побеждает его в каскаде.
            var introDocForPreview = project.GetDocument(Path.Combine(root, "intro.dita"));
            var previewWithExtraCss = publisher.RenderPreview(introDocForPreview, extraCss: "body { font-family: TestFont; }");
            Check(previewWithExtraCss.Contains("h1.custom-marker { color: red; }"),
                "RenderPreview: пользовательский CSS проекта тоже попадает в предпросмотр");
            Check(previewWithExtraCss.Contains("body { font-family: TestFont; }"),
                "RenderPreview: extraCss добавляется к предпросмотру");
            Check(previewWithExtraCss.IndexOf("h1.custom-marker", StringComparison.Ordinal) <
                  previewWithExtraCss.IndexOf("TestFont", StringComparison.Ordinal),
                "extraCss идёт после пользовательского CSS в каскаде — значит побеждает его");

            project.SetCustomCssPath(null);
            var previewWithoutCustomCss = publisher.RenderPreview(introDocForPreview, extraCss: "body { font-family: TestFont; }");
            Check(previewWithoutCustomCss.Contains("body { font-family: TestFont; }") && !previewWithoutCustomCss.Contains("h1.custom-marker"),
                "extraCss работает и без пользовательского CSS проекта");
            Check(project.CustomCssPath is null, "пользовательский CSS можно отключить");

            // Условия сборки: сохранение между запусками и отключение.
            project.SetConditions(new Dictionary<string, HashSet<string>>
            {
                ["platform"] = new HashSet<string> { "windows", "linux" }
            }, showDraftComments: true);
            Check(project.ExcludedConditionValues["platform"].SetEquals(new[] { "windows", "linux" }),
                "исключённые значения условий сохранены в проекте");
            Check(project.ShowDraftComments, "флаг показа черновых комментариев сохранён в проекте");

            var reopenedConditions = new DitaProject(root);
            Check(reopenedConditions.ExcludedConditionValues["platform"].SetEquals(new[] { "windows", "linux" }),
                "условия сборки переживают переоткрытие проекта");
            Check(reopenedConditions.ShowDraftComments, "флаг черновых комментариев переживает переоткрытие проекта");

            project.SetConditions(new Dictionary<string, HashSet<string>>(), showDraftComments: false);
            Check(project.ExcludedConditionValues.Count == 0, "условия сборки можно сбросить");
            Check(!new DitaProject(root).ShowDraftComments, "сброс условий сборки сохраняется на диске");

            // Колонтитулы PDF: сохранение между запусками и отключение.
            project.SetPdfHeaderFooter(true, "Заголовок документа", "© 2026");
            Check(project.PdfShowHeaderFooter, "флаг колонтитулов сохранён в проекте");
            Check(project.PdfHeaderText == "Заголовок документа", "текст шапки сохранён в проекте");

            var reopenedHeaderFooter = new DitaProject(root);
            Check(reopenedHeaderFooter.PdfShowHeaderFooter, "колонтитулы переживают переоткрытие проекта");
            Check(reopenedHeaderFooter.PdfFooterText == "© 2026", "текст подвала переживает переоткрытие проекта");

            project.SetPdfHeaderFooter(false, null, null);
            Check(!new DitaProject(root).PdfShowHeaderFooter, "отключение колонтитулов сохраняется на диске");

            // Поиск: обычный текст и регулярное выражение.
            var plainHits = project.Search("продукте");
            Check(plainHits.Count == 1, $"обычный поиск нашёл совпадений: {plainHits.Count}");

            var regexHits = project.Search(@"прод\w+", regex: true);
            Check(regexHits.Count == 1, $"поиск по regex нашёл совпадений: {regexHits.Count}");

            var badRegexHits = project.Search("[", regex: true);
            Check(badRegexHits.Count == 0, "некорректный regex не роняет поиск");

            // Замена: обычный текст и регулярное выражение, с сохранением между документами.
            var plainReplace = project.ReplaceAll("Коротко о продукте.", "Кратко о товаре.");
            Check(plainReplace.ReplacementCount == 1, "обычная замена нашла одно вхождение");
            Check(plainReplace.ChangedFiles.Count == 1, "обычная замена затронула один файл");
            Check(project.GetDocument(Path.Combine(root, "intro.dita")).Root
                    .FindDescendant("shortdesc")!.InnerText.Contains("Кратко о товаре."),
                "обычная замена применилась к тексту документа");

            // Замена работает по отдельным текстовым узлам, не сквозь дочерние элементы — регулярное
            // выражение здесь не пересекает границу с вложенным <b>Готово</b>.
            var regexReplace = project.ReplaceAll(@"Наж\w+", "Кликните", regex: true);
            Check(regexReplace.ReplacementCount == 1, "regex-замена нашла одно вхождение");
            Check(project.GetDocument(Path.Combine(root, "install.dita")).Root
                    .FindDescendant("steps")!.InnerText.Contains("Кликните Готово"),
                "regex-замена изменила текстовый узел");

            // Импорт условий сборки из .ditaval.
            var ditavalPath = Path.Combine(root, "profile.ditaval");
            File.WriteAllText(ditavalPath, """
<?xml version="1.0" encoding="UTF-8"?>
<val>
  <prop action="exclude" att="platform" val="linux"/>
  <prop action="include" val="tip"/>
  <prop action="flag" att="audience" val="expert" color="red" backgroundcolor="yellow" style="bold" changebar="orange"/>
</val>
""");
            var ditavalRules = DitavalReader.ReadExcludeRules(ditavalPath);
            Check(ditavalRules.TryGetValue("platform", out var linuxRule) && linuxRule.Contains("linux"),
                "правило exclude из .ditaval прочитано");
            Check(ditavalRules.Count == 1, "правило include из .ditaval пропущено как неподдерживаемое");

            var ditavalFull = DitavalReader.Read(ditavalPath);
            Check(ditavalFull.Exclude.TryGetValue("platform", out var linuxRule2) && linuxRule2.Contains("linux"),
                "Read(): правило exclude прочитано вместе с правилами подсветки");
            Check(ditavalFull.Flags.Count == 1, "Read(): правило flag прочитано");
            var flagRule = ditavalFull.Flags[0];
            Check(flagRule is { Attribute: "audience", Value: "expert", Color: "red", BackgroundColor: "yellow", Style: "bold", ChangeBar: "orange" },
                "Read(): все атрибуты правила flag прочитаны верно");

            // Привязка .ditaval к проекту: путь сохраняется, файл перечитывается заново при каждом
            // обращении — правки на диске подхватываются без переимпорта.
            project.SetDitavalPath("profile.ditaval");
            Check(project.DitavalPath == "profile.ditaval", "путь к связанному .ditaval сохранён в проекте");

            var reopenedDitaval = new DitaProject(root);
            Check(reopenedDitaval.DitavalPath == "profile.ditaval", "путь к .ditaval переживает переоткрытие проекта");

            var linked = project.ResolveLinkedDitaval();
            Check(linked is not null && linked.Exclude["platform"].Contains("linux") && linked.Flags.Count == 1,
                "связанный .ditaval резолвится с правилами исключения и подсветки");

            File.WriteAllText(ditavalPath, """
<?xml version="1.0" encoding="UTF-8"?>
<val>
  <prop action="exclude" att="platform" val="linux"/>
  <prop action="exclude" att="platform" val="macos"/>
  <prop action="flag" att="audience" val="expert" color="red" backgroundcolor="yellow" style="bold" changebar="orange"/>
</val>
""");
            var relinked = project.ResolveLinkedDitaval();
            Check(relinked!.Exclude["platform"].SetEquals(new[] { "linux", "macos" }),
                "изменение .ditaval на диске подхватывается без переимпорта");

            // DitavalWriter: правка исключений через диалог условий не должна стирать правила
            // подсветки, написанные вручную в том же файле — round-trip Read → Write → Read.
            var editedExclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["platform"] = new HashSet<string> { "linux" },
                ["product"] = new HashSet<string> { "enterprise" }
            };
            DitavalWriter.Write(ditavalPath, new DitavalRules(editedExclude, relinked.Flags));
            var rewritten = DitavalReader.Read(ditavalPath);
            Check(rewritten.Exclude["platform"].SetEquals(new[] { "linux" }) && rewritten.Exclude["product"].SetEquals(new[] { "enterprise" }),
                "DitavalWriter записал новые правила исключения");
            Check(!rewritten.Exclude.ContainsKey("platform") || !rewritten.Exclude["platform"].Contains("macos"),
                "исключённое ранее значение (macos), снятое в редакторе, не попало в файл");
            Check(rewritten.Flags.Count == 1 && rewritten.Flags[0] is { Attribute: "audience", Value: "expert", Color: "red", BackgroundColor: "yellow", Style: "bold", ChangeBar: "orange" },
                "DitavalWriter сохранил правило подсветки нетронутым при правке исключений");

            project.SetDitavalPath(null);
            Check(project.DitavalPath is null, "связь с .ditaval можно снять");
            Check(new DitaProject(root).DitavalPath is null, "снятие связи с .ditaval сохраняется на диске");

            // Привязка внешнего DTD: путь сохраняется, разбирается заново при каждом обращении.
            File.WriteAllText(Path.Combine(root, "custom.dtd"), """
<!ELEMENT widget (#PCDATA)>
<!ATTLIST widget class CDATA "+ topic/ph custom-d/widget ">
""");
            project.SetExternalDtdPath("custom.dtd");
            Check(project.ExternalDtdPath == "custom.dtd", "путь к внешнему DTD сохранён в проекте");

            var reopenedDtd = new DitaProject(root);
            Check(reopenedDtd.ExternalDtdPath == "custom.dtd", "путь к внешнему DTD переживает переоткрытие проекта");

            var dtdResult = project.ResolveExternalDtd();
            Check(dtdResult is not null && dtdResult.Elements.Any(e => e.Name == "widget"),
                "связанный внешний DTD резолвится в элементы каталога");

            project.SetExternalDtdPath(null);
            Check(project.ExternalDtdPath is null, "связь с внешним DTD можно снять");
            Check(new DitaProject(root).ExternalDtdPath is null, "снятие связи с внешним DTD сохраняется на диске");

            // Слияние правил исключения (используется при импорте .ditaval поверх уже заданных условий).
            var existingExclude = new Dictionary<string, HashSet<string>>
            {
                ["platform"] = new HashSet<string> { "windows" },
                ["audience"] = new HashSet<string> { "expert" }
            };
            var addedCount = DitaProject.MergeExcludeConditions(existingExclude, ditavalRules);
            Check(addedCount == 1, $"слияние вернуло число реально добавленных значений: {addedCount}");
            Check(existingExclude["platform"].SetEquals(new[] { "windows", "linux" }),
                "слияние объединило значения по общему атрибуту, не затерев старое");
            Check(existingExclude["audience"].SetEquals(new[] { "expert" }),
                "слияние не тронуло атрибут, которого нет в импортируемых правилах");

            var noNewValues = DitaProject.MergeExcludeConditions(existingExclude, ditavalRules);
            Check(noNewValues == 0, "повторное слияние тех же правил не добавляет новых значений");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // ------------------------------------------------------------ keyscope

    private static void KeyScopeTests()
    {
        Section("Области ключей (keyscope)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic-a.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-a">
  <title>Топик А</title>
  <conbody>
    <p>Текущее издание: <keyword keyref="edition"/>.</p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "topic-b.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-b">
  <title>Топик Б</title>
  <conbody>
    <p>Текущее издание: <keyword keyref="edition"/>.</p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "topic-c.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-c">
  <title>Топик В</title>
  <conbody>
    <p>Издание ветки А (по явному пути): <keyword keyref="branch-a.edition"/>.</p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест областей ключей</title>
  <topicref keyscope="branch-a">
    <keydef keys="edition"><topicmeta><keywords><keyword>Выпуск А</keyword></keywords></topicmeta></keydef>
    <topicref href="topic-a.dita"/>
  </topicref>
  <topicref keyscope="branch-b">
    <keydef keys="edition"><topicmeta><keywords><keyword>Выпуск Б</keyword></keywords></topicmeta></keydef>
    <topicref href="topic-b.dita"/>
  </topicref>
  <topicref href="topic-c.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            // Без цепочки областей — ключ виден, только если определён в корне (здесь не определён).
            Check(project.ResolveKey("edition") is null, "неквалифицированный ключ вне области не резолвится в корне");
            Check(project.ResolveKey("branch-a.edition")?.KeyText == "Выпуск А", "явно квалифицированный ключ резолвится напрямую");
            Check(project.ResolveKey("branch-b.edition")?.KeyText == "Выпуск Б", "явно квалифицированный ключ второй ветки резолвится напрямую");
            Check(project.ResolveKey("edition", new[] { "branch-a" })?.KeyText == "Выпуск А",
                "ключ резолвится с учётом переданной цепочки области");
            Check(project.ResolveKey("edition", new[] { "no-such-scope" }) is null,
                "несуществующая область не приводит к ложному совпадению");

            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            });

            var html = File.ReadAllText(result.EntryFile);
            Check(html.Contains("Выпуск А") && html.Contains("Выпуск Б"),
                "у двух веток с одинаковым именем ключа разные значения попали в публикацию");
            Check(html.Contains("Издание ветки А (по явному пути): ") && html.Contains(">Выпуск А<"),
                "топик вне области достаёт значение чужой ветки по явному пути branch-a.edition");

            var docxOut = Path.Combine(root, "out.docx");
            var docxResult = new DocxPublisher(project).Publish(Path.Combine(root, "map.ditamap"),
                new PublishOptions(), docxOut);
            using (var doc = WordprocessingDocument.Open(docxOut, false))
            {
                var docxText = doc.MainDocumentPart!.Document.Body!.InnerText;
                Check(docxText.Contains("Выпуск А") && docxText.Contains("Выпуск Б"),
                    "области ключей учитываются и при экспорте в DOCX");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void MultiProjectWorkspaceTests()
    {
        Section("Мультипроектный workspace (проекты-источники ключей)");

        var mainRoot = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-main");
        var sharedRoot = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-shared");
        Directory.CreateDirectory(mainRoot);
        Directory.CreateDirectory(sharedRoot);

        try
        {
            File.WriteAllText(Path.Combine(sharedRoot, "terms.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Общие термины</title>
  <keydef keys="product-name"><topicmeta><keywords><keyword>Мегапродукт</keyword></keywords></topicmeta></keydef>
</map>
""");
            var sharedProject = new DitaProject(sharedRoot);
            sharedProject.Scan();

            File.WriteAllText(Path.Combine(mainRoot, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <p>Добро пожаловать в <keyword keyref="product-name"/>.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(mainRoot, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Основной проект</title>
  <topicref href="topic.dita"/>
</map>
""");

            var project = new DitaProject(mainRoot);
            project.Scan();

            Check(project.ResolveKey("product-name") is null, "до подключения источника ключ не резолвится");
            Check(!project.KeyExistsAnywhere("product-name"), "до подключения источника KeyExistsAnywhere тоже не находит ключ");

            project.AddReferencedProject(sharedRoot);
            Check(project.ReferencedProjectPaths.Count == 1, "путь к проекту-источнику сохранён");
            Check(project.ResolveKey("product-name")?.KeyText == "Мегапродукт",
                "ключ резолвится из подключённого проекта-источника");
            Check(project.KeyExistsAnywhere("product-name"), "KeyExistsAnywhere видит ключ из проекта-источника");

            var issues = project.ValidateAll();
            Check(!issues.Any(i => i.Message.Contains("product-name")),
                "keyref на ключ из проекта-источника не считается неразрешённым при валидации");

            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(mainRoot, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(mainRoot, "out"),
                SingleFile = true
            });
            var html = File.ReadAllText(result.EntryFile);
            Check(html.Contains("Мегапродукт"), "keyref на ключ из проекта-источника подставился в публикацию");

            var reopened = new DitaProject(mainRoot);
            reopened.Scan();
            Check(reopened.ReferencedProjectPaths.Count == 1, "связь с проектом-источником переживает переоткрытие проекта");
            Check(reopened.ResolveKey("product-name")?.KeyText == "Мегапродукт",
                "разрешение ключа из источника работает и после переоткрытия проекта");

            project.RemoveReferencedProject(sharedRoot);
            Check(project.ReferencedProjectPaths.Count == 0, "проект-источник можно отключить");
            Check(project.ResolveKey("product-name") is null, "после отключения источника ключ снова не резолвится");

            // Защита от циклической связи: источник, который сам ссылается на подключивший его
            // проект, не должен уйти в бесконечную рекурсию при Scan() — а его собственные "источники"
            // просто игнорируются (наружу видна только его собственная корневая область ключей).
            sharedProject.AddReferencedProject(mainRoot);
            project.AddReferencedProject(sharedRoot);
            project.Scan();
            Check(project.ResolveKey("product-name")?.KeyText == "Мегапродукт",
                "циклическая связь между проектами не мешает разрешению ключа и не роняет Scan()");
        }
        finally
        {
            try
            {
                Directory.Delete(mainRoot, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }

            try
            {
                Directory.Delete(sharedRoot, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void ValidationAndAnchorFixTests()
    {
        Section("Исправления: валидация keyscope-ключей и якоря вложенных элементов");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "a.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="a">
  <title>A</title>
  <conbody>
    <p>Издание: <keyword keyref="edition"/>.</p>
    <note id="warn1">Предупреждение.</note>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "b.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="b">
  <title>B</title>
  <conbody>
    <p>См. также <xref href="a.dita#a/warn1">предупреждение</xref>.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест исправлений</title>
  <topicref keyscope="branch-a">
    <keydef keys="edition"><topicmeta><keywords><keyword>Выпуск А</keyword></keywords></topicmeta></keydef>
    <topicref href="a.dita"/>
  </topicref>
  <topicref href="b.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            // --- ложные ошибки валидации для keyscope-ключей ---
            var issues = project.ValidateAll();
            Check(!issues.Any(i => i.Message.Contains("\"edition\"")),
                $"keyref на ключ, объявленный только внутри keyscope, не считается необъявленным: " +
                $"{string.Join("; ", issues.Where(i => i.Message.Contains("edition")).Select(i => i.Message))}");
            Check(project.KeyExistsAnywhere("edition"), "KeyExistsAnywhere находит ключ во вложенной области");
            Check(!project.KeyExistsAnywhere("no-such-key"), "KeyExistsAnywhere не даёт ложных срабатываний");

            // --- якорь для xref на элемент внутри другого топика в однофайловой сборке ---
            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            });

            var html = File.ReadAllText(result.EntryFile);
            var hrefMatch = Regex.Match(html, "href=\"#(a--warn1)\"");
            Check(hrefMatch.Success, $"xref на a.dita#a/warn1 ссылается на префиксованный якорь: найдено в HTML? {hrefMatch.Success}");
            Check(hrefMatch.Success && html.Contains($"id=\"{hrefMatch.Groups[1].Value}\""),
                $"целевой элемент действительно имеет такой id в выводе (якорь не битый)");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // -------------------------------------------------- таблицы соответствий

    private static void RelTableTests()
    {
        Section("Таблицы соответствий (reltable)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "concept.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="concept"><title>Обзор</title><conbody><p>Текст.</p></conbody></concept>
""");
            File.WriteAllText(Path.Combine(root, "task.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="task"><title>Установка</title><taskbody><steps><step><cmd>Шаг.</cmd></step></steps></taskbody></task>
""");
            File.WriteAllText(Path.Combine(root, "ref.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<reference id="ref"><title>Параметры</title><refbody><section><p>Текст.</p></section></refbody></reference>
""");

            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест таблицы соответствий</title>
  <topicref href="concept.dita"/>
  <topicref href="task.dita"/>
  <topicref href="ref.dita"/>
  <reltable>
    <relrow>
      <relcell><topicref href="concept.dita"/></relcell>
      <relcell><topicref href="task.dita"/></relcell>
    </relrow>
    <relrow>
      <relcell><topicref href="task.dita"/></relcell>
      <relcell><topicref href="ref.dita"/></relcell>
    </relrow>
  </reltable>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var tree = MapTree.Build(project, Path.Combine(root, "map.ditamap"));
            var conceptPath = Path.GetFullPath(Path.Combine(root, "concept.dita"));
            var taskPath = Path.GetFullPath(Path.Combine(root, "task.dita"));
            var refPath = Path.GetFullPath(Path.Combine(root, "ref.dita"));

            Check(tree.RelatedLinks.TryGetValue(conceptPath, out var conceptLinks) &&
                  conceptLinks.Count == 1 && string.Equals(conceptLinks[0].Path, taskPath, StringComparison.OrdinalIgnoreCase),
                "обзор связан с задачей (одна строка reltable)");
            Check(tree.RelatedLinks.TryGetValue(taskPath, out var taskLinks) && taskLinks.Count == 2,
                $"задача встречается в двух строках — связана с обоими соседями: {taskLinks?.Count}");
            Check(tree.RelatedLinks.TryGetValue(refPath, out var refLinks) &&
                  refLinks.Count == 1 && string.Equals(refLinks[0].Path, taskPath, StringComparison.OrdinalIgnoreCase),
                "справка связана с задачей (вторая строка reltable)");
            Check(!tree.RelatedLinks.ContainsKey(conceptPath) || !conceptLinks!.Any(l => string.Equals(l.Path, refPath, StringComparison.OrdinalIgnoreCase)),
                "обзор и справка не связаны напрямую — они в разных строках reltable");

            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            });

            var html = File.ReadAllText(result.EntryFile);
            Check(html.Contains("reltable-links"), "автоматический блок related-links из reltable попал в публикацию");
            Check(Regex.Matches(html, "reltable-links").Count == 3,
                $"блок сгенерирован для каждого из трёх топиков: {Regex.Matches(html, "reltable-links").Count}");

            var docxOut = Path.Combine(root, "out.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "map.ditamap"), new PublishOptions(), docxOut);
            using (var doc = WordprocessingDocument.Open(docxOut, false))
            {
                var hyperlinks = doc.MainDocumentPart!.Document.Body!.Descendants<Hyperlink>().Count(h => h.Anchor is not null);
                Check(hyperlinks >= 4, $"reltable-связи стали внутренними гиперссылками и в DOCX: {hyperlinks}");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void MapTreeMiscTests()
    {
        Section("MapTree: mapref, keyref-topicref, заголовки узлов карты (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "innertopic.dita"), "<concept id=\"inner\"><title>Внутренний топик</title><conbody><p>x</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "inner.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map><title>Inner</title><topicref href="innertopic.dita"/></map>
""");
            File.WriteAllText(Path.Combine(root, "k1target.dita"), "<concept id=\"k1t\"><title>Цель по ключу</title><conbody><p>x</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "a.dita"), "<concept id=\"a\"><title>A</title><conbody><p>x</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "multi.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="first">
  <title>Первый</title>
  <conbody><concept id="second"><title>Второй</title><conbody><p>x</p></conbody></concept></conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "outer.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Outer</title>
  <mapref href="inner.ditamap"/>
  <keydef keys="k1" href="k1target.dita"/>
  <keydef keys="k2"><topicmeta><keywords><keyword>дефолт</keyword></keywords></topicmeta></keydef>
  <keydef/>
  <topicref keyref="k1"/>
  <topicref keyref="no-such-key"/>
  <topicref href="http://example.com" scope="external"/>
  <topicref href="a.dita" navtitle="Заголовок из атрибута"/>
  <topicref href="nope.dita"><topicmeta><linktext>Текст ссылки</linktext></topicmeta></topicref>
  <topicref href="multi.dita#second"/>
  <topicref href="totally-missing.dita"/>
  <topicref/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var tree = MapTree.Build(project, Path.Combine(root, "outer.ditamap"));
            var items = tree.Items.ToList();

            var mapRefItem = items.Single(i => i.ElementName == "mapref");
            Check(mapRefItem.IsMapRef, "mapref распознан как IsMapRef");
            Check(mapRefItem.Title == "Inner", $"заголовок вложенной карты подхватился: '{mapRefItem.Title}'");
            Check(items.Any(i => i.Title == "Внутренний топик"),
                "содержимое вложенной карты (mapref) раскрыто рекурсивно и попало в общее дерево");

            var byKeyref = items.Single(i => i.Node.GetAttribute("keyref") == "k1");
            Check(byKeyref.TargetPath == Path.GetFullPath(Path.Combine(root, "k1target.dita")) && !byKeyref.IsBroken,
                "topicref с keyref (без href) резолвится через ключ карты");
            Check(byKeyref.Title == "Цель по ключу", "заголовок узла по keyref взят из целевого топика");

            var unresolvedKeyref = items.Single(i => i.Node.GetAttribute("keyref") == "no-such-key");
            Check(unresolvedKeyref.IsBroken, "topicref с несуществующим keyref помечен как битый");

            var external = items.Single(i => i.Href == "http://example.com");
            Check(external.TargetPath is null && !external.IsBroken,
                "scope=\"external\" — ссылка не резолвится и не считается битой (внешние URL не проверяются)");

            var navtitleAttr = items.Single(i => i.Href == "a.dita");
            Check(navtitleAttr.Title == "Заголовок из атрибута", "атрибут navtitle на topicref побеждает заголовок целевого топика (A)");

            var linktextItem = items.Single(i => i.Href == "nope.dita");
            Check(linktextItem.Title == "Текст ссылки", "topicmeta/linktext используется как заголовок, когда navtitle нет");
            Check(linktextItem.IsBroken, "тот же узел: цель (nope.dita) не существует — тоже помечен битым");

            var subTopicItem = items.Single(i => i.Href == "multi.dita#second");
            Check(subTopicItem.TargetTopicId == "second", "фрагмент #second разобран как TargetTopicId");
            Check(subTopicItem.Title == "Второй",
                $"заголовок взят из ВЛОЖЕННОГО топика по TargetTopicId (не из файла целиком, там был бы 'Первый'): '{subTopicItem.Title}'");

            var keydefWithText = items.Single(i => i.Keys == "k2");
            Check(keydefWithText.Title == "ключ: k2", $"keydef без href/navtitle/linktext — заголовок 'ключ: <keys>': '{keydefWithText.Title}'");

            var keydefBare = items.Single(i => i.ElementName == "keydef" && i.Keys is null);
            Check(keydefBare.Title == "keydef", "keydef совсем без атрибутов — заголовок буквально 'keydef'");

            var brokenWithHref = items.Single(i => i.Href == "totally-missing.dita");
            Check(brokenWithHref.Title == "totally-missing.dita", "нет ни navtitle/linktext, ни цели — заголовком становится сам href");

            var bareTopicref = items.Single(i => i.ElementName == "topicref" && i.Href is null && i.Keys is null && i.Node.GetAttribute("keyref") is null);
            Check(bareTopicref.Title == "<topicref>", "совсем пустой topicref (ни href, ни keyref, ни keys) — заголовок '<имя_элемента>'");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // ------------------------------------------------- пометка изменений (rev)

    private static void RevChangeTests()
    {
        Section("Пометка изменений (rev)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <p rev="v2">Изменённый абзац.</p>
    <p>Обычный абзац.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест rev</title>
  <topicref href="topic.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            });

            var html = File.ReadAllText(result.EntryFile);
            Check(html.Contains("rev-changed"), "класс rev-changed попал в публикацию");
            Check(Regex.IsMatch(html, "<p class=\"rev-changed\">Изменённый абзац\\."),
                "класс rev-changed стоит именно на изменённом абзаце");
            Check(!Regex.IsMatch(html, "<p class=\"rev-changed\">Обычный абзац\\."),
                "обычный абзац класс rev-changed не получил");

            var docxOut = Path.Combine(root, "out.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "map.ditamap"), new PublishOptions(), docxOut);
            using (var doc = WordprocessingDocument.Open(docxOut, false))
            {
                var hasRevBorder = doc.MainDocumentPart!.Document.Body!.Descendants<LeftBorder>()
                    .Any(b => b.Color?.Value == "D4380D");
                Check(hasRevBorder, "полоса на полях у изменённого абзаца попала и в DOCX");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void TrackChangesTests()
    {
        Section("Track changes (status=\"new\"/\"deleted\")");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <p id="plain">Обычный абзац.</p>
    <p id="inserted">Новый абзац.</p>
    <p id="deleted">Абзац на удаление.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест track changes</title>
  <topicref href="topic.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var topicPath = Path.Combine(root, "topic.dita");
            var doc = project.GetDocument(topicPath);
            var insertedNode = doc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "inserted");
            var deletedNode = doc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "deleted");

            TrackChanges.MarkInserted(insertedNode, "Автор А");
            TrackChanges.MarkDeleted(deletedNode, "Автор Б");

            Check(TrackChanges.IsInserted(insertedNode) && !TrackChanges.IsDeleted(insertedNode),
                "MarkInserted проставил status=\"new\"");
            Check(TrackChanges.IsDeleted(deletedNode) && !TrackChanges.IsInserted(deletedNode),
                "MarkDeleted проставил status=\"deleted\"");
            Check(insertedNode.GetAttribute("tcauthor") == "Автор А", "автор вставки сохранён");
            Check(deletedNode.GetAttribute("tcauthor") == "Автор Б", "автор удаления сохранён");
            Check(TrackChanges.CollectTracked(doc.Root).Count() == 2, "CollectTracked нашёл обе отслеживаемые правки");

            var publisher = new HtmlPublisher(project);
            var published = publisher.Publish(Path.Combine(root, "map.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            });
            var publishedHtml = File.ReadAllText(published.EntryFile);
            Check(publishedHtml.Contains("Новый абзац."), "вставленный абзац попал в публикацию как обычное содержимое");
            Check(!publishedHtml.Contains("Абзац на удаление."), "помеченный на удаление абзац в публикацию не попал");

            var previewHtml = publisher.RenderPreview(doc);
            Check(previewHtml.Contains("Абзац на удаление."), "предпросмотр показывает помеченное на удаление содержимое");
            Check(Regex.IsMatch(previewHtml, "<p[^>]*class=\"tc-inserted\"[^>]*>Новый абзац\\."),
                "класс tc-inserted стоит на вставленном абзаце в предпросмотре");
            Check(Regex.IsMatch(previewHtml, "<p[^>]*class=\"tc-deleted\"[^>]*>Абзац на удаление\\."),
                "класс tc-deleted стоит на удалённом абзаце в предпросмотре");

            var docxOut = Path.Combine(root, "out.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "map.ditamap"), new PublishOptions(), docxOut);
            using (var docxDoc = WordprocessingDocument.Open(docxOut, false))
            {
                var docxText = docxDoc.MainDocumentPart!.Document.Body!.InnerText;
                Check(docxText.Contains("Новый абзац."), "вставленный абзац попал и в DOCX");
                Check(!docxText.Contains("Абзац на удаление."), "помеченный на удаление абзац в DOCX не попал");
            }

            // Accept/Reject — чистая работа с деревом, без публикации.
            var acceptDoc = DitaDocument.Parse(doc.ToXmlString());
            var acceptInserted = acceptDoc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "inserted");
            var acceptDeleted = acceptDoc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "deleted");
            TrackChanges.Accept(acceptInserted);
            TrackChanges.Accept(acceptDeleted);
            Check(!TrackChanges.IsTracked(acceptInserted) && acceptInserted.Parent is not null,
                "Accept на вставке снимает пометку, узел остаётся в дереве");
            Check(acceptDeleted.Parent is null, "Accept на удалении физически убирает узел из дерева");

            var rejectDoc = DitaDocument.Parse(doc.ToXmlString());
            var rejectInserted = rejectDoc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "inserted");
            var rejectDeleted = rejectDoc.Root.DescendantsAndSelf().First(n => n.Name == "p" && n.GetAttribute("id") == "deleted");
            TrackChanges.Reject(rejectInserted);
            TrackChanges.Reject(rejectDeleted);
            Check(rejectInserted.Parent is null, "Reject на вставке физически убирает узел из дерева");
            Check(!TrackChanges.IsTracked(rejectDeleted) && rejectDeleted.Parent is not null,
                "Reject на удалении снимает пометку, узел восстановлен в дереве");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void XliffTests()
    {
        Section("Экспорт/импорт XLIFF");

        const string SourceXml = """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Обзор</title>
  <shortdesc>Краткое описание.</shortdesc>
  <conbody>
    <p>Текст с <b>жирным</b> и <uicontrol keyref="setup-button">Готово</uicontrol> и картинкой <image href="pic.png"><alt>Схема</alt></image>.</p>
    <note>Общее предупреждение.
      <p>Вложенный абзац внутри note.</p>
    </note>
  </conbody>
</concept>
""";

        var doc = DitaDocument.Parse(SourceXml);
        doc.FilePath = "topic.dita";

        var xliff = XliffConverter.Export(doc, "ru", "en");
        var units = xliff.Root!.Element("file")!.Element("body")!.Elements("trans-unit").ToList();

        Check(units.Count == 6,
            $"шесть независимых сегментов: title, shortdesc, alt (внутри image), p (conbody), p (внутри note), собственный текст note -> найдено {units.Count}");

        var pUnit = units.FirstOrDefault(u => u.Attribute("resname")!.Value == "/concept/conbody/p");
        Check(pUnit is not null, "сегмент абзаца из conbody найден по resname");
        var pSource = pUnit!.Element("source")!;
        Check(pSource.Elements("bpt").Count() == 3,
            $"в абзаце три парных фразовых элемента (b/uicontrol/image): {pSource.Elements("bpt").Count()}");
        Check(pSource.Value.Contains("жирным") && pSource.Value.Contains("Готово"),
            "текст внутри b/uicontrol попал в исходный сегмент как обычный переводимый текст");

        var altUnit = units.FirstOrDefault(u => u.Attribute("resname")!.Value.EndsWith("/alt", StringComparison.Ordinal));
        Check(altUnit is not null, "alt внутри image сегментирован независимо от абзаца");
        Check(altUnit!.Element("source")!.Value == "Схема", "текст alt попал в свой сегмент как есть");

        // --- перевод: подменяем текст в target каждого сегмента на маркер "[EN] исходный текст"
        foreach (var unit in units)
        {
            var target = unit.Element("target")!;
            foreach (var textNode in target.Nodes().OfType<XText>().ToList())
            {
                textNode.Value = "[EN]" + textNode.Value;
            }
        }

        var warnings = new List<string>();
        var reimported = DitaDocument.Parse(SourceXml);
        reimported.FilePath = "topic.dita";
        var applied = XliffConverter.Import(reimported, xliff, warnings);

        Check(applied == units.Count, $"импорт применил все {units.Count} сегментов: {applied}");
        Check(warnings.Count == 0, "чистый round-trip без предупреждений: " + string.Join("; ", warnings));

        var titleText = reimported.Root.FirstElement("title")!.InnerText;
        Check(titleText == "[EN]Обзор", $"заголовок переведён: '{titleText}'");

        var pNode = reimported.Root.FindDescendant("conbody")!.FirstElement("p")!;
        Check(pNode.InnerText.Contains("[EN]Текст с") && pNode.InnerText.Contains("[EN]жирным") && pNode.InnerText.Contains("[EN]Готово"),
            $"текст абзаца и текст внутри b/uicontrol переведены: '{pNode.InnerText}'");

        var uicontrolNode = pNode.FindDescendant("uicontrol")!;
        Check(uicontrolNode.GetAttribute("keyref") == "setup-button",
            "атрибут keyref у uicontrol сохранён после round-trip");

        var imageNode = pNode.FindDescendant("image")!;
        Check(imageNode.GetAttribute("href") == "pic.png", "атрибут href у image сохранён после round-trip");
        Check(imageNode.FirstElement("alt")!.InnerText == "[EN]Схема", "alt внутри image переведён независимо от абзаца");

        var noteNode = reimported.Root.FindDescendant("note")!;
        Check(noteNode.Children.Any(c => c.Kind == NodeKind.Text && c.Value.Contains("[EN]Общее предупреждение")),
            "собственный текст note переведён");
        Check(noteNode.FirstElement("p")!.InnerText.Contains("[EN]Вложенный абзац"),
            "вложенный <p> внутри note переведён как отдельный сегмент");

        // --- устойчивость к повреждённому XLIFF: не роняет импорт, копит предупреждения
        var brokenXliff = XliffConverter.Export(doc, "ru", "en");
        var brokenUnits = brokenXliff.Root!.Element("file")!.Element("body")!.Elements("trans-unit").ToList();
        brokenUnits[0].Element("target")!.Remove();
        var missingTargetWarnings = new List<string>();
        var docForMissingTarget = DitaDocument.Parse(SourceXml);
        var appliedMissingTarget = XliffConverter.Import(docForMissingTarget, brokenXliff, missingTargetWarnings);
        Check(appliedMissingTarget == brokenUnits.Count - 1, "сегмент без <target> пропущен, остальные применены");
        Check(missingTargetWarnings.Count == 1, $"отсутствие <target> дало одно предупреждение: {missingTargetWarnings.Count}");

        var badIdXliff = XliffConverter.Export(doc, "ru", "en");
        var badPUnit = badIdXliff.Root!.Element("file")!.Element("body")!.Elements("trans-unit")
            .First(u => u.Attribute("resname")!.Value == "/concept/conbody/p");
        badPUnit.Element("target")!.Element("bpt")!.SetAttributeValue("id", "999");
        var badIdWarnings = new List<string>();
        var docForBadId = DitaDocument.Parse(SourceXml);
        XliffConverter.Import(docForBadId, badIdXliff, badIdWarnings);
        Check(badIdWarnings.Any(w => w.Contains("вне диапазона")), "placeholder id вне диапазона зафиксирован предупреждением, импорт не падает");
    }

    private static readonly string[] PhraseTags = { "b", "i", "u", "tt" };
    private static readonly string[] RandomWordsPool =
        { "текст", "пример", "значение", "документ", "проверка", "элемент", "раздел", "материал" };

    private static string RandomWords(Random rnd)
    {
        var count = 1 + rnd.Next(3);
        return string.Join(" ", Enumerable.Range(0, count).Select(_ => RandomWordsPool[rnd.Next(RandomWordsPool.Length)]));
    }

    /// <summary>Случайная, но валидная вложенность фразовых элементов внутри абзаца/note —
    /// b/i/u/tt рекурсивно, плюс uicontrol с keyref и image с alt (те же элементы, на которых
    /// раньше вручную ловился баг потери вложенности в XliffTests).</summary>
    private static string GenerateInlineFragment(Random rnd, int depth)
    {
        var pieceCount = 1 + rnd.Next(3);
        var sb = new StringBuilder();
        for (var i = 0; i < pieceCount; i++)
        {
            sb.Append(GenerateInlinePiece(rnd, depth));
        }

        return sb.ToString();
    }

    private static string GenerateInlinePiece(Random rnd, int depth)
    {
        var roll = rnd.Next(depth <= 0 ? 2 : 5);
        switch (roll)
        {
            case 0:
            case 1:
                return RandomWords(rnd);
            case 2:
                var tag = PhraseTags[rnd.Next(PhraseTags.Length)];
                return $"<{tag}>{GenerateInlineFragment(rnd, depth - 1)}</{tag}>";
            case 3:
                return $"<uicontrol keyref=\"k{rnd.Next(5)}\">{GenerateInlineFragment(rnd, depth - 1)}</uicontrol>";
            default:
                return $"<image href=\"pic{rnd.Next(5)}.png\"><alt>{RandomWords(rnd)}</alt></image>";
        }
    }

    private static string GenerateRandomTopicXml(Random rnd) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>{RandomWords(rnd)}</title>
  <shortdesc>{RandomWords(rnd)}</shortdesc>
  <conbody>
    <p>{GenerateInlineFragment(rnd, 2)}</p>
    <note>{RandomWords(rnd)} <p>{GenerateInlineFragment(rnd, 1)}</p></note>
  </conbody>
</concept>
""";

    /// <summary>Рекурсивно сверяет форму дерева до и после «перевода»: те же элементы, те же
    /// атрибуты, то же число и порядок детей — а текстовые узлы отличаются ровно на префикс.</summary>
    private static void AssertSameShapeAfterTranslation(DitaNode original, DitaNode translated, string prefix, List<string> mismatches, string path)
    {
        if (original.Kind != translated.Kind)
        {
            mismatches.Add($"{path}: разный тип узла ({original.Kind} vs {translated.Kind})");
            return;
        }

        if (original.Kind == NodeKind.Element)
        {
            if (original.Name != translated.Name)
            {
                mismatches.Add($"{path}: разное имя элемента ({original.Name} vs {translated.Name})");
            }

            var origAttrs = original.Attributes.Select(a => (a.Name, a.Value)).OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
            var transAttrs = translated.Attributes.Select(a => (a.Name, a.Value)).OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
            if (!origAttrs.SequenceEqual(transAttrs))
            {
                mismatches.Add($"{path}: атрибуты не совпадают ({string.Join(",", origAttrs)} vs {string.Join(",", transAttrs)})");
            }
        }
        else if (original.Kind == NodeKind.Text)
        {
            if (translated.Value != prefix + original.Value)
            {
                mismatches.Add($"{path}: текст переведён неверно ('{original.Value}' -> '{translated.Value}', ожидалось '{prefix + original.Value}')");
            }
        }

        if (original.Children.Count != translated.Children.Count)
        {
            mismatches.Add($"{path}: разное число дочерних узлов ({original.Children.Count} vs {translated.Children.Count})");
            return;
        }

        for (var i = 0; i < original.Children.Count; i++)
        {
            AssertSameShapeAfterTranslation(original.Children[i], translated.Children[i], prefix, mismatches, $"{path}/{i}");
        }
    }

    private static void XliffConverterPropertyTests()
    {
        Section("Property-based: экспорт/импорт XLIFF (случайные фразовые деревья)");

        var rnd = new Random(777);
        const int iterations = 80;
        var failures = 0;

        for (var i = 0; i < iterations; i++)
        {
            var sourceXml = GenerateRandomTopicXml(rnd);
            try
            {
                var baseline = DitaDocument.Parse(sourceXml);

                // 1) round-trip без перевода: применение немодифицированных <target> обязано
                //    вернуть побайтово тот же документ, что и свежий разбор исходника.
                var docForNoop = DitaDocument.Parse(sourceXml);
                docForNoop.FilePath = "random.dita";
                var xliffForNoop = XliffConverter.Export(docForNoop, "ru", "en");
                var noopWarnings = new List<string>();
                var reimportedNoop = DitaDocument.Parse(sourceXml);
                reimportedNoop.FilePath = "random.dita";
                XliffConverter.Import(reimportedNoop, xliffForNoop, noopWarnings);

                if (noopWarnings.Count != 0)
                {
                    failures++;
                    Failures.Add($"XLIFF property: noop-импорт дал предупреждения на случае {i}: {string.Join("; ", noopWarnings)}\nXML: {sourceXml}");
                    continue;
                }

                if (reimportedNoop.ToXmlString() != baseline.ToXmlString())
                {
                    failures++;
                    Failures.Add($"XLIFF property: noop round-trip изменил документ на случае {i}\nXML: {sourceXml}");
                    continue;
                }

                // 2) round-trip с "переводом": каждый текстовый узел в target получает префикс
                //    [EN] — после импорта форма дерева обязана остаться той же, только текст
                //    отличается ровно на этот префикс (см. AssertSameShapeAfterTranslation).
                var docForTranslate = DitaDocument.Parse(sourceXml);
                docForTranslate.FilePath = "random.dita";
                var xliffForTranslate = XliffConverter.Export(docForTranslate, "ru", "en");
                var units = xliffForTranslate.Root!.Element("file")!.Element("body")!.Elements("trans-unit").ToList();
                foreach (var unit in units)
                {
                    var target = unit.Element("target")!;
                    foreach (var textNode in target.Nodes().OfType<XText>().ToList())
                    {
                        textNode.Value = "[EN]" + textNode.Value;
                    }
                }

                var translateWarnings = new List<string>();
                var reimportedTranslated = DitaDocument.Parse(sourceXml);
                reimportedTranslated.FilePath = "random.dita";
                var applied = XliffConverter.Import(reimportedTranslated, xliffForTranslate, translateWarnings);

                if (translateWarnings.Count != 0)
                {
                    failures++;
                    Failures.Add($"XLIFF property: перевод дал предупреждения на случае {i}: {string.Join("; ", translateWarnings)}\nXML: {sourceXml}");
                    continue;
                }

                if (applied != units.Count)
                {
                    failures++;
                    Failures.Add($"XLIFF property: применено {applied} из {units.Count} сегментов на случае {i}\nXML: {sourceXml}");
                    continue;
                }

                var mismatches = new List<string>();
                AssertSameShapeAfterTranslation(baseline.Root, reimportedTranslated.Root, "[EN]", mismatches, "");
                if (mismatches.Count > 0)
                {
                    failures++;
                    Failures.Add($"XLIFF property: расхождение формы дерева на случае {i}: {string.Join(" | ", mismatches)}\nXML: {sourceXml}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Failures.Add($"XLIFF property: исключение на случае {i}: {ex.Message}\nXML: {sourceXml}");
            }
        }

        Check(failures == 0,
            $"{iterations} случайных фразовых деревьев: экспорт/импорт XLIFF без потерь формы и содержимого ({iterations - failures}/{iterations})");
    }

    private static void DtdAttributeListParserTests()
    {
        Section("DtdAttributeListParser: типы атрибутов (по отчёту покрытия)");

        var attrs = DtdAttributeListParser.Parse(
            "req CDATA #REQUIRED " +
            "fixedval CDATA #FIXED \"const\" " +
            "single IDREF #IMPLIED " +
            "multi IDREFS #IMPLIED " +
            "tok NMTOKEN #IMPLIED " +
            "toks NMTOKENS #IMPLIED " +
            "kind NOTATION (gif|jpeg) #IMPLIED " +
            "status (obsolete|deprecated) \"deprecated\" " +
            "plain CDATA 'single-quoted default'"
        ).ToDictionary(a => a.Name);

        Check(attrs.Count == 9, $"разобраны все атрибуты одного ATTLIST: {attrs.Count}");

        Check(attrs["req"] is { Type: AttrType.CData, Required: true, DefaultValue: null }, "#REQUIRED помечает атрибут обязательным без значения по умолчанию");
        Check(attrs["fixedval"] is { Required: false, DefaultValue: "const" }, "#FIXED \"значение\" даёт зафиксированное значение по умолчанию");
        Check(attrs["single"].Type == AttrType.IdRef, "IDREF распознан");
        Check(attrs["multi"].Type == AttrType.IdRef, "IDREFS распознан как тот же AttrType.IdRef");
        Check(attrs["tok"].Type == AttrType.NmToken, "NMTOKEN распознан");
        Check(attrs["toks"].Type == AttrType.NmToken, "NMTOKENS распознан как тот же AttrType.NmToken");
        Check(attrs["kind"].Type == AttrType.CData, "NOTATION (a|b) сведён к CDATA — сама нотация не нужна каталогу");
        Check(attrs["status"] is { Type: AttrType.Enumeration } s && s.Values.SequenceEqual(new[] { "obsolete", "deprecated" }) && s.DefaultValue == "deprecated",
            "перечисление и его значение по умолчанию (двойные кавычки) разобраны");
        Check(attrs["plain"].DefaultValue == "single-quoted default", "значение по умолчанию в одинарных кавычках тоже разбирается");

        var empty = DtdAttributeListParser.Parse(string.Empty);
        Check(empty.Count == 0, "пустой текст ATTLIST — пустой список атрибутов, не падает");

        var danglingName = DtdAttributeListParser.Parse("onlyname");
        Check(danglingName.Count == 0, "имя атрибута без типа/умолчания (оборванный текст) не добавляется как половинчатая запись");
    }

    private static void DtdCatalogLoaderTests()
    {
        Section("Загрузка внешнего DTD");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // Проверяем сразу три механизма настоящего DTD: параметрическую сущность как текстовую
            // подстановку внутри ATTLIST (%common-atts;), внешний файл через ENTITY SYSTEM, и голую
            // %entity; верхнего уровня, раскрывающуюся в целые ELEMENT/ATTLIST — именно так реальные
            // модульные DTD DITA подключают домены специализации.
            File.WriteAllText(Path.Combine(root, "widget.mod"), """
<!ENTITY % widget-def '<!ELEMENT widget (#PCDATA)><!ATTLIST widget %common-atts; class CDATA "+ topic/ph custom-d/widget ">'>
%widget-def;
""");
            File.WriteAllText(Path.Combine(root, "custom.dtd"), """
<!ENTITY % common-atts "id ID #IMPLIED">
<!ENTITY % widget-mod SYSTEM "widget.mod">
%widget-mod;
<!ELEMENT root (widget+)>
<!ATTLIST root class CDATA "- topic/topic ">
""");

            var result = DtdCatalogLoader.Load(Path.Combine(root, "custom.dtd"));

            Check(result.Warnings.Count == 0, "загрузка без предупреждений: " + string.Join("; ", result.Warnings));
            Check(result.Elements.Count == 2, $"найдено два элемента (root, widget): {result.Elements.Count}");

            var widget = result.Elements.FirstOrDefault(e => e.Name == "widget");
            Check(widget is not null, "widget из внешнего файла (ENTITY SYSTEM) найден");
            Check(widget!.ClassAttr == "+ topic/ph custom-d/widget ", $"@class у widget прочитан из ATTLIST: '{widget.ClassAttr}'");
            Check(widget.Display == DisplayKind.Inline, $"@class 'topic/ph' даёт DisplayKind.Inline: {widget.Display}");
            Check(widget.Attributes.ContainsKey("id"), "атрибут id из %common-atts; попал в widget (раскрытие параметрической сущности в ATTLIST)");
            Check(widget.Model.AllowsText(), "контент-модель widget (#PCDATA) разобрана существующим ModelParser");

            var rootDef = result.Elements.FirstOrDefault(e => e.Name == "root");
            Check(rootDef is not null, "root найден");
            Check(rootDef!.ClassAttr == "- topic/topic ", $"@class у root: '{rootDef.ClassAttr}'");
            Check(rootDef.Display == DisplayKind.Topic, $"@class 'topic/topic' даёт DisplayKind.Topic: {rootDef.Display}");
            Check(rootDef.Model.CollectNames().Contains("widget"), "контент-модель root (widget+) ссылается на widget");

            // Merge — на отдельном экземпляре каталога, не на общем Default, чтобы не влиять на
            // остальные проверки; встроенные элементы при этом не теряются.
            var mergedCatalog = DitaCatalog.Load(CatalogSource.All);
            mergedCatalog.Merge(result.Elements);
            Check(mergedCatalog.Get("widget") is not null, "после Merge внешний элемент доступен через каталог");
            Check(mergedCatalog.Get("p") is not null, "после Merge встроенные элементы каталога никуда не делись");

            var missingWarnings = DtdCatalogLoader.Load(Path.Combine(root, "no-such-file.dtd"));
            Check(missingWarnings.Elements.Count == 0 && missingWarnings.Warnings.Count == 1,
                "загрузка несуществующего файла не падает, а даёт предупреждение и пустой результат");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static readonly (string? ClassAttr, DisplayKind Expected)[] DtdClassVariants =
    {
        ("+ topic/ph custom-d/inline-el ", DisplayKind.Inline),
        ("- topic/topic ", DisplayKind.Topic),
        ("+ topic/table custom-d/table-el ", DisplayKind.Table),
        ("+ topic/pre custom-d/pre-el ", DisplayKind.Preformatted),
        (null, DisplayKind.Block) // без @class — решает форма модели, см. ниже
    };

    /// <summary>Прогоняет тот же генератор контент-моделей (ModelSpec), что и
    /// ModelAutomatonPropertyTests, но теперь через весь путь ЧЕРЕЗ реальный DTD-текст:
    /// DtdReader (сущности, ELEMENT/ATTLIST) → DtdCatalogLoader (ElementDef, DisplayKind по
    /// @class) → ElementDef.Automaton — если где-то на этом пути модель или @class потеряются
    /// или исказятся, сгенерированная валидная последовательность перестанет проходить, а
    /// ожидаемый DisplayKind разойдётся с фактическим.</summary>
    private static void DtdCatalogLoaderPropertyTests()
    {
        Section("Property-based: DtdReader + DtdCatalogLoader (случайные DTD-фрагменты)");

        var rnd = new Random(20260921);
        var alphabet = new[] { "x1", "x2", "x3", "x4", "x5" };
        const int iterations = 60;
        var failures = 0;

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "PropDtd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    var elementCount = 2 + rnd.Next(3); // 2..4
                    var sb = new StringBuilder();
                    // Параметрическая сущность на общий атрибут — как в реальных модульных DTD
                    // DITA (%univ-atts; и т.п.), уже проверялась вручную в DtdCatalogLoaderTests.
                    sb.Append("<!ENTITY % common-atts \"id ID #IMPLIED\">\n");

                    var expectations = new List<(string Name, string ClassAttr, DisplayKind Expected, ModelSpec Model)>();

                    for (var e = 0; e < elementCount; e++)
                    {
                        var name = $"el{i}_{e}";
                        var spec = GenerateModelNode(rnd, alphabet, depth: 2);
                        var modelText = SerializeModelNode(spec);
                        sb.Append("<!ELEMENT ").Append(name).Append(' ').Append(modelText).Append(">\n");

                        var variant = DtdClassVariants[rnd.Next(DtdClassVariants.Length)];
                        var expectedDisplay = variant.ClassAttr is null
                            ? (ModelParser.Parse(modelText).AllowsText() ? DisplayKind.Block : DisplayKind.Container)
                            : variant.Expected;

                        sb.Append("<!ATTLIST ").Append(name).Append(" %common-atts;");
                        if (variant.ClassAttr is not null)
                        {
                            sb.Append(" class CDATA \"").Append(variant.ClassAttr).Append('"');
                        }

                        sb.Append(">\n");

                        expectations.Add((name, variant.ClassAttr ?? string.Empty, expectedDisplay, spec));
                    }

                    var dtdPath = Path.Combine(root, $"case{i}.dtd");
                    File.WriteAllText(dtdPath, sb.ToString());

                    var result = DtdCatalogLoader.Load(dtdPath);
                    if (result.Warnings.Count != 0)
                    {
                        failures++;
                        Failures.Add($"DtdCatalogLoader property: неожиданные предупреждения на случае {i}: {string.Join("; ", result.Warnings)}");
                        continue;
                    }

                    if (result.Elements.Count != expectations.Count)
                    {
                        failures++;
                        Failures.Add($"DtdCatalogLoader property: ожидалось {expectations.Count} элементов, получено {result.Elements.Count} (случай {i})");
                        continue;
                    }

                    foreach (var (name, classAttr, expectedDisplay, spec) in expectations)
                    {
                        var def = result.Elements.FirstOrDefault(el => el.Name == name);
                        if (def is null)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: элемент {name} не найден (случай {i})");
                            continue;
                        }

                        if (def.ClassAttr != classAttr)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: @class у {name} — ожидалось '{classAttr}', получено '{def.ClassAttr}' (случай {i})");
                        }

                        if (def.Display != expectedDisplay)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: DisplayKind у {name} — ожидалось {expectedDisplay}, получено {def.Display} (случай {i})");
                        }

                        if (!def.Attributes.ContainsKey("id"))
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: атрибут id из %common-atts; не попал в {name} (случай {i})");
                        }

                        var validSequence = GenerateValidSequence(spec, rnd);
                        if (!def.Automaton.Validate(validSequence, out _, out _))
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: контент-модель {name} не приняла собственную сгенерированную последовательность [{string.Join(",", validSequence)}] (случай {i})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Failures.Add($"DtdCatalogLoader property: исключение на случае {i}: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }

        Check(failures == 0,
            $"{iterations} случайных DTD-фрагментов (сущности + ELEMENT/ATTLIST + @class) разобраны и провалидированы без расхождений ({iterations - failures}/{iterations})");
    }

    private static void PluginLoaderTests()
    {
        Section("Плагины: загрузка сборки через AssemblyLoadContext");

        // tests/TestPlugin — не настоящий плагин, а компилируемая solution'ом заглушка (Private=false
        // на ссылке на DitaStudio.Core, как и полагается настоящему плагину) — единственный способ
        // проверить, что typeof(T).IsAssignableFrom(type) реально работает через границу отдельно
        // загруженной сборки, а не просто на бумаге.
        var testPluginDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestPlugin", "bin", "Debug", "net8.0");
        var testPluginDll = Path.Combine(testPluginDir, "TestPlugin.dll");

        if (!File.Exists(testPluginDll))
        {
            Console.WriteLine($"  (TestPlugin.dll не найден по {testPluginDll} — раздел пропущен; соберите весь DitaStudio.sln)");
            return;
        }

        var rules = PluginLoader.Load<IValidationRulePlugin>(testPluginDir);
        Check(rules.Warnings.Count == 0, "загрузка настоящей .dll без предупреждений: " + string.Join("; ", rules.Warnings));
        Check(rules.Instances.Count == 1, $"найдена ровно одна конкретная реализация IValidationRulePlugin (абстрактный класс пропущен): {rules.Instances.Count}");

        var rule = rules.Instances.FirstOrDefault();
        Check(rule?.Name == "TestPlugin.NoteFlaggingRule", $"Name загруженного плагина: '{rule?.Name}'");

        var doc = DitaDocument.Parse("""
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <note>Предупреждение.</note>
    <p>Обычный абзац.</p>
  </conbody>
</concept>
""");
        var issues = rule?.Check(doc).ToList() ?? new List<ValidationIssue>();
        Check(issues.Count == 1 && issues[0].Message.Contains("из тестового плагина"),
            "плагин из отдельно загруженной сборки реально выполняется и находит note в документе хоста");

        var formats = PluginLoader.Load<IPublishFormatPlugin>(testPluginDir);
        Check(formats.Instances.Count == 1 && formats.Instances[0].Name == "TestPlugin.NoopPublishFormat",
            "PluginLoader.Load<T> фильтрует ровно по запрошенному контракту, даже когда в той же сборке есть реализации другого");

        var missing = PluginLoader.Load<IValidationRulePlugin>(Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N")));
        Check(missing.Instances.Count == 0 && missing.Warnings.Count == 0, "несуществующая папка плагинов — пустой результат без ошибок");
    }

    /// <summary>Правило, которое всегда падает — проверяет, что DitaProject.ValidateAll не роняет
    /// всю проверку из-за одного сломанного плагина.</summary>
    private sealed class ThrowingRule : IValidationRulePlugin
    {
        public string Name => "ThrowingRule";

        public IEnumerable<ValidationIssue> Check(DitaDocument document) => throw new InvalidOperationException("нарочно сломан");
    }

    /// <summary>Локальная реализация контракта (не связана с tests/TestPlugin — та существует для
    /// проверки настоящей динамической загрузки, а тут просто нужен любой IValidationRulePlugin,
    /// чтобы проверить, что DitaProject.ValidateAll его вызывает и вливает результат).</summary>
    private sealed class NoteFlaggingTestRule : IValidationRulePlugin
    {
        public string Name => "NoteFlaggingTestRule";

        public IEnumerable<ValidationIssue> Check(DitaDocument document) =>
            document.Root.DescendantsAndSelf()
                .Where(n => n.Kind == NodeKind.Element && n.Name == "note")
                .Select(n => new ValidationIssue(IssueSeverity.Info, "[из тестового плагина] найден note", n, document.FilePath));
    }

    private static void DitaProjectValidateAllWithPluginsTests()
    {
        Section("DitaProject.ValidateAll с плагинами");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic1.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic1">
  <title>Тема 1</title>
  <conbody>
    <note>Предупреждение.</note>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "topic2.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic2">
  <title> </title>
  <conbody>
    <p>Без note.</p>
  </conbody>
</concept>
""");
            var project = new DitaProject(root);
            project.Scan();

            var withoutPlugins = project.ValidateAll();
            Check(!withoutPlugins.Any(i => i.Message.Contains("из тестового плагина")),
                "без плагинов правило из плагина не срабатывает");
            Check(withoutPlugins.Any(i => i.Message.Contains("Пустой заголовок")),
                "обычная проверка стиля находит пустой заголовок во втором файле (опорная точка для следующей проверки)");

            var noteRule = new NoteFlaggingTestRule();
            var withPlugin = project.ValidateAll(new IValidationRulePlugin[] { noteRule });
            Check(withPlugin.Count(i => i.Message.Contains("из тестового плагина")) == 1,
                "плагин находит note ровно в одном файле из двух");

            var withThrowingPlugin = project.ValidateAll(new IValidationRulePlugin[] { new ThrowingRule() });
            Check(withThrowingPlugin.Count(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("ThrowingRule") && i.Message.Contains("нарочно сломан")) == 2,
                "плагин падает на обоих файлах — по предупреждению на каждый, ни один не пропущен");
            Check(withThrowingPlugin.Any(i => i.Message.Contains("Пустой заголовок")),
                "обычная проверка стиля второго файла всё равно выполнилась, несмотря на падение плагина на первом");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void DitavalFlagTests()
    {
        Section("Подсветка .ditaval (action=\"flag\")");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <p audience="expert">Только для экспертов.</p>
    <p>Обычный абзац.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест подсветки</title>
  <topicref href="topic.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var options = new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out"),
                SingleFile = true
            };
            options.FlagConditions.Add(new DitavalFlagRule("audience", "expert", "red", "yellow", "bold", "orange"));

            var result = new HtmlPublisher(project).Publish(Path.Combine(root, "map.ditamap"), options);
            var html = File.ReadAllText(result.EntryFile);

            Check(Regex.IsMatch(html, "<p class=\"ditaval-flag\" style=\"[^\"]*\">Только для экспертов\\."),
                "class ditaval-flag стоит на абзаце с совпавшим атрибутом");
            Check(!Regex.IsMatch(html, "<p class=\"ditaval-flag\"[^>]*>Обычный абзац\\."),
                "обычный абзац подсветку не получил");

            var flaggedMatch = Regex.Match(html, "<p class=\"ditaval-flag\" style=\"([^\"]*)\">Только для экспертов");
            var style = flaggedMatch.Groups[1].Value;
            Check(style.Contains("color:red"), "цвет текста из правила flag попал в style");
            Check(style.Contains("background-color:yellow"), "цвет фона из правила flag попал в style");
            Check(style.Contains("font-weight:bold"), "начертание bold из правила flag попало в style");
            Check(style.Contains("border-left:3px solid orange"), "полоса изменений changebar из правила flag попала в style");

            // Правило без @val совпадает с любым значением атрибута.
            var wildcardOptions = new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-wildcard"),
                SingleFile = true
            };
            wildcardOptions.FlagConditions.Add(new DitavalFlagRule("audience", null, "green", null, null, null));
            var wildcardResult = new HtmlPublisher(project).Publish(Path.Combine(root, "map.ditamap"), wildcardOptions);
            var wildcardHtml = File.ReadAllText(wildcardResult.EntryFile);
            Check(wildcardHtml.Contains("color:green"), "правило flag без @val совпадает при любом значении атрибута");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // ------------------------------------------------- property-based: .ditaval

    /// <summary>Случайный токен-значение для .ditaval: гарантированно не пустой и не из одних
    /// пробелов (иначе DitavalReader сам его отбросит — это не баг, а другое инвариант), но может
    /// содержать спецсимволы XML (&amp;"'&lt;&gt;) и юникод — чтобы гонять Escape()/XmlReader.</summary>
    private static string RandomDitavalToken(Random rnd)
    {
        const string pool = "abcABC0123 _-.,:;!?()[]{}&<>\"'йцукенгшщзхъфывапролджэячсмитьбюЙЦУ中文한글";
        var length = 1 + rnd.Next(8);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = pool[rnd.Next(pool.Length)];
        }

        var s = new string(chars);
        return string.IsNullOrWhiteSpace(s) ? "x" + s : s;
    }

    private static Dictionary<string, HashSet<string>> GenerateRandomExclude(Random rnd)
    {
        var exclude = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var attrCount = 1 + rnd.Next(3);
        for (var a = 0; a < attrCount; a++)
        {
            var attr = RandomDitavalToken(rnd);
            if (!exclude.TryGetValue(attr, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                exclude[attr] = values;
            }

            var valueCount = 1 + rnd.Next(3);
            for (var v = 0; v < valueCount; v++)
            {
                values.Add(RandomDitavalToken(rnd));
            }
        }

        return exclude;
    }

    private static List<DitavalFlagRule> GenerateRandomFlags(Random rnd)
    {
        var count = rnd.Next(4); // 0..3 — включая случай без единого правила подсветки
        var flags = new List<DitavalFlagRule>();
        for (var i = 0; i < count; i++)
        {
            string? Optional(double skipChance) => rnd.NextDouble() < skipChance ? null : RandomDitavalToken(rnd);
            flags.Add(new DitavalFlagRule(
                RandomDitavalToken(rnd),
                Optional(0.3),
                Optional(0.4),
                Optional(0.4),
                Optional(0.4),
                Optional(0.4)));
        }

        return flags;
    }

    private static bool ExcludeEquals(Dictionary<string, HashSet<string>> a, Dictionary<string, HashSet<string>> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, values) in a)
        {
            if (!b.TryGetValue(key, out var otherValues) || !values.SetEquals(otherValues))
            {
                return false;
            }
        }

        return true;
    }

    private static void DitavalPropertyTests()
    {
        Section("Property-based: DitavalWriter + DitavalReader (случайные правила .ditaval)");

        var rnd = new Random(999);
        const int iterations = 150;
        var failures = 0;

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "PropDitaval_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "random.ditaval");

        try
        {
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    var exclude = GenerateRandomExclude(rnd);
                    var flags = GenerateRandomFlags(rnd);

                    DitavalWriter.Write(path, new DitavalRules(exclude, flags));
                    var reread = DitavalReader.Read(path);

                    if (!ExcludeEquals(exclude, reread.Exclude))
                    {
                        failures++;
                        Failures.Add($"Ditaval property: exclude не совпал после round-trip на случае {i}");
                        continue;
                    }

                    if (!flags.SequenceEqual(reread.Flags))
                    {
                        failures++;
                        Failures.Add($"Ditaval property: flag-правила не совпали после round-trip на случае {i}: " +
                            $"было [{string.Join(" | ", flags)}], стало [{string.Join(" | ", reread.Flags)}]");
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Failures.Add($"Ditaval property: исключение на случае {i}: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }

        Check(failures == 0,
            $"{iterations} случайных наборов правил .ditaval пережили Write→Read без потерь ({iterations - failures}/{iterations})");
    }

    private static void RefResolverTests()
    {
        Section("RefResolver: Parse/ResolvePath/FindTarget/ResolveConref/ExpandConrefs (по отчёту покрытия)");

        Check(RefResolver.IsExternal("http://example.com"), "IsExternal: http");
        Check(RefResolver.IsExternal("HTTPS://example.com"), "IsExternal регистронезависим");
        Check(RefResolver.IsExternal("mailto:a@b.com"), "IsExternal: mailto");
        Check(RefResolver.IsExternal("ftp://host/file"), "IsExternal: ftp");
        Check(!RefResolver.IsExternal("topic.dita"), "IsExternal: обычный относительный путь — не внешний");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var aPath = Path.Combine(root, "a.dita");
            var bPath = Path.Combine(root, "b.dita");
            var subDir = Path.Combine(root, "sub");
            Directory.CreateDirectory(subDir);
            var cPath = Path.Combine(subDir, "c.dita");

            File.WriteAllText(aPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="a">
  <title>A</title>
  <conbody>
    <p id="shared" outputclass="local">Локальный текст (не должен исчезнуть).</p>
    <p conref="b.dita#b/borrowed" audience="local-wins">Заглушка.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(bPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="b">
  <title>B</title>
  <conbody>
    <p id="borrowed" audience="borrowed-loses">Заимствуемый текст<i>с вложенным</i>.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(cPath, "<concept id=\"c\"><title>C</title><conbody><p>В подпапке.</p></conbody></concept>");

            var project = new DitaProject(root);
            project.Scan();
            var docA = project.GetDocument(aPath);

            // --- Parse
            var localFragment = RefResolver.Parse(aPath, "#shared");
            Check(localFragment.Path is null && localFragment.TopicId == "shared" && localFragment.IsLocalFragment,
                "Parse: '#id' — локальный фрагмент без файла (TopicId, IsLocalFragment)");

            var topicAndElement = RefResolver.Parse(aPath, "b.dita#b/borrowed");
            Check(topicAndElement.TopicId == "b" && topicAndElement.ElementId == "borrowed" && topicAndElement.Path == bPath,
                "Parse: 'файл#топик/элемент' разобран полностью");

            var topicOnly = RefResolver.Parse(aPath, "b.dita#b");
            Check(topicOnly.TopicId == "b" && topicOnly.ElementId is null, "Parse: 'файл#топик' без элемента — ElementId == null");

            var externalWithFragment = RefResolver.Parse(aPath, "https://example.com/x#y");
            Check(externalWithFragment.Path is null && externalWithFragment.TopicId == "y",
                "Parse: внешняя ссылка не резолвится в Path, но фрагмент всё равно разбирается");

            Check(RefResolver.Parse(aPath, "b.dita").ToString() == "b.dita", "DitaReference.ToString() возвращает исходную строку (Raw)");

            // --- ResolvePath
            Check(RefResolver.ResolvePath(aPath, "") is null, "ResolvePath: пустой href — null");
            Check(RefResolver.ResolvePath(aPath, "http://example.com") is null, "ResolvePath: внешний href — null");
            Check(RefResolver.ResolvePath(aPath, "#anything") == Path.GetFullPath(aPath),
                "ResolvePath: '#fragment' без файла — путь самого базового файла");
            Check(RefResolver.ResolvePath(aPath, "sub/c.dita") == Path.GetFullPath(cPath), "ResolvePath: относительный путь в подпапку");
            Check(RefResolver.ResolvePath(aPath, "sub/c.dita#c") == Path.GetFullPath(cPath), "ResolvePath: фрагмент отбрасывается при резолве пути файла");

            // --- MakeRelative
            Check(RefResolver.MakeRelative(aPath, cPath) == "sub/c.dita", "MakeRelative: путь в подпапку, со слешем вперёд (не '\\\\')");
            Check(RefResolver.MakeRelative(cPath, aPath) == "../a.dita", "MakeRelative: путь из подпапки наверх");

            // --- FindTarget / FindById
            Check(ReferenceEquals(RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "#shared")), docA.Root.FindDescendant("p")),
                "FindTarget: локальный фрагмент ищет в исходном документе");
            Check(RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "no-such-file.dita#x")) is null,
                "FindTarget: файл не существует — null");
            var targetRoot = RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "b.dita"));
            Check(targetRoot?.Name == "concept" && targetRoot.GetAttribute("id") == "b",
                "FindTarget: ссылка без фрагмента — корень целевого документа");
            Check(RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "b.dita#no-such-topic")) is null,
                "FindTarget: топик с таким id не найден — null");
            var targetElement = RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "b.dita#b/borrowed"));
            Check(targetElement?.GetAttribute("id") == "borrowed", "FindTarget: топик и элемент оба найдены");
            Check(RefResolver.FindTarget(project, docA, RefResolver.Parse(aPath, "b.dita#b/no-such-elem")) is null,
                "FindTarget: элемент с таким id внутри топика не найден — null");

            // --- ResolveConref (href-style)
            var conrefNode = docA.Root.DescendantsAndSelf().First(n => n.HasAttribute("conref"));
            var resolvedConref = RefResolver.ResolveConref(project, docA, conrefNode);
            Check(resolvedConref?.GetAttribute("id") == "borrowed", "ResolveConref: обычный conref резолвится через Parse+FindTarget");

            var noConrefNode = docA.Root.FindDescendant("title")!;
            Check(RefResolver.ResolveConref(project, docA, noConrefNode) is null,
                "ResolveConref: узел без conref/conkeyref — null");

            // --- ResolveConref (conkeyref-style) — нужен реальный keydef в карте
            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>T</title>
  <keydef keys="shared-key" href="b.dita"/>
  <topicref href="a.dita"/>
  <topicref href="b.dita"/>
</map>
""");
            project.Scan();
            docA = project.GetDocument(aPath);

            var conkeyrefWholeNode = DitaNode.Element("p");
            conkeyrefWholeNode.SetAttribute("conkeyref", "shared-key");
            var conkeyrefWhole = RefResolver.ResolveConref(project, docA, conkeyrefWholeNode);
            Check(conkeyrefWhole?.GetAttribute("id") == "b", "ResolveConref: conkeyref без '/' — корень целевого топика по ключу");

            var conkeyrefElementNode = DitaNode.Element("p");
            conkeyrefElementNode.SetAttribute("conkeyref", "shared-key/borrowed");
            var conkeyrefElement = RefResolver.ResolveConref(project, docA, conkeyrefElementNode);
            Check(conkeyrefElement?.GetAttribute("id") == "borrowed", "ResolveConref: conkeyref с '/элемент' находит элемент внутри топика по ключу");

            var conkeyrefUnknownNode = DitaNode.Element("p");
            conkeyrefUnknownNode.SetAttribute("conkeyref", "no-such-key");
            Check(RefResolver.ResolveConref(project, docA, conkeyrefUnknownNode) is null,
                "ResolveConref: conkeyref на несуществующий ключ — null");

            // --- ExpandConrefs: локальные атрибуты побеждают, id заимствованного элемента снят,
            // вложенное фразовое содержимое (i) пришло вместе с текстом.
            var expanded = RefResolver.ExpandConrefs(project, docA);
            var expandedConrefNode = expanded.Root.DescendantsAndSelf().First(n => n.GetAttribute("audience") is not null);
            Check(expandedConrefNode.GetAttribute("id") is null, "ExpandConrefs: id заимствованного элемента снят (не задваивается)");
            Check(expandedConrefNode.GetAttribute("audience") == "local-wins",
                "ExpandConrefs: атрибут локального элемента (audience) побеждает атрибут из источника");
            Check(expandedConrefNode.InnerText.Contains("Заимствуемый текст") && expandedConrefNode.FindDescendant("i") is not null,
                "ExpandConrefs: содержимое источника (включая вложенный <i>) скопировано целиком");
            Check(!expandedConrefNode.HasAttribute("conref"), "ExpandConrefs: атрибут conref в результат не переносится");
            Check(docA.Root.DescendantsAndSelf().First(n => n.HasAttribute("conref")).HasAttribute("conref"),
                "ExpandConrefs возвращает копию — исходный документ не тронут (conref остался)");

            // --- ValidateReferences
            var brokenHrefDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p><xref href=\"nowhere.dita\"/></p></conbody></concept>");
            brokenHrefDoc.FilePath = aPath;
            var brokenIssues = RefResolver.ValidateReferences(project, brokenHrefDoc);
            Check(brokenIssues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("не найден")),
                "ValidateReferences: href на несуществующий файл — ошибка");

            var externalScopeDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p><xref href=\"nowhere.dita\" scope=\"external\"/></p></conbody></concept>");
            externalScopeDoc.FilePath = aPath;
            Check(RefResolver.ValidateReferences(project, externalScopeDoc).Count == 0,
                "ValidateReferences: scope=\"external\" пропускает проверку файла, даже если href похож на локальный");

            var pdfFormatDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p><xref href=\"doc.pdf#section1\" format=\"pdf\"/></p></conbody></concept>");
            pdfFormatDoc.FilePath = aPath;
            File.WriteAllText(Path.Combine(root, "doc.pdf"), "не настоящий pdf, но файл существует");
            Check(RefResolver.ValidateReferences(project, pdfFormatDoc).Count == 0,
                "ValidateReferences: format=\"pdf\" — фрагмент (#section1) не проверяется как id топика");

            var unknownKeyDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p><xref keyref=\"no-such-key\"/></p></conbody></concept>");
            unknownKeyDoc.FilePath = aPath;
            Check(RefResolver.ValidateReferences(project, unknownKeyDoc).Any(i => i.Message.Contains("не объявлен")),
                "ValidateReferences: keyref на необъявленный ключ — ошибка");

            var unresolvedConkeyrefDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p conkeyref=\"no-such-key\"/></conbody></concept>");
            unresolvedConkeyrefDoc.FilePath = aPath;
            Check(RefResolver.ValidateReferences(project, unresolvedConkeyrefDoc).Any(i => i.Message.Contains("Не удалось разрешить conkeyref")),
                "ValidateReferences: неразрешимый conkeyref — ошибка");

            var missingTopicIdDoc = DitaDocument.Parse("<concept id=\"x\"><title>T</title><conbody><p><xref href=\"b.dita#no-such-topic\"/></p></conbody></concept>");
            missingTopicIdDoc.FilePath = aPath;
            Check(RefResolver.ValidateReferences(project, missingTopicIdDoc).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Не найден целевой элемент")),
                "ValidateReferences: файл существует, но id топика внутри — нет: предупреждение (не ошибка)");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // -------------------------------------------------------------- рефакторинг

    private static void RefactorTests()
    {
        Section("Рефакторинг: переименование id и перенос файла");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var aPath = Path.Combine(root, "a.dita");
            var bPath = Path.Combine(root, "b.dita");

            File.WriteAllText(aPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="a">
  <title>A</title>
  <conbody>
    <p id="para1">Текст с <xref href="b.dita#topic-b/elem1">ссылкой на элемент</xref>.</p>
  </conbody>
</concept>
""");
            File.WriteAllText(bPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-b">
  <title>B</title>
  <conbody>
    <p id="elem1">Целевой абзац.</p>
    <p>Топик <xref href="a.dita#a">A</xref>, абзац <xref href="a.dita#a/para1">para1</xref>.</p>
  </conbody>
</concept>
""");

            var project = new DitaProject(root);
            project.Scan();

            // --- переименование id элемента (elementId в фрагменте) ---
            var renameElem = RefactorService.RenameId(project, aPath, "para1", "intro-note");
            Check(renameElem.UpdatedReferences == 1, $"переименование elementId обновило одну ссылку: {renameElem.UpdatedReferences}");
            Check(project.GetDocument(aPath).Root.FindDescendant("p")?.GetAttribute("id") == "intro-note",
                "сам элемент переименован");
            var bDoc1 = project.GetDocument(bPath);
            var xrefToElem = bDoc1.Root.DescendantsAndSelf().First(n => n.GetAttribute("href")?.Contains("para1") == true || n.GetAttribute("href")?.Contains("intro-note") == true);
            Check(xrefToElem.GetAttribute("href") == "a.dita#a/intro-note", $"ссылка на элемент обновлена: {xrefToElem.GetAttribute("href")}");
            var xrefToTopicOnly = bDoc1.Root.DescendantsAndSelf().First(n => n.GetAttribute("href") == "a.dita#a");
            Check(xrefToTopicOnly is not null, "ссылка на сам топик (без elementId) не пострадала от переименования абзаца");

            // --- переименование id топика (topicId в фрагменте) ---
            var renameTopic = RefactorService.RenameId(project, bPath, "topic-b", "topic-beta");
            Check(renameTopic.UpdatedReferences == 1, $"переименование topicId обновило одну ссылку: {renameTopic.UpdatedReferences}");
            var aDoc = project.GetDocument(aPath);
            var xrefToTopic = aDoc.Root.DescendantsAndSelf().First(n => n.Name == "xref");
            Check(xrefToTopic.GetAttribute("href") == "b.dita#topic-beta/elem1",
                $"ссылка на топик обновлена с сохранением elementId: {xrefToTopic.GetAttribute("href")}");

            // Сохраняем на диск перед проверкой переноса файла.
            project.GetDocument(aPath).Save(aPath);
            project.GetDocument(bPath).Save(bPath);

            // --- перенос файла в подпапку ---
            var newBPath = Path.Combine(root, "sub", "b.dita");
            var move = RefactorService.MoveFile(project, bPath, newBPath);
            Check(File.Exists(newBPath), "файл физически перенесён по новому пути");
            Check(!File.Exists(bPath), "старый файл удалён");
            Check(move.ChangedDocuments.Count == 2, $"перенос затронул исходный и ссылающийся документ: {move.ChangedDocuments.Count}");

            foreach (var changedDoc in move.ChangedDocuments)
            {
                changedDoc.Save(changedDoc.FilePath);
            }

            project.Scan();

            var aText = File.ReadAllText(aPath);
            Check(aText.Contains("href=\"sub/b.dita#topic-beta/elem1\""),
                "входящая ссылка на перенесённый файл пересчитана с учётом новой папки");

            var bText = File.ReadAllText(newBPath);
            Check(bText.Contains("href=\"../a.dita#a\""),
                "исходящая ссылка перенесённого файла пересчитана от нового расположения");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void ExtractToConrefTests()
    {
        Section("Рефакторинг: вынесение в conref");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var aPath = Path.Combine(root, "a.dita");
            var bPath = Path.Combine(root, "b.dita");

            File.WriteAllText(aPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-a">
  <title>A</title>
  <conbody>
    <note id="warn1">Общее предупреждение.</note>
  </conbody>
</concept>
""");
            File.WriteAllText(bPath, """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic-b">
  <title>B</title>
  <conbody>
    <p>Текст.</p>
  </conbody>
</concept>
""");

            var project = new DitaProject(root);
            project.Scan();

            // --- вынесение в существующий топик ---
            var noteInA = project.GetDocument(aPath).Root.FindDescendant("note")!;
            var result = RefactorService.ExtractToConref(project, aPath, noteInA, "warn1", bPath, null);
            Check(result.UpdatedReferences == 1, "вынесение в существующий топик выполнено");
            Check(result.ChangedDocuments.Count == 2, $"затронуты оба документа: {result.ChangedDocuments.Count}");
            foreach (var changedDoc in result.ChangedDocuments)
            {
                changedDoc.Save(changedDoc.FilePath!);
            }

            var stub = File.ReadAllText(aPath);
            Check(stub.Contains("<note conref=\"b.dita#topic-b/warn1\"") && !stub.Contains("Общее предупреждение"),
                $"на исходном месте осталась пустая ссылка conref: {stub}");
            var target = File.ReadAllText(bPath);
            Check(target.Contains("<note id=\"warn1\">Общее предупреждение.</note>"),
                $"содержимое перенесено в целевой топик: {target}");

            // --- вынесение в новый файл ---
            project.Scan();
            var pInB = project.GetDocument(bPath).Root.FindDescendant("p")!;
            var newFileResult = RefactorService.ExtractToConref(
                project, bPath, pInB, "shared_text", null, "shared/reuse.dita");
            Check(newFileResult.UpdatedReferences == 1, "вынесение в новый файл выполнено");
            var newFilePath = Path.Combine(root, "shared", "reuse.dita");
            Check(File.Exists(newFilePath), "новый файл создан на диске");
            foreach (var changedDoc in newFileResult.ChangedDocuments)
            {
                changedDoc.Save(changedDoc.FilePath!);
            }

            var newFileText = File.ReadAllText(newFilePath);
            Check(newFileText.Contains("<p id=\"shared_text\">Текст.</p>") && !newFileText.Contains("<p></p>"),
                $"содержимое перенесено в новый файл без плейсхолдера: {newFileText}");
            var bText = File.ReadAllText(bPath);
            Check(bText.Contains("conref=\"shared/reuse.dita#"), $"ссылка на новый файл проставлена: {bText}");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void PdfExporterTests()
    {
        Section("Экспорт в PDF через headless-браузер (по отчёту покрытия)");

        var browser = PdfExporter.FindBrowser();
        Check(PdfExporter.IsAvailable == (browser is not null), "IsAvailable согласован с FindBrowser()");

        if (browser is null)
        {
            Console.WriteLine("  (Edge/Chrome не найден по стандартным путям — остальные проверки раздела пропущены)");
            return;
        }

        Check(File.Exists(browser), $"FindBrowser вернул существующий исполняемый файл: {browser}");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var htmlPath = Path.Combine(root, "page.html");
            File.WriteAllText(htmlPath, "<html><body><h1>Проверка PdfExporter</h1></body></html>");
            var pdfPath = Path.Combine(root, "out.pdf");

            var error = PdfExporter.ExportToPdf(htmlPath, pdfPath, timeoutSeconds: 60);
            Check(error is null, $"ExportToPdf не сообщил об ошибке: {error}");
            Check(File.Exists(pdfPath), "PDF-файл создан");

            var header = new byte[5];
            using (var stream = File.OpenRead(pdfPath))
            {
                stream.ReadExactly(header);
            }

            Check(System.Text.Encoding.ASCII.GetString(header) == "%PDF-", "созданный файл начинается с настоящей PDF-сигнатуры");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void DiffTests()
    {
        Section("Сравнение файлов (построчный diff)");

        var same = XmlDiff.Compare("a\nb\nc", "a\nb\nc");
        Check(same.All(d => d.Kind == DiffKind.Equal), "идентичный текст — все строки совпадают");

        var changed = XmlDiff.Compare("a\nb\nc", "a\nX\nc");
        Check(changed.Count(d => d.Kind == DiffKind.Removed) == 1 && changed.Count(d => d.Kind == DiffKind.Added) == 1,
            $"изменённая строка — одно удаление и одно добавление: {string.Join(",", changed.Select(d => d.Kind))}");
        Check(changed[0].Kind == DiffKind.Equal && changed[^1].Kind == DiffKind.Equal,
            "общие строки вокруг изменения остались Equal");

        var addedOnly = XmlDiff.Compare("a\nc", "a\nb\nc");
        Check(addedOnly.Count(d => d.Kind == DiffKind.Added) == 1 && addedOnly.Count(d => d.Kind == DiffKind.Removed) == 0,
            "добавленная строка распознана без ложного удаления");

        var removedOnly = XmlDiff.Compare("a\nb\nc", "a\nc");
        Check(removedOnly.Count(d => d.Kind == DiffKind.Removed) == 1 && removedOnly.Count(d => d.Kind == DiffKind.Added) == 0,
            "удалённая строка распознана без ложного добавления");
    }

    private static void GitHistoryTests()
    {
        Section("Сравнение с git-историей");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunExternalOrSkip("git", root, "init");
            if (!Directory.Exists(Path.Combine(root, ".git")))
            {
                Console.WriteLine("  (git недоступен в окружении — раздел пропущен)");
                return;
            }

            RunExternalOrSkip("git", root, "config", "user.email", "test@example.com");
            RunExternalOrSkip("git", root, "config", "user.name", "Test");

            var tracked = Path.Combine(root, "topic.dita");
            File.WriteAllText(tracked, "версия из коммита");
            RunExternalOrSkip("git", root, "add", "topic.dita");
            RunExternalOrSkip("git", root, "commit", "-m", "начальный коммит");

            File.WriteAllText(tracked, "рабочая копия, ещё не закоммичена");

            var headContent = GitHistory.ReadRevision(tracked);
            Check(headContent?.Trim() == "версия из коммита", "ReadRevision вернул содержимое из HEAD, а не рабочей копии");
            Check(GitHistory.IsInRepository(tracked), "файл внутри git-репозитория распознан");

            var untracked = Path.Combine(root, "untracked.dita");
            File.WriteAllText(untracked, "не в истории");
            Check(GitHistory.ReadRevision(untracked) is null, "неотслеживаемый файл — ReadRevision возвращает null");

            var outsideRepo = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + ".dita");
            Check(!GitHistory.IsInRepository(outsideRepo), "файл вне репозитория git не распознан как отслеживаемый");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void SvnHistoryTests()
    {
        Section("Сравнение с историей SVN");

        var repoPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-svn-repo");
        var wcPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-svn-wc");

        try
        {
            RunExternalOrSkip("svnadmin", Path.GetTempPath(), "create", repoPath);
            if (!Directory.Exists(Path.Combine(repoPath, "conf")))
            {
                Check(!SvnHistory.IsInRepository(Path.Combine(Path.GetTempPath(), "nonexistent.dita")),
                    "svn недоступен — IsInRepository не падает, возвращает false");
                Console.WriteLine("  (svn недоступен в окружении — остальные проверки раздела пропущены)");
                return;
            }

            Directory.CreateDirectory(wcPath);
            var repoUrl = "file:///" + repoPath.Replace('\\', '/');
            RunExternalOrSkip("svn", wcPath, "checkout", repoUrl, ".");
            if (!Directory.Exists(Path.Combine(wcPath, ".svn")))
            {
                Console.WriteLine("  (svn checkout не удался — остальные проверки раздела пропущены)");
                return;
            }

            var tracked = Path.Combine(wcPath, "topic.dita");
            File.WriteAllText(tracked, "версия из репозитория");
            RunExternalOrSkip("svn", wcPath, "add", "topic.dita");
            RunExternalOrSkip("svn", wcPath, "commit", "-m", "начальный коммит");

            File.WriteAllText(tracked, "рабочая копия, ещё не закоммичена");

            var baseContent = SvnHistory.ReadRevision(tracked);
            Check(baseContent?.Trim() == "версия из репозитория", "ReadRevision вернул содержимое BASE, а не рабочей копии");
            Check(SvnHistory.IsInRepository(tracked), "файл под версионным контролем распознан");

            var untracked = Path.Combine(wcPath, "untracked.dita");
            File.WriteAllText(untracked, "не в истории");
            Check(SvnHistory.ReadRevision(untracked) is null, "неотслеживаемый файл — ReadRevision возвращает null");

            var outsideWc = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + ".dita");
            Check(!SvnHistory.IsInRepository(outsideWc), "файл вне рабочей копии svn не распознан как отслеживаемый");
        }
        finally
        {
            try
            {
                Directory.Delete(repoPath, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }

            try
            {
                Directory.Delete(wcPath, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void RunExternalOrSkip(string executable, string workingDirectory, params string[] arguments)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = System.Diagnostics.Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch
        {
            // клиент не установлен — вызывающий тест сам обнаружит отсутствие результата и пропустит раздел
        }
    }

    // ---------------------------------------------------------------- DOCX

    private static void DocxTests()
    {
        Section("Экспорт в DOCX");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="intro">
  <title>Введение<fn>Сноска про введение.</fn></title>
  <conbody>
    <p>Смотрите <xref href="install.dita#install">установку</xref>. Ключ: <keyword keyref="product-name"/>.</p>
    <table>
      <tgroup cols="2">
        <colspec colname="c1" colnum="1" colwidth="1*"/>
        <colspec colname="c2" colnum="2" colwidth="1*"/>
        <thead><row><entry colname="c1">A</entry><entry colname="c2">B</entry></row></thead>
        <tbody>
          <row><entry colname="c1" morerows="1">R1</entry><entry colname="c2">X</entry></row>
          <row><entry colname="c2">Y</entry></row>
        </tbody>
      </tgroup>
    </table>
    <note type="tip">Совет.</note>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "install.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="install">
  <title>Установка</title>
  <taskbody>
    <steps>
      <step><cmd>Первый шаг</cmd></step>
      <step><cmd>Второй шаг</cmd></step>
      <step><cmd>Третий шаг</cmd><info>Пояснение без обёртки p.</info><stepresult>Результат без обёртки p.</stepresult></step>
    </steps>
  </taskbody>
</task>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест DOCX</title>
  <keydef keys="product-name"><topicmeta><keywords><keyword>Пример</keyword></keywords></topicmeta></keydef>
  <topicref href="intro.dita"/>
  <topicref href="install.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var outFile = Path.Combine(root, "out.docx");
            var publisher = new DocxPublisher(project);
            var result = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);

            Check(result.Warnings.Count == 0, "экспорт в DOCX прошёл без предупреждений: " + string.Join("; ", result.Warnings));
            Check(File.Exists(outFile), "файл .docx создан");

            using var doc = WordprocessingDocument.Open(outFile, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Пример"), "ключ product-name подставлен в DOCX");
            Check(text.Contains("Установка"), "заголовок второго топика попал в DOCX");
            Check(text.Contains("Пояснение без обёртки p."), "info с голым текстом (без <p>) не потерян в DOCX");
            Check(text.Contains("Результат без обёртки p."), "stepresult с голым текстом (без <p>) не потерян в DOCX");

            var footnotesPart = doc.MainDocumentPart.FootnotesPart;
            var realFootnotes = footnotesPart?.Footnotes?.Elements<Footnote>().Count(f => (f.Id?.Value ?? 0) > 0) ?? 0;
            Check(realFootnotes == 1, $"настоящая сноска Word создана: {realFootnotes}");
            Check(body.Descendants<FootnoteReference>().Count() == 1, "ссылка на сноску вставлена в текст");

            var hyperlinks = body.Descendants<Hyperlink>().ToList();
            Check(hyperlinks.Count == 1 && hyperlinks[0].Anchor is not null,
                "перекрёстная ссылка стала внутренней гиперссылкой на закладку");

            var table = body.Descendants<Table>().First();
            var rows = table.Elements<TableRow>().ToList();
            Check(rows.Count == 3, "в таблице три строки (шапка + 2 строки тела)");

            var bodyRow1Cells = rows[1].Elements<TableCell>().ToList();
            var bodyRow2Cells = rows[2].Elements<TableCell>().ToList();
            Check(bodyRow1Cells[0].TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Restart,
                "объединение ячеек по вертикали начато (Restart)");
            Check(bodyRow2Cells.Count == 2 &&
                  bodyRow2Cells[0].TableCellProperties?.GetFirstChild<VerticalMerge>()?.Val?.Value == MergedCellValues.Continue,
                "вторая строка таблицы получила ячейку-продолжение объединения");

            var tocField = body.Descendants<SimpleField>().FirstOrDefault(f => f.Instruction?.Value?.Contains("TOC") == true);
            Check(tocField is not null, "поле оглавления (TOC) добавлено");

            var headings = body.Elements<Paragraph>()
                .Count(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading") == true);
            Check(headings == 2, $"оба топика получили заголовки-абзацы: {headings}");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    /// <summary>Минимальный (нерабочий как изображение, но валидный по заголовку) PNG —
    /// сигнатура + честный IHDR с шириной/высотой; ImageSize читает только эти байты,
    /// пиксельные данные и CRC ей не нужны.</summary>
    private static byte[] BuildMinimalPng(int width, int height)
    {
        var bytes = new List<byte>(33);
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void AppendBigEndian(int value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        AppendBigEndian(13);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
        AppendBigEndian(width);
        AppendBigEndian(height);
        bytes.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        AppendBigEndian(0);
        return bytes.ToArray();
    }

    private static byte[] BuildMinimalGif(int width, int height)
    {
        var bytes = new List<byte> { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' };
        bytes.Add((byte)(width & 0xFF));
        bytes.Add((byte)((width >> 8) & 0xFF));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)((height >> 8) & 0xFF));
        while (bytes.Count < 24)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>signedHeight отрицательный проверяет ветку Math.Abs в ReadPixelSize.</summary>
    private static byte[] BuildMinimalBmp(int width, int signedHeight)
    {
        var bytes = new List<byte>(30) { (byte)'B', (byte)'M' };
        while (bytes.Count < 18)
        {
            bytes.Add(0);
        }

        bytes.AddRange(BitConverter.GetBytes(width));
        bytes.AddRange(BitConverter.GetBytes(signedHeight));
        while (bytes.Count < 30)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    /// <summary>SOI + один незначащий APP0-сегмент (проверяет пропуск сегмента по длине) + SOF0 с
    /// шириной/высотой — ReadJpegSize возвращает результат сразу после SOF0, дальше можно не писать.</summary>
    private static byte[] BuildMinimalJpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange(new byte[14]); // тело APP0 — 16 (длина) - 2 = 14 байт, содержимое неважно
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11 });
        bytes.Add(8); // precision
        bytes.Add((byte)((height >> 8) & 0xFF));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)((width >> 8) & 0xFF));
        bytes.Add((byte)(width & 0xFF));
        return bytes.ToArray();
    }

    private static void ImageSizeFormatsTests()
    {
        Section("ImageSize через DocxRenderer.RenderImage: GIF/BMP/JPEG, единицы измерения (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "pic.gif"), BuildMinimalGif(160, 90));
            File.WriteAllBytes(Path.Combine(root, "pic.bmp"), BuildMinimalBmp(320, -240));
            File.WriteAllBytes(Path.Combine(root, "pic.jpg"), BuildMinimalJpeg(400, 300));
            File.WriteAllBytes(Path.Combine(root, "unit.png"), BuildMinimalPng(1000, 500));

            File.WriteAllText(Path.Combine(root, "topic.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="t">
  <title>T</title>
  <conbody>
    <p><image href="pic.gif"/></p>
    <p><image href="pic.bmp"/></p>
    <p><image href="pic.jpg"/></p>
    <p><image href="unit.png" width="2cm"/></p>
    <p><image href="unit.png" width="10mm"/></p>
    <p><image href="unit.png" width="36pt"/></p>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map><title>T</title><topicref href="topic.dita"/></map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var outFile = Path.Combine(root, "out.docx");
            var result = new DocxPublisher(project).Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { Language = "ru" }, outFile);
            Check(result.Warnings.Count == 0, "публикация без предупреждений: " + string.Join("; ", result.Warnings));

            using var doc = WordprocessingDocument.Open(outFile, false);
            var drawings = doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToList();
            Check(drawings.Count == 6, $"все 6 изображений встроены: {drawings.Count}");

            const long emuPerInch = 914400;
            const long emuPerPixel = emuPerInch / 96;

            Check(drawings[0].Extent!.Cx!.Value == 160L * emuPerPixel && drawings[0].Extent!.Cy!.Value == 90L * emuPerPixel,
                $"GIF: размер прочитан из заголовка (160x90): {drawings[0].Extent!.Cx},{drawings[0].Extent!.Cy}");
            Check(drawings[1].Extent!.Cx!.Value == 320L * emuPerPixel && drawings[1].Extent!.Cy!.Value == 240L * emuPerPixel,
                $"BMP: размер прочитан из заголовка, отрицательная высота стала положительной через Math.Abs (320x240): {drawings[1].Extent!.Cx},{drawings[1].Extent!.Cy}");
            Check(drawings[2].Extent!.Cx!.Value == 400L * emuPerPixel && drawings[2].Extent!.Cy!.Value == 300L * emuPerPixel,
                $"JPEG: размер прочитан из маркера SOF0 после пропуска APP0 (400x300): {drawings[2].Extent!.Cx},{drawings[2].Extent!.Cy}");

            Check(drawings[3].Extent!.Cx!.Value == (long)(2 * emuPerInch / 2.54), $"width=\"2cm\" переведён в EMU через дюймы/2.54: {drawings[3].Extent!.Cx}");
            Check(drawings[4].Extent!.Cx!.Value == (long)(10 * emuPerInch / 25.4), $"width=\"10mm\" переведён в EMU через дюймы/25.4: {drawings[4].Extent!.Cx}");
            Check(drawings[5].Extent!.Cx!.Value == (long)(36 * emuPerInch / 72), $"width=\"36pt\" переведён в EMU через дюймы/72 (0.5in = 457200 EMU): {drawings[5].Extent!.Cx}");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    /// <summary>
    /// Элементы публикации, которых нет в основных DocxTests/HtmlPublisher-проверках:
    /// figure/dl/parml/simpletable/properties/choicetable, image (естественный размер, явные
    /// width/height, слишком широкое изображение — обрезка по MaxWidthEmu, отсутствующий файл,
    /// неподдерживаемый формат, внешняя ссылка), xref (внешний, неразрешённый, keyref без href).
    /// Один и тот же проект публикуется и в HTML, и в DOCX — расхождения в поведении между
    /// рендерерами (например, внешние изображения) фиксируются как есть, не как баг.
    /// </summary>
    private static void AdvancedRenderingTests()
    {
        Section("HTML/DOCX: figure/dl/parml/simpletable/image/xref (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "real.png"), BuildMinimalPng(200, 100));
            File.WriteAllBytes(Path.Combine(root, "wide.png"), BuildMinimalPng(2000, 1000));
            File.WriteAllBytes(Path.Combine(root, "pic.webp"), new byte[] { 1, 2, 3, 4 });

            File.WriteAllText(Path.Combine(root, "second.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="second">
  <title>Второй топик</title>
  <conbody><p>Текст.</p></conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "advanced.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="advanced">
  <title>Продвинутые элементы</title>
  <conbody>
    <fig><title>Схема</title>
      <p>Внутри рисунка.</p>
      <desc>Описание рисунка.</desc>
    </fig>
    <dl>
      <dlhead><dthd>Термин</dthd><ddhd>Значение</ddhd></dlhead>
      <dlentry><dt>Ключ</dt><dd>Значение ключа</dd></dlentry>
    </dl>
    <parml>
      <plentry><pt>-x</pt><pd>Включить X</pd></plentry>
    </parml>
    <simpletable>
      <sthead><stentry>H1</stentry><stentry>H2</stentry></sthead>
      <strow><stentry>A1</stentry><stentry>B1</stentry></strow>
    </simpletable>
    <properties>
      <prophead><proptypehd>Тип</proptypehd><propvaluehd>Значение</propvaluehd><propdeschd>Описание</propdeschd></prophead>
      <property><proptype>color</proptype><propvalue>red</propvalue><propdesc>Красный</propdesc></property>
    </properties>
    <choicetable>
      <chhead><choptionhd>Опция</choptionhd><chdeschd>Описание</chdeschd></chhead>
      <chrow><choption>A</choption><chdesc>Вариант A</chdesc></chrow>
    </choicetable>
    <p>Без размеров: <image href="real.png"/></p>
    <p>Явные размеры: <image href="real.png" width="2in" height="1in"/></p>
    <p>Слишком широкое: <image href="wide.png"/></p>
    <p>Через keyref: <image keyref="img-key"/></p>
    <p>Нет файла: <image href="missing.png"/></p>
    <p>Неподдерживаемый формат: <image href="pic.webp"/></p>
    <p>Внешнее: <image href="https://example.com/pic.png"/></p>
    <p>Внешняя ссылка: <xref href="https://example.com" scope="external">Сайт</xref></p>
    <p>Неразрешённая: <xref href="nowhere.dita#topic"/></p>
    <p>По ключу без href: <xref keyref="label-key"/></p>
    <related-links>
      <link href="second.dita#second"/>
      <link href="https://example.com" scope="external"><linktext>Внешний сайт</linktext></link>
    </related-links>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест расширенного рендера</title>
  <keydef keys="img-key" href="real.png"/>
  <keydef keys="label-key"><topicmeta><keywords><keyword>Голый текст</keyword></keywords></topicmeta></keydef>
  <topicref href="advanced.dita"/>
  <topicref href="second.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var mapPath = Path.Combine(root, "guide.ditamap");

            // --------------------------------------------------------------- HTML
            var htmlResult = new HtmlPublisher(project).Publish(mapPath, new PublishOptions { OutputDirectory = Path.Combine(root, "html-out"), SingleFile = true });
            var html = File.ReadAllText(htmlResult.EntryFile);

            Check(html.Contains("<figcaption class=\"fig-title\">Рисунок 1. Схема</figcaption>"), "html: подпись рисунка пронумерована");
            Check(html.Contains("<div class=\"desc\">Описание рисунка.</div>"), "html: desc рисунка отрисован");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<dt>Ключ</dt>\\s*<dd>Значение ключа</dd>"), "html: dlentry (dt/dd) отрисован");
            Check(html.Contains("class=\"dlhead\""), "html: dlhead отрисован");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<dt>-x</dt>\\s*<dd>Включить X</dd>"), "html: parml (pt/pd как dt/dd) отрисован");
            Check(html.Contains("H1") && html.Contains("A1"), "html: generic simpletable отрисован");
            Check(html.Contains("color") && html.Contains("red") && html.Contains("Красный"), "html: properties отрисован");
            Check(html.Contains("Вариант A"), "html: choicetable отрисован");

            // CopyImages по умолчанию включён — HtmlPublisher копирует файлы в media/ и переписывает
            // src, кешируя по абсолютному исходному пути (real.png использован трижды и должен
            // трижды сослаться на один и тот же скопированный файл).
            Check(html.Contains("<img src=\"media/real.png\" alt=\"\" />"), "html: изображение без width/height скопировано в media/, атрибуты размера не добавлены");
            Check(html.Contains("width=\"2in\"") && html.Contains("height=\"1in\""), "html: явные width/height перенесены в <img> как есть (без пересчёта в EMU — это забота DOCX)");
            Check(html.Contains("src=\"media/wide.png\""), "html: слишком широкое изображение всё равно отрисовано (у HTML нет понятия печатной полосы)");
            Check(html.Split("src=\"media/real.png\"").Length - 1 == 3,
                $"html: keyref и прямой href на одно и то же изображение резолвятся в один и тот же скопированный файл (3 вхождения): {html.Split("src=\"media/real.png\"").Length - 1}");
            Check(html.Contains("src=\"missing.png\""), "html: несуществующий файл — копирование не удалось, но <img> всё равно отрисован с исходным именем (не 'media/', в отличие от успешно скопированных)");
            Check(html.Contains("src=\"media/pic.webp\""), "html: неподдерживаемый для DOCX формат в HTML не особый случай — обычный скопированный <img>");
            Check(html.Contains("src=\"https://example.com/pic.png\""), "html: внешнее изображение отрисовано как обычный <img> (в отличие от DOCX, который его пропускает)");

            Check(html.Contains("<a href=\"https://example.com\">Сайт</a>"), "html: внешняя xref стала обычной ссылкой");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a href=\"#topic\">#topic</a>"),
                "html (single-file): неразрешённая xref внутри публикации деградирует до якоря по topicId, а не до буквального href (это забота DOCX — там именно буквальный href)");
            Check(html.Contains("Голый текст") && !html.Contains("label-key"), "html: xref по keyref без href показывает KeyText");
            Check(html.Contains("related-links") && html.Contains("Второй топик") && html.Contains("Внешний сайт"),
                "html: related-links собрал и внутреннюю, и внешнюю ссылку");

            // --------------------------------------------------------------- DOCX
            var docxPath = Path.Combine(root, "out.docx");
            var docxResult = new DocxPublisher(project).Publish(mapPath, new PublishOptions { Language = "ru" }, docxPath);

            using var doc = WordprocessingDocument.Open(docxPath, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Рисунок 1. Схема"), "docx: подпись рисунка пронумерована");
            Check(text.Contains("Описание рисунка."), "docx: desc рисунка отрисован");
            Check(text.Contains("Ключ") && text.Contains("Значение ключа"), "docx: dlentry (dt/dd) отрисован");
            Check(text.Contains("-x") && text.Contains("Включить X"), "docx: parml (pt/pd) отрисован");
            Check(text.Contains("H1") && text.Contains("A1"), "docx: generic simpletable отрисован");
            Check(text.Contains("Красный"), "docx: properties отрисован (заданные заголовки колонок использованы)");
            Check(text.Contains("Вариант A"), "docx: choicetable отрисован");

            var drawings = body.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToList();
            Check(drawings.Count == 4, $"docx: во внедрение попали 4 изображения (натуральный размер, явные размеры, слишком широкое, keyref): {drawings.Count}");

            if (drawings.Count >= 4)
            {
                var natural = drawings[0].Extent!;
                Check(natural.Cx!.Value == 200L * 9525 && natural.Cy!.Value == 100L * 9525,
                    $"docx: естественный размер картинки взят из PNG-заголовка (200x100 px @96dpi): {natural.Cx},{natural.Cy}");

                var explicitSize = drawings[1].Extent!;
                Check(explicitSize.Cx!.Value == 2L * 914400 && explicitSize.Cy!.Value == 914400,
                    $"docx: явные width/height (2in x 1in) переопределяют естественный размер: {explicitSize.Cx},{explicitSize.Cy}");

                var wide = drawings[2].Extent!;
                const long maxWidthEmu = 6L * 914400;
                Check(wide.Cx!.Value == maxWidthEmu && wide.Cx!.Value < 2000L * 9525,
                    $"docx: слишком широкое изображение (2000px) обрезано по MaxWidthEmu (6 дюймов): {wide.Cx}");
            }

            Check(docxResult.Warnings.Any(w => w.Contains("Изображение не найдено") && w.Contains("missing.png")),
                "docx: отсутствующий файл изображения дал предупреждение");
            Check(docxResult.Warnings.Any(w => w.Contains("не поддерживается") && w.Contains("pic.webp")),
                "docx: неподдерживаемый формат дал предупреждение");
            Check(text.Contains("pic.webp"), "docx: неподдерживаемый формат показан как текстовая ссылка на имя файла");
            Check(!text.Contains("example.com/pic.png"), "docx: внешнее изображение молча пропущено (не встроено, не текстом)");

            var hyperlinks = body.Descendants<Hyperlink>().ToList();
            Check(hyperlinks.Any(h => h.Id?.Value is not null), "docx: внешняя xref стала гиперссылкой по Relationship Id");
            Check(text.Contains("nowhere.dita#topic"), "docx: неразрешённая xref деградирует до буквального href как простого текста (без гиперссылки)");
            Check(text.Contains("Голый текст"), "docx: xref по keyref без href показывает KeyText как обычный текст");
            Check(text.Contains("Второй топик") && text.Contains("Внешний сайт"), "docx: related-links собрал и внутреннюю, и внешнюю ссылку");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    /// <summary>
    /// Остальные конструкции HtmlRenderer, которых нет ни в одном другом тесте: CALS-таблица,
    /// object/video/audio, foreign/svg-container, coderef, сноски, предметный указатель
    /// (indexterm/RenderIndexSection), abbreviated-form (все три исхода), RenderInline-варианты
    /// (overline/q/cite/menucascade/state/boolean/tm), spectitle-заголовок раздела, hazardstatement,
    /// вложенный топик (RenderNestedTopic) и глоссарная статья как отдельный тип топика.
    /// </summary>
    private static void HtmlRendererMiscTests()
    {
        Section("HtmlRenderer: таблица/медиа/foreign/сноски/указатель/abbreviated-form (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "code.txt"), "int main() { return 0; }");
            File.WriteAllBytes(Path.Combine(root, "video.mp4"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(root, "audio.mp3"), new byte[] { 4, 5, 6 });
            File.WriteAllBytes(Path.Combine(root, "flashthing.bin"), new byte[] { 7, 8 });

            File.WriteAllText(Path.Combine(root, "glossary.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<glossentry id="acr-glossentry">
  <glossterm>Central Processing Unit</glossterm>
  <glossAcronym>CPU</glossAcronym>
  <glossdef>Определение.</glossdef>
</glossentry>
""");

            File.WriteAllText(Path.Combine(root, "plain.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="plain">
  <title>Обычный топик, не глоссарий</title>
  <conbody><p>Текст.</p></conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "main.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="main">
  <title>Продвинутые конструкции</title>
  <conbody>
    <concept id="nested"><title>Вложенный топик</title><conbody><p>Внутри вложенного.</p></conbody></concept>
    <section spectitle="Пользовательский раздел"><p>Текст раздела без явного title.</p></section>
    <hazardstatement type="warning">
      <messagepanel><typeoftext>ОПАСНО</typeoftext><howtoavoid>Не делайте так.</howtoavoid></messagepanel>
    </hazardstatement>
    <table>
      <title>Заголовок таблицы</title>
      <tgroup cols="3">
        <colspec colname="c1" colnum="1" colwidth="2*"/>
        <colspec colname="c2" colnum="2" colwidth="1*"/>
        <colspec colname="c3" colnum="3" colwidth="1*"/>
        <thead><row><entry>H1</entry><entry>H2</entry><entry>H3</entry></row></thead>
        <tbody>
          <row><entry namest="c1" nameend="c2" align="center">Объединённая</entry><entry morerows="1" valign="top">R</entry></row>
          <row><entry>X</entry><entry>Y</entry></row>
        </tbody>
      </tgroup>
    </table>
    <object data="flashthing.bin" type="application/octet-stream"/>
    <object>Без data — дети рендерятся как есть</object>
    <video href="video.mp4"/>
    <audio href="audio.mp3" controls="false"/>
    <video><media-source href="video.mp4"/></video>
    <svg-container><svg width="10" height="10"><circle r="5"/></svg></svg-container>
    <codeblock><coderef href="code.txt"/></codeblock>
    <codeblock><coderef href="missing.txt"/></codeblock>
    <p>Сноска с меткой<fn callout="*">Особая сноска.</fn> и обычная<fn>Вторая сноска.</fn>.</p>
    <p>Термин <indexterm>CPU<indexterm>детали</indexterm></indexterm> встречается и здесь: <indexterm>CPU</indexterm>.</p>
    <p>По ключу глоссария: <abbreviated-form keyref="cpu-key"/>; по ключу без глоссария: <abbreviated-form keyref="plain-key"/>; по несуществующему ключу: <abbreviated-form keyref="no-such-key"/>; без keyref: <abbreviated-form/>.</p>
    <p><overline>надчёркнутый</overline> <q>цитата</q> <cite>Источник</cite> <menucascade><uicontrol>Файл</uicontrol><uicontrol>Открыть</uicontrol></menucascade> <state name="mode" value="on"/> <boolean state="yes"/> <tm tmtype="reg">Reg</tm> <tm tmtype="service">Serv</tm> <tm>Trade</tm></p>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест HtmlRenderer</title>
  <keydef keys="cpu-key" href="glossary.dita"/>
  <keydef keys="plain-key" href="plain.dita"><topicmeta><keywords><keyword>Обычная ссылка</keyword></keywords></topicmeta></keydef>
  <topicref href="main.dita"/>
  <topicref href="glossary.dita"/>
  <topicref href="plain.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            var htmlResult = new HtmlPublisher(project).Publish(
                Path.Combine(root, "guide.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "html-out"), SingleFile = true });
            var html = File.ReadAllText(htmlResult.EntryFile);

            // FigureNumber/TableNumber: публично объявленные счётчики, нигде в проекте больше не
            // используемые (HtmlPublisher их не трогает) — минимальная проверка самих геттеров/сеттеров.
            var standaloneRenderer = new HtmlRenderer(project) { FigureNumber = 5, TableNumber = 3 };
            Check(standaloneRenderer.FigureNumber == 5 && standaloneRenderer.TableNumber == 3,
                "FigureNumber/TableNumber — обычные читаемые/записываемые свойства");

            Check(html.Contains("Вложенный топик") && html.Contains("Внутри вложенного."), "вложенный топик (RenderNestedTopic) отрисован");
            Check(html.Contains("Пользовательский раздел"), "section без title использует spectitle как заголовок");

            Check(html.Contains("class=\"typeoftext\"") && html.Contains("ОПАСНО") && html.Contains("Не делайте так."),
                "hazardstatement/messagepanel отрисован по произвольным именам дочерних элементов");

            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<col style=\"width:2\\*\"[^>]*/>|<col />"), "CALS-таблица: colgroup сгенерирован");
            Check(html.Contains("<th>H1</th>") || html.Contains("<th>H1"), "CALS-таблица: заголовок thead/th отрисован");
            Check(html.Contains("colspan=\"2\""), "CALS-таблица: namest/nameend дали colspan");
            Check(html.Contains("rowspan=\"2\""), "CALS-таблица: morerows=1 дал rowspan=2 (morerows+1)");
            Check(html.Contains("text-align:center"), "CALS-таблица: align превращён в style");
            Check(html.Contains("vertical-align:top"), "CALS-таблица: valign превращён в style");
            Check(html.Contains("Заголовок 1. Заголовок таблицы") || html.Contains("Таблица 1. Заголовок таблицы"),
                $"CALS-таблица пронумерована подписью (реальный текст подписи см. Labels.Table)");

            Check(html.Contains("<object data=\"media/flashthing.bin\"") || html.Contains("<object data=\"flashthing.bin\""),
                "object с data встроен как <object>");
            Check(html.Contains("Без data — дети рендерятся как есть"), "object без data рендерит своих детей как есть");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<video src=\"[^\"]*video\\.mp4\" controls>"), "video с href и без controls=\"false\" получает атрибут controls");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<audio src=\"[^\"]*audio\\.mp3\"></audio>"), "audio с controls=\"false\" не получает атрибут controls");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<video src=\"[^\"]*video\\.mp4\" controls></video>\\s*<video src=\"[^\"]*video\\.mp4\" controls>") ||
                  html.Split("video.mp4").Length - 1 >= 2,
                "video без href, но с media-source, тоже находит источник");

            Check(html.Contains("<circle r=\"5\"") && html.Contains("<svg"), "svg-container передан как есть (RenderForeign)");

            Check(html.Contains("int main() { return 0; }"), "coderef на существующий файл вставляет его содержимое");
            Check(html.Contains("<!-- coderef не найден: missing.txt -->"), "coderef на несуществующий файл даёт HTML-комментарий, а не падает");

            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a class=\"fn-ref\" href=\"#fn1\"[^>]*>\\[\\*\\]</a>"), "сноска с callout использует его как маркер вместо номера");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<a class=\"fn-ref\" href=\"#fn2\"[^>]*>\\[2\\]</a>"), "вторая сноска без callout нумеруется автоматически");
            Check(html.Contains("class=\"footnotes\"") && html.Contains("Особая сноска.") && html.Contains("Вторая сноска."),
                "блок сносок в конце топика собрал обе сноски по тексту");

            Check(html.Contains("class=\"index-terms\""), "предметный указатель отрисован (RenderIndexSection)");
            Check(System.Text.RegularExpressions.Regex.IsMatch(html, "<li>CPU\\s*<a href=\"[^\"]*\">1</a>"),
                "верхний уровень указателя: термин CPU встретился дважды в одном топике, но Distinct() схлопнул ссылки в одну");
            Check(html.Contains("<li>детали"), "вложенный indexterm стал подпунктом указателя");

            Check(html.Contains("<abbr class=\"abbreviated-form\">CPU</abbr>"), "abbreviated-form: акроним из глоссария найден и использован");
            Check(html.Contains("<abbr class=\"abbreviated-form\">Обычная ссылка</abbr>"), "abbreviated-form: ключ резолвится, но цель не глоссарий — используется KeyText ключа");
            Check(html.Contains("<abbr class=\"abbreviated-form\">no-such-key</abbr>"), "abbreviated-form: ключ не резолвится вовсе — используется буквальный keyref");
            Check(!html.Contains("<abbr class=\"abbreviated-form\"></abbr>"), "abbreviated-form без keyref не создаёт пустой <abbr>");

            Check(html.Contains("<span style=\"text-decoration:overline\">надчёркнутый</span>"), "overline отрисован");
            Check(html.Contains("<q>цитата</q>"), "q отрисован как <q>");
            Check(html.Contains("<cite>Источник</cite>"), "cite отрисован");
            Check(html.Contains("class=\"menucascade\"") && html.Contains("Файл") && html.Contains("Открыть") && html.Contains("&rarr;"),
                "menucascade собрал цепочку uicontrol через разделитель");
            Check(html.Contains("class=\"state\">mode=on</span>"), "state отрисован как name=value");
            Check(html.Contains("class=\"boolean\">yes</span>"), "boolean отрисован по атрибуту state");
            Check(html.Contains("Reg&reg;") || html.Contains("Reg&amp;reg;"), "tm tmtype=\"reg\" даёт символ ®");
            Check(html.Contains("Serv&#8480;") || html.Contains("Serv&amp;#8480;"), "tm tmtype=\"service\" даёт символ ℠");
            Check(html.Contains("Trade&trade;") || html.Contains("Trade&amp;trade;"), "tm без tmtype по умолчанию — ™");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    // ------------------------------------------- списки и шаги (диспетчер рендера)

    /// <summary>
    /// Характеризационные проверки для веток switch, которые сознательно НЕ объединены
    /// общей категорией (ul/sl/choices, steps/steps-unordered, li/step) — HtmlRenderer и
    /// DocxRenderer расходятся тут по возможностям формата. Фиксируют текущее поведение,
    /// чтобы следующий шаг унификации диспетчера не сломал его молча.
    /// </summary>
    private static void ListAndStepsDispatchTests()
    {
        Section("Списки и шаги: защита перед унификацией диспетчера рендера");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "lists.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="lists">
  <title>Списки</title>
  <conbody>
    <ul>
      <li>Обычный пункт<ul><li>Вложенный пункт</li></ul></li>
    </ul>
    <ol>
      <li>Пункт по порядку</li>
    </ol>
    <sl>
      <sli>Простой пункт</sli>
    </sl>
    <choices>
      <choice>Вариант выбора</choice>
    </choices>
  </conbody>
</concept>
""");

            File.WriteAllText(Path.Combine(root, "steps.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<task id="steps-task">
  <title>Шаги</title>
  <taskbody>
    <steps>
      <step>
        <cmd>Первый шаг</cmd>
        <info><p>Пояснение к шагу</p></info>
        <substeps>
          <substep><cmd>Подшаг А</cmd></substep>
        </substeps>
        <stepresult><p>Результат первого шага</p></stepresult>
      </step>
    </steps>
    <steps-unordered>
      <step><cmd>Проверить диск</cmd></step>
    </steps-unordered>
  </taskbody>
</task>
""");

            File.WriteAllText(Path.Combine(root, "coverage.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Тест покрытия списков и шагов</title>
  <topicref href="lists.dita"/>
  <topicref href="steps.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();

            // --- HTML ---
            var htmlPublisher = new HtmlPublisher(project);
            var htmlResult = htmlPublisher.Publish(Path.Combine(root, "coverage.ditamap"), new PublishOptions
            {
                OutputDirectory = Path.Combine(root, "out-html"),
                SingleFile = true
            });
            var html = File.ReadAllText(htmlResult.EntryFile);

            Check(html.Contains("<li>Обычный пункт"), "html: <li> обычного ul отрисован");
            Check(html.Contains("<li>Вложенный пункт</li>"), "html: вложенный список внутри <li> дошёл до вывода");
            Check(html.Contains("<ul class=\"sl\""), "html: <sl> обёрнут в <ul class=\"sl\">");
            Check(html.Contains("<li>Простой пункт</li>"), "html: <sli> отрисован как <li>");
            Check(html.Contains("<ul class=\"choices\""), "html: <choices> обёрнут в <ul class=\"choices\">");
            Check(html.Contains("<li>Вариант выбора</li>"), "html: <choice> отрисован как <li>");
            Check(html.Contains("<li>Пункт по порядку</li>"), "html: <li> обычного ol отрисован");

            Check(Regex.Matches(html, "Порядок действий").Count == 2,
                "html: заголовок шагов сгенерирован и для <steps>, и для <steps-unordered>");
            Check(html.Contains("<ol class=\"steps\""), "html: <steps> обёрнут в <ol class=\"steps\">");
            Check(html.Contains("<ul class=\"steps\""), "html: <steps-unordered> обёрнут в <ul class=\"steps\">");
            Check(html.Contains("<div class=\"cmd\">Первый шаг</div>"), "html: <cmd> отрисован своим div");
            Check(html.Contains("<div class=\"info\">") && html.Contains("Пояснение к шагу"),
                "html: <info> отрисован своим div");
            Check(html.Contains("<div class=\"stepresult\">") && html.Contains("Результат первого шага"),
                "html: <stepresult> отрисован своим div");
            Check(html.Contains("Подшаг А"), "html: <substeps>/<substep> дошли до вывода");
            Check(html.Contains("Проверить диск"), "html: шаг внутри <steps-unordered> отрисован");

            // --- DOCX ---
            var docxOut = Path.Combine(root, "out.docx");
            new DocxPublisher(project).Publish(Path.Combine(root, "coverage.ditamap"), new PublishOptions(), docxOut);

            using var doc = WordprocessingDocument.Open(docxOut, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var text = body.InnerText;

            Check(text.Contains("Обычный пункт") && text.Contains("Вложенный пункт"), "docx: ul и вложенный ul отрисованы");
            Check(text.Contains("Простой пункт"), "docx: sl отрисован");
            Check(text.Contains("Вариант выбора"), "docx: choices отрисован");
            Check(text.Contains("Пункт по порядку"), "docx: ol отрисован");
            Check(text.Contains("Первый шаг") && text.Contains("Пояснение к шагу") && text.Contains("Результат первого шага"),
                "docx: cmd/info/stepresult шага отрисованы");
            Check(text.Contains("Подшаг А"), "docx: substeps/substep отрисованы");
            Check(text.Contains("Проверить диск"), "docx: шаг внутри steps-unordered отрисован");

            var bulletGroup = new[] { "Обычный пункт", "Простой пункт", "Вариант выбора", "Проверить диск" }
                .Select(t => FindNumberingAbstractId(doc, t)).ToList();
            var decimalGroup = new[] { "Пункт по порядку", "Первый шаг", "Подшаг А" }
                .Select(t => FindNumberingAbstractId(doc, t)).ToList();

            Check(bulletGroup.All(id => id is not null) && bulletGroup.Distinct().Count() == 1,
                $"docx: ul/sl/choices/steps-unordered используют одно и то же маркированное оформление списка: [{string.Join(",", bulletGroup)}]");
            Check(decimalGroup.All(id => id is not null) && decimalGroup.Distinct().Count() == 1,
                $"docx: ol/steps/substeps используют одно и то же нумерованное оформление списка: [{string.Join(",", decimalGroup)}]");
            Check(bulletGroup[0] != decimalGroup[0],
                "docx: маркированный и нумерованный список используют разное оформление (ordered-флаг не перепутан)");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static int? FindNumberingAbstractId(WordprocessingDocument doc, string paragraphText)
    {
        var body = doc.MainDocumentPart!.Document.Body!;
        var paragraph = body.Descendants<Paragraph>().FirstOrDefault(p => p.InnerText.Contains(paragraphText));
        var numId = paragraph?.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
        if (numId is null)
        {
            return null;
        }

        var numbering = doc.MainDocumentPart.NumberingDefinitionsPart!.Numbering!;
        return numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId)?.AbstractNumId?.Val?.Value;
    }
}
