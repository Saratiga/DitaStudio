using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
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
        ContentModelTests();
        RoundTripTests();
        ValidationTests();
        TemplateTests();
        EditingTests();
        ProjectTests();
        DocxTests();

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

            project.SetCustomCssPath(null);
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
</val>
""");
            var ditavalRules = DitavalReader.ReadExcludeRules(ditavalPath);
            Check(ditavalRules.TryGetValue("platform", out var linuxRule) && linuxRule.Contains("linux"),
                "правило exclude из .ditaval прочитано");
            Check(ditavalRules.Count == 1, "правило include из .ditaval пропущено как неподдерживаемое");
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
}
