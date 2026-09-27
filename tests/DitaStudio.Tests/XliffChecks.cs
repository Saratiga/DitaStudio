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

// Экспорт/импорт XLIFF (включая property-based).
internal static partial class CoreChecks
{
    internal static void XliffTests()
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

    internal static void XliffConverterPropertyTests()
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
}
