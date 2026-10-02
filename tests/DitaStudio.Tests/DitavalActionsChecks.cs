using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// .ditaval: exclude без val + include, passthrough; запись обратно ничего не теряет.
internal static partial class CoreChecks
{
    internal static void DitavalActionsTests()
    {
        Section(".ditaval: include и passthrough");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "rules.ditaval");
            File.WriteAllText(file, """
                <?xml version="1.0" encoding="UTF-8"?>
                <val>
                  <prop att="product" action="exclude"/>
                  <prop att="product" val="alpha" action="include"/>
                  <prop att="audience" val="expert" action="exclude"/>
                  <prop att="platform" val="linux" action="passthrough"/>
                  <prop att="otherprops" action="passthrough"/>
                  <prop att="rev" val="2" action="flag" color="red"/>
                </val>
                """);

            var rules = DitavalReader.Read(file);
            Check(rules.ExcludeUnlisted!.SetEquals(new[] { "product" }), "exclude без val читается как «исключить всё»");
            Check(rules.Include!["product"].SetEquals(new[] { "alpha" }), "include с val читается");
            Check(rules.Exclude["audience"].SetEquals(new[] { "expert" }) && !rules.Exclude.ContainsKey("product"),
                "exclude с val остаётся в обычных исключениях, без val — не попадает в них");
            Check(rules.Passthrough!.Count == 2 && rules.Passthrough[0] == new DitavalPassthroughRule("platform", "linux") &&
                  rules.Passthrough[1] == new DitavalPassthroughRule("otherprops", null), "passthrough читается (с val и без)");
            Check(rules.Flags.Count == 1, "правило подсветки не потерялось");

            // --- запись возвращает те же правила
            var copy = Path.Combine(root, "copy.ditaval");
            DitavalWriter.Write(copy, rules);
            var again = DitavalReader.Read(copy);
            Check(again.ExcludeUnlisted!.SetEquals(rules.ExcludeUnlisted) && again.Include!["product"].SetEquals(new[] { "alpha" }) &&
                  again.Passthrough!.Count == 2 && again.Flags.Count == 1 && again.Exclude["audience"].Contains("expert"),
                "запись и повторное чтение дают те же правила");

            // --- правка исключений в диалоге не стирает include и passthrough
            var edited = rules with { Exclude = new Dictionary<string, HashSet<string>> { ["audience"] = new() { "novice" } } };
            DitavalWriter.Write(copy, edited);
            var afterEdit = DitavalReader.Read(copy);
            Check(afterEdit.Exclude["audience"].SetEquals(new[] { "novice" }) && afterEdit.Include!["product"].Contains("alpha") &&
                  afterEdit.ExcludeUnlisted!.Contains("product") && afterEdit.Passthrough!.Count == 2,
                "правка исключений сохраняет include, exclude-всё и passthrough");

            // --- файл без include и passthrough — прежнее поведение
            var plain = Path.Combine(root, "plain.ditaval");
            File.WriteAllText(plain, "<val><prop att=\"audience\" val=\"expert\" action=\"exclude\"/></val>");
            var plainRules = DitavalReader.Read(plain);
            Check(plainRules.Exclude["audience"].Contains("expert") && plainRules.ExcludeUnlisted!.Count == 0 && plainRules.Passthrough!.Count == 0,
                "обычный файл читается как раньше");

            // --- фильтр: исключить всё, вернуть alpha
            var options = new PublishOptions { Language = "ru" };
            DitaProject.MergeExcludeConditions(options.ExcludeConditions, rules.Exclude);
            foreach (var a in rules.ExcludeUnlisted)
            {
                options.ExcludeUnlistedConditions.Add(a);
            }

            foreach (var (a, v) in rules.Include)
            {
                options.IncludeConditions[a] = new HashSet<string>(v);
            }

            DitaNode El(string attr, string? value)
            {
                var node = DitaNode.Element("p");
                if (value is not null)
                {
                    node.SetAttribute(attr, value);
                }

                return node;
            }

            Check(PublishFilter.IsIncluded(El("product", "alpha"), options), "product=alpha возвращён через include");
            Check(!PublishFilter.IsIncluded(El("product", "beta"), options), "product=beta исключён (исключены все, кроме alpha)");
            Check(PublishFilter.IsIncluded(El("product", "alpha beta"), options), "несколько значений: достаточно одного включённого");
            Check(!PublishFilter.IsIncluded(El("product", "beta gamma"), options), "все значения исключены — элемент исключён");
            Check(PublishFilter.IsIncluded(El("product", null), options), "элемент без атрибута входит всегда");
            Check(!PublishFilter.IsIncluded(El("audience", "expert"), options), "обычное исключение по val работает");
            Check(PublishFilter.IsIncluded(El("audience", "novice"), options), "другие значения audience остаются");

            // --- passthrough: значение уходит в HTML как data-атрибут
            const string topic = "<?xml version=\"1.0\"?><topic id=\"t\"><title>T</title><body><p platform=\"linux mac\">Абзац</p><p platform=\"win\">Другой</p></body></topic>";
            File.WriteAllText(Path.Combine(root, "t.dita"), topic);
            File.WriteAllText(Path.Combine(root, "m.ditamap"), "<?xml version=\"1.0\"?><map><title>M</title><topicref href=\"t.dita\"/></map>");
            var project = new DitaProject(root);
            project.Scan();
            var publishOptions = new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true, Language = "ru" };
            publishOptions.PassthroughConditions.AddRange(rules.Passthrough);
            var result = new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"), publishOptions);
            var html = File.ReadAllText(result.EntryFile);
            Check(html.Contains("data-platform=\"linux mac\""), "passthrough с val: значение попало в HTML");
            Check(!html.Contains("data-platform=\"win\""), "passthrough с val: другое значение не выводится");
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
