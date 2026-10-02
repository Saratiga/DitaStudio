using System.Xml.Linq;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Язык подписей публикации и язык XLIFF берутся из xml:lang документа.
internal static partial class CoreChecks
{
    internal static void PublicationLanguageTests()
    {
        Section("Язык публикации и XLIFF по xml:lang");

        // --- выбор подписей
        Check(Labels.For("ru-RU") == Labels.Russian && Labels.For("RU") == Labels.Russian, "ru → русские подписи");
        Check(Labels.For("en-US") == Labels.English && Labels.For("en") == Labels.English, "en → английские подписи");
        Check(Labels.For("de-DE") == Labels.English, "язык, которого нет среди подписей, — английские");
        Check(Labels.For(null) == Labels.For(Loc.Instance.Language), "язык не задан — по языку интерфейса");
        Check(DocumentLanguage.Primary("pt_BR") == "pt" && DocumentLanguage.Primary(" ") is null, "основной код языка");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string topic = "<?xml version=\"1.0\"?><topic id=\"t\"{0}><title>T</title><body><p>Text</p><note>N</note></body></topic>";
            File.WriteAllText(Path.Combine(root, "t.dita"), string.Format(topic, string.Empty));
            string Map(string? lang) => "<?xml version=\"1.0\"?><map" + (lang is null ? string.Empty : $" xml:lang=\"{lang}\"") +
                "><title>M</title><topicref href=\"t.dita\"/></map>";

            var project = new DitaProject(root);

            string Publish(string? lang, string? forced = null)
            {
                File.WriteAllText(Path.Combine(root, "m.ditamap"), Map(lang));
                project.Scan();
                var options = new PublishOptions { OutputDirectory = Path.Combine(root, "out-" + Guid.NewGuid().ToString("N")), SingleFile = true, Language = forced };
                return File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"), options).EntryFile);
            }

            // тесты идут с русским интерфейсом: карта без xml:lang — русские подписи
            Check(Publish(null).Contains("Примечание"), "карта без xml:lang на русском интерфейсе — «Примечание»");
            Check(Publish("en-US").Contains("Note") && !Publish("en-US").Contains("Примечание"), "xml:lang=en-US — английские подписи при русском интерфейсе");
            Check(Publish("ru-RU").Contains("Примечание"), "xml:lang=ru-RU — русские подписи");
            Check(Publish("de").Contains("Note"), "xml:lang=de — подписи по умолчанию английские");
            Check(Publish("en").Contains("Примечание") == false && Publish("en", forced: "ru").Contains("Примечание"),
                "явно заданный язык публикации главнее xml:lang");

            // --- XLIFF: языки и предупреждение о другом файле
            var doc = DitaDocument.Parse(string.Format(topic, " xml:lang=\"en-US\""));
            var xliff = DitaStudio.Core.Localization.XliffConverter.Export(doc, "en-US", "de");
            var file = xliff.Root!.Element("file")!;
            Check(file.Attribute("source-language")!.Value == "en-US" && file.Attribute("target-language")!.Value == "de",
                "экспорт пишет заданные исходный и целевой языки");

            var sameWarnings = new List<string>();
            DitaStudio.Core.Localization.XliffConverter.Import(doc, xliff, sameWarnings);
            Check(!sameWarnings.Any(w => w.Contains("en-US") || w.Contains("«en»")), "импорт в документ на том же языке — без предупреждения о языке");

            var russianDoc = DitaDocument.Parse(string.Format(topic, " xml:lang=\"ru-RU\""));
            var mismatchWarnings = new List<string>();
            DitaStudio.Core.Localization.XliffConverter.Import(russianDoc, xliff, mismatchWarnings);
            Check(mismatchWarnings.Count(w => w.Contains("«en»") && w.Contains("«ru»")) == 1, "импорт в документ на другом языке предупреждает: " + string.Join(" | ", mismatchWarnings));

            var noLangDoc = DitaDocument.Parse(string.Format(topic, string.Empty));
            var noLangWarnings = new List<string>();
            DitaStudio.Core.Localization.XliffConverter.Import(noLangDoc, xliff, noLangWarnings);
            Check(!noLangWarnings.Any(w => w.Contains("«en»")), "у документа нет xml:lang — предупреждения о языке нет");
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
