using System.Text.Json;
using System.Xml.Linq;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Project;

namespace DitaStudio.Tests;

// Пакетный XLIFF: вся карта — по файлу на топик и manifest.json; импорт возвращает перевод в каждый топик.
internal static partial class CoreChecks
{
    internal static void XliffBatchTests()
    {
        Section("Пакетный XLIFF по карте");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "tasks"));
        try
        {
            File.WriteAllText(Path.Combine(root, "intro.dita"),
                "<?xml version=\"1.0\"?><topic id=\"intro\" xml:lang=\"ru-RU\"><title>Введение</title><body><p>Первый <b>важный</b> абзац.</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "tasks", "install.dita"),
                "<?xml version=\"1.0\"?><topic id=\"install\"><title>Установка</title><body><p>Запустите программу.</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "sub.ditamap"),
                "<?xml version=\"1.0\"?><map><title>Часть</title><topicref href=\"tasks/install.dita\"/></map>");
            var mapPath = Path.Combine(root, "guide.ditamap");
            File.WriteAllText(mapPath,
                "<?xml version=\"1.0\"?><map xml:lang=\"ru-RU\"><title>Руководство</title><topicref href=\"intro.dita\"/>" +
                "<mapref href=\"sub.ditamap\"/><topicref href=\"tasks/install.dita\"/><topicref href=\"missing.dita\"/></map>");

            var project = new DitaProject(root);
            project.Scan();
            var folder = Path.Combine(root, "xliff");

            // --- экспорт: по файлу на топик (повторы и битые ссылки не плодят файлов), манифест
            var export = XliffBatch.Export(project, mapPath, folder, "ru", "en");
            var files = Directory.GetFiles(folder, "*.xliff").Select(Path.GetFileName).OrderBy(x => x).ToList();
            Check(export.FileCount == 2 && files.Count == 2, $"два топика карты — два файла: {string.Join(", ", files)}");
            Check(files.Contains("intro.dita.en.xliff") && files.Contains("tasks__install.dita.en.xliff"), "имена файлов из путей топиков");
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, XliffBatch.ManifestName))))
            {
                var entries = manifest.RootElement.GetProperty("Files").EnumerateArray().Select(e => e.GetProperty("Path").GetString()).ToList();
                Check(entries.SequenceEqual(new[] { "intro.dita", "tasks/install.dita" }), "в манифесте пути топиков: " + string.Join(", ", entries));
                Check(manifest.RootElement.GetProperty("Target").GetString() == "en", "в манифесте целевой язык");
            }

            var introXliff = XDocument.Load(Path.Combine(folder, "intro.dita.en.xliff"));
            Check(introXliff.Root!.Element("file")!.Attribute("source-language")!.Value == "ru-RU", "исходный язык — xml:lang топика");
            var installXliff = XDocument.Load(Path.Combine(folder, "tasks__install.dita.en.xliff"));
            Check(installXliff.Root!.Element("file")!.Attribute("source-language")!.Value == "ru", "у топика без xml:lang — язык, заданный при выгрузке");

            // --- импорт без изменений перевода: документы не меняются
            var untouched = XliffBatch.Import(project, folder);
            Check(untouched.ChangedDocuments.Count == 0 && untouched.SegmentCount > 0, "перевод, совпадающий с исходным, документы не меняет");

            // --- перевод заголовков и абзаца с разметкой
            void Translate(XDocument xliff, string from, string to)
            {
                foreach (var unit in xliff.Root!.Descendants("trans-unit"))
                {
                    var target = unit.Element("target")!;
                    var text = target.Value;
                    if (text.Contains(from))
                    {
                        // подменяем только текстовые узлы, теги bpt/ept остаются
                        foreach (var node in target.DescendantNodes().OfType<XText>().ToList())
                        {
                            node.Value = node.Value.Replace(from, to);
                        }
                    }
                }
            }

            Translate(introXliff, "Введение", "Introduction");
            Translate(introXliff, "Первый ", "The first ");
            Translate(introXliff, " абзац.", " paragraph.");
            introXliff.Save(Path.Combine(folder, "intro.dita.en.xliff"));
            Translate(installXliff, "Установка", "Installation");
            installXliff.Save(Path.Combine(folder, "tasks__install.dita.en.xliff"));

            var result = XliffBatch.Import(project, folder);
            var intro = project.GetDocument(Path.Combine(root, "intro.dita"));
            var install = project.GetDocument(Path.Combine(root, "tasks", "install.dita"));
            Check(result.ChangedDocuments.Count == 2 && result.ChangedDocuments.All(d => d.IsDirty), "изменены оба топика и помечены несохранёнными");
            Check(intro.Root.FirstElement("title")!.InnerText == "Introduction", "заголовок первого топика переведён");
            Check(install.Root.FirstElement("title")!.InnerText == "Installation", "заголовок второго топика переведён");
            var paragraph = intro.Root.FindDescendant("p")!;
            Check(paragraph.InnerText == "The first важный paragraph." && paragraph.FirstElement("b")?.InnerText == "важный",
                "разметка внутри абзаца (b) сохранилась: " + paragraph.InnerText);
            Check(!File.ReadAllText(Path.Combine(root, "intro.dita")).Contains("Introduction"), "файлы на диске импорт не трогает — сохранение за вызывающим");

            // --- повторный импорт тех же файлов: те же сегменты, документ уже переведён — идемпотентно
            var again = XliffBatch.Import(project, folder);
            Check(again.ChangedDocuments.Count == 0 && intro.Root.FirstElement("title")!.InnerText == "Introduction", "повторный импорт ничего не меняет");

            // --- проблемы: нет XLIFF-файла, топика нет в проекте, нет манифеста
            File.Delete(Path.Combine(folder, "intro.dita.en.xliff"));
            var missingFile = XliffBatch.Import(project, folder);
            Check(missingFile.Warnings.Any(w => w.Contains("intro.dita.en.xliff")), "нет XLIFF-файла из манифеста — предупреждение, остальные применяются");

            File.Delete(Path.Combine(root, "tasks", "install.dita"));
            project.Scan();
            var missingTopic = XliffBatch.Import(project, folder);
            Check(missingTopic.Warnings.Any(w => w.Contains("tasks/install.dita")), "топика нет в проекте — предупреждение");

            var empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(empty);
            var noManifest = XliffBatch.Import(project, empty);
            Check(noManifest.ChangedDocuments.Count == 0 && noManifest.Warnings.Count == 1, "в папке нет манифеста — одно понятное предупреждение");
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
