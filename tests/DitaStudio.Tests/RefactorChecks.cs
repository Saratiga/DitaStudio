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

// conref/keyref, рефакторинг id и файлов, вынесение в conref.
internal static partial class CoreChecks
{
    internal static void RefResolverTests()
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

    internal static void RefactorTests()
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

    internal static void ExtractToConrefTests()
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
}
