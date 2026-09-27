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

// Ключи и области ключей, мультипроектный workspace, таблицы соответствий, дерево карты.
internal static partial class CoreChecks
{
    internal static void KeyScopeTests()
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
            CheckValidDocx(docxOut, "Области ключей (keyscope)");
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

    internal static void KeyDefinitionMiscTests()
    {
        Section("KeyDefinition: KeyText через navtitle, KeyContent, ToString() (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "map.ditamap"), """
<?xml version="1.0" encoding="UTF-8"?>
<map>
  <title>T</title>
  <keydef keys="via-navtitle" href="a.dita"><topicmeta><navtitle>Заголовок навигации</navtitle></topicmeta></keydef>
  <keydef keys="via-keyword" href="a.dita"><topicmeta><keywords><keyword>Слово-ключ</keyword></keywords></topicmeta></keydef>
  <keydef keys="bare" href="a.dita"/>
</map>
""");
            File.WriteAllText(Path.Combine(root, "a.dita"), "<concept id=\"a\"><title>A</title><conbody><p>x</p></conbody></concept>");

            var project = new DitaProject(root);
            project.Scan();

            var viaNavtitle = project.ResolveKey("via-navtitle", null)!;
            Check(viaNavtitle.KeyText == "Заголовок навигации", $"KeyText берёт navtitle, когда keywords/keyword нет: '{viaNavtitle.KeyText}'");
            Check(viaNavtitle.KeyContent is null, "KeyContent — null, когда нет keywords/keyword (navtitle не считается)");

            var viaKeyword = project.ResolveKey("via-keyword", null)!;
            Check(viaKeyword.KeyContent?.InnerText == "Слово-ключ", "KeyContent возвращает сам узел <keyword>, когда он есть");

            var bare = project.ResolveKey("bare", null)!;
            Check(bare.KeyText is null, "KeyText — null, когда нет ни keyword, ни navtitle");
            Check(bare.ToString() == "bare -> a.dita", $"ToString() формата 'ключ -> href': '{bare}'");
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

    internal static void MultiProjectWorkspaceTests()
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

            Check(!File.Exists(Path.Combine(mainRoot, ".ditastudio-references")),
                "отключение последнего источника удаляет файл настройки, а не оставляет пустой список");

            project.RemoveReferencedProject(sharedRoot);
            Check(project.ReferencedProjectPaths.Count == 0, "повторное отключение уже не подключённого источника — no-op, не падает");

            project.AddReferencedProject(sharedRoot);
            project.AddReferencedProject(sharedRoot);
            Check(project.ReferencedProjectPaths.Count == 1, "повторное подключение того же источника не создаёт дубликат");
            project.RemoveReferencedProject(sharedRoot);

            var missingRoot = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-missing");
            project.AddReferencedProject(missingRoot);
            project.Scan();
            Check(project.ReferencedProjectPaths.Count == 1 && project.ResolveKey("product-name") is null,
                "источник, указывающий на несуществующую папку, не роняет Scan() и просто не даёт ключей");
            project.RemoveReferencedProject(missingRoot);

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

    internal static void ValidationAndAnchorFixTests()
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

    internal static void RelTableTests()
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
            CheckValidDocx(docxOut, "Таблицы соответствий (reltable)");
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

    internal static void MapTreeMiscTests()
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
}
