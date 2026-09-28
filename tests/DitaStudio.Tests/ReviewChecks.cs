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

// Пометка изменений (rev) и track changes.
internal static partial class CoreChecks
{
    internal static void RevChangeTests()
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
            CheckValidDocx(docxOut, "Пометка изменений (rev)");
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

    internal static void TrackChangesTests()
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
            CheckValidDocx(docxOut, "Пометка изменений (rev)");
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
}
