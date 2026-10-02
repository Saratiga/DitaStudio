using DocumentFormat.OpenXml.Packaging;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;

namespace DitaStudio.Tests;

// Условная сборка действует и на строки карты: исключённый topicref уходит из публикации вместе с веткой.
internal static partial class CoreChecks
{
    internal static void ConditionalMapTests()
    {
        Section("Условия сборки для строк карты");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string topic = "<?xml version=\"1.0\"?><topic id=\"{0}\"><title>Топик {0}</title><body><p>Текст {0}</p></body></topic>";
            foreach (var id in new[] { "alpha", "common", "expert", "child" })
            {
                File.WriteAllText(Path.Combine(root, id + ".dita"), string.Format(topic, id));
            }

            var mapPath = Path.Combine(root, "guide.ditamap");
            File.WriteAllText(mapPath, """
                <?xml version="1.0"?>
                <map>
                  <title>Руководство</title>
                  <topicref href="common.dita"/>
                  <topicref href="alpha.dita" product="alpha"/>
                  <topichead navtitle="Для экспертов" audience="expert">
                    <topicref href="expert.dita"/>
                    <topicref href="child.dita"/>
                  </topichead>
                  <reltable>
                    <relrow>
                      <relcell><topicref href="common.dita"/></relcell>
                      <relcell><topicref href="alpha.dita"/></relcell>
                    </relrow>
                  </reltable>
                </map>
                """);

            var project = new DitaProject(root);
            project.Scan();

            // --- без условий: всё на месте
            var full = MapTree.Build(project, mapPath);
            Check(full.PublicationOrder.Count() == 4, $"без условий в публикации 4 топика: {full.PublicationOrder.Count()}");

            // --- с условиями: исключённые строки и ветки уходят, остаётся один топик
            var options = new PublishOptions { OutputDirectory = Path.Combine(root, "out"), Language = "ru" };
            options.ExcludeConditions["product"] = new HashSet<string> { "alpha" };
            options.ExcludeConditions["audience"] = new HashSet<string> { "expert" };
            var filtered = MapTree.Build(project, mapPath, node => PublishFilter.IsIncluded(node, options));
            var names = filtered.PublicationOrder.Select(i => Path.GetFileNameWithoutExtension(i.TargetPath!)).ToList();
            Check(names.SequenceEqual(new[] { "common" }), "после исключения остался только common: " + string.Join(", ", names));
            Check(!filtered.Items.Any(i => i.ElementName == "topichead"), "исключённый topichead не в дереве");
            Check(!filtered.RelatedLinks.ContainsKey(Path.GetFullPath(Path.Combine(root, "common.dita"))) ||
                  filtered.RelatedLinks[Path.GetFullPath(Path.Combine(root, "common.dita"))].Count == 0,
                "связь reltable с исключённым топиком снята");

            // --- исключено только одно значение: вторая ветка остаётся
            var onlyProduct = new PublishOptions { Language = "ru" };
            onlyProduct.ExcludeConditions["product"] = new HashSet<string> { "alpha" };
            var treeProduct = MapTree.Build(project, mapPath, node => PublishFilter.IsIncluded(node, onlyProduct));
            Check(treeProduct.PublicationOrder.Count() == 3, "исключён product=alpha — остаются 3 топика");

            // --- публикация: сайт, единый файл и DOCX
            var site = new HtmlPublisher(project).Publish(mapPath, options);
            var files = Directory.GetFiles(options.OutputDirectory, "*.html").Select(Path.GetFileName).ToList();
            Check(files.Contains("common.html") && !files.Contains("alpha.html") && !files.Contains("expert.html") && !files.Contains("child.html"),
                "в сайт вошёл только common: " + string.Join(", ", files));
            var index = File.ReadAllText(site.EntryFile);
            Check(index.Contains("Топик common") && !index.Contains("Топик alpha") && !index.Contains("Для экспертов"), "оглавление без исключённых");

            var singleOptions = new PublishOptions { OutputDirectory = Path.Combine(root, "single"), SingleFile = true, Language = "ru" };
            singleOptions.ExcludeConditions["product"] = new HashSet<string> { "alpha" };
            var single = new HtmlPublisher(project).Publish(mapPath, singleOptions);
            var singleHtml = File.ReadAllText(single.EntryFile);
            Check(!singleHtml.Contains("Текст alpha") && singleHtml.Contains("Текст common") && singleHtml.Contains("Текст expert"),
                "единый HTML: исключён только alpha");

            var docxPath = Path.Combine(root, "guide.docx");
            new DocxPublisher(project).Publish(mapPath, options, docxPath);
            string docxText;
            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                docxText = doc.MainDocumentPart!.Document.Body!.InnerText;
            }

            Check(docxText.Contains("Текст common") && !docxText.Contains("Текст alpha") && !docxText.Contains("Текст expert"),
                "DOCX: исключённые строки карты не попали в документ");

            // --- без условий публикация прежняя
            var allOptions = new PublishOptions { OutputDirectory = Path.Combine(root, "all"), Language = "ru" };
            new HtmlPublisher(project).Publish(mapPath, allOptions);
            Check(Directory.GetFiles(allOptions.OutputDirectory, "*.html").Length >= 5, "без условий — все страницы на месте");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // временная папка удалится системой
            }
        }
    }
}
