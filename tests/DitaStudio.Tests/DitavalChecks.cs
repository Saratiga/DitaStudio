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

// Условия сборки .ditaval: exclude/flag (включая property-based).
internal static partial class CoreChecks
{
    internal static void DitavalFlagTests()
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

    internal static void DitavalPropertyTests()
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
}
