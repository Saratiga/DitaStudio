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

// Папка проекта, карты, публикация HTML по проекту.
internal static partial class CoreChecks
{
    internal static void ProjectTests()
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

            Check(project.Search(string.Empty).Count == 0, "пустой запрос — пустой результат, не 'совпадает со всем'");
            Check(project.Search("   ").Count == 0, "запрос из одних пробелов — тоже пустой результат");

            var caseSensitiveMiss = project.Search("ПРОДУКТЕ", caseSensitive: true);
            Check(caseSensitiveMiss.Count == 0, "поиск с учётом регистра не находит несовпадающий по регистру текст");
            var caseInsensitiveHit = project.Search("ПРОДУКТЕ", caseSensitive: false);
            Check(caseInsensitiveHit.Count == 1, "тот же запрос без учёта регистра находит совпадение");

            var elementNameHits = project.Search("shortdesc", elementNames: true);
            Check(elementNameHits.Count >= 1 && elementNameHits.All(h => h.Node.Name == "shortdesc"),
                $"поиск по именам элементов находит узлы <shortdesc> вместо текста внутри них: {elementNameHits.Count}");
            var elementNameRegexHits = project.Search(@"^short\w+$", elementNames: true, regex: true);
            Check(elementNameRegexHits.Count == elementNameHits.Count, "поиск по именам элементов тоже умеет работать через regex");

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

    internal static void HtmlPublisherMiscTests()
    {
        Section("HtmlPublisher: многофайловая сборка, RenderPreview для карты (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"), "<concept id=\"intro\"><title>Введение</title><conbody><p>Текст.</p></conbody></concept>");
            // Разные настоящие файлы на диске ("doc.name.dita" и "doc name.dita" — точка и пробел),
            // но SafeFileName сводит и точку, и пробел к '-', так что оба претендуют на "doc-name.html" —
            // второй должен получить суффикс "doc-name-1.html". Одноимённые файлы с точностью до
            // регистра ("Doc.dita"/"doc.dita") тут не годятся — NTFS сам не даст их создать раздельно.
            File.WriteAllText(Path.Combine(root, "doc.name.dita"), "<concept id=\"docA\"><title>Документ A</title><conbody><p>x</p></conbody></concept>");
            File.WriteAllText(Path.Combine(root, "doc name.dita"), "<concept id=\"docB\"><title>Документ Б</title><conbody><p>x<xref href=\"intro.dita#intro\"/></p></conbody></concept>");

            File.WriteAllText(Path.Combine(root, "guide.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>Многостраничное руководство</title>
  <topicref href="intro.dita"/>
  <topicref href="doc.name.dita"/>
  <topicref href="doc name.dita"/>
</map>
""");

            var project = new DitaProject(root);
            project.Scan();
            var publisher = new HtmlPublisher(project);

            var outDir = Path.Combine(root, "site");
            var result = publisher.Publish(Path.Combine(root, "guide.ditamap"), new PublishOptions { OutputDirectory = outDir });

            Check(File.Exists(Path.Combine(outDir, "index.html")), "index.html создан для многостраничной сборки");
            Check(File.Exists(Path.Combine(outDir, "intro.html")), "первый топик получил файл по своему имени");
            Check(File.Exists(Path.Combine(outDir, "doc-name.html")) && File.Exists(Path.Combine(outDir, "doc-name-1.html")),
                "коллизия очищенных имён файлов (точка/пробел оба стали '-') разрешена суффиксом -1");
            Check(result.Files.Count == 5, $"в списке файлов style.css + index.html + 3 страницы топиков: {result.Files.Count}");

            var indexHtml = File.ReadAllText(Path.Combine(outDir, "index.html"));
            Check(indexHtml.Contains("intro.html") && indexHtml.Contains("Введение"),
                "index.html ссылается на первый топик карты по имени и заголовку");

            var introHtml = File.ReadAllText(Path.Combine(outDir, "intro.html"));
            Check(introHtml.Contains("<a href=\"doc-name.html\">Документ A</a>"),
                "у первой страницы есть Pager-ссылка \"вперёд\" на следующий топик карты");

            var docBHtml = File.ReadAllText(Path.Combine(outDir, "doc-name-1.html"));
            Check(docBHtml.Contains("href=\"intro.html#intro\""),
                "перекрёстная ссылка на другой файл в многостраничной сборке стала 'файл.html#id'");

            // Пустая карта: index.html получает заглушку "В карте нет топиков.".
            File.WriteAllText(Path.Combine(root, "empty.ditamap"), "<map><title>Пустая</title></map>");
            var emptyResult = publisher.Publish(Path.Combine(root, "empty.ditamap"), new PublishOptions { OutputDirectory = Path.Combine(root, "empty-out") });
            Check(File.ReadAllText(emptyResult.EntryFile).Contains("В карте нет топиков."), "пустая карта — заглушка вместо ссылки на первый топик");

            // RenderPreview карты: сохранённая (с FilePath) — список топиков с пометками "битый"/"только ресурс".
            File.WriteAllText(Path.Combine(root, "broken.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>С проблемами</title>
  <topicref href="intro.dita"/>
  <topicref href="nowhere.dita"/>
  <keydef keys="only-resource" href="intro.dita"/>
</map>
""");
            var brokenMapDoc = project.GetDocument(Path.Combine(root, "broken.ditamap"));
            var mapPreview = publisher.RenderPreview(brokenMapDoc);
            Check(mapPreview.Contains("файл не найден"), "RenderPreview карты помечает битую ссылку");
            Check(mapPreview.Contains("только ресурс"), "RenderPreview карты помечает keydef как resource-only");
            Check(mapPreview.Contains("Введение"), "RenderPreview карты перечисляет обычные топики по заголовку");

            // Та же карта — через настоящую многостраничную сборку: битая ссылка и keydef попадают
            // в оглавление как "голова без ссылки" (<li class="head">), а не как обычная <a>.
            var brokenSiteResult = publisher.Publish(Path.Combine(root, "broken.ditamap"), new PublishOptions { OutputDirectory = Path.Combine(root, "broken-out") });
            var brokenIndexHtml = File.ReadAllText(brokenSiteResult.EntryFile);
            Check(System.Text.RegularExpressions.Regex.IsMatch(brokenIndexHtml, "<li class=\"head\"><span>[^<]*</span>"),
                "оглавление многостраничной сборки: узел без валидной ссылки (битый href) отрисован как <li class=\"head\">, без <a>");

            // Карта без FilePath (ещё не сохранена на диск) — отдельная заглушка.
            var unsavedMap = DitaDocument.Parse("<map><title>Новая карта</title></map>");
            Check(publisher.RenderPreview(unsavedMap).Contains("Карта не сохранена."), "RenderPreview несохранённой карты — заглушка, не падает");
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
