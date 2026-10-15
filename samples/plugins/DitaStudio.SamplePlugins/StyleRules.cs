using DitaStudio.Core.Model;
using DitaStudio.Core.Validation;

namespace DitaStudio.SamplePlugins;

/// <summary>
/// Правило проверки: слишком длинные абзацы, пункты списков и команды шагов. Длинную фразу трудно прочитать с первого раза,
/// а команду шага — выполнить. Замечание уровня «Сведения»: рекомендация, а не ошибка.
/// </summary>
public sealed class LongBlockRule : IValidationRulePlugin
{
    /// <summary>Больше скольких слов — уже «длинно».</summary>
    private const int MaxWords = 60;

    /// <summary>Для команды шага предел строже: одно действие — одно короткое предложение.</summary>
    private const int MaxCommandWords = 25;

    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal) { "p", "li", "cmd" };

    public string Name => "Пример: длинные абзацы и команды";

    public IEnumerable<ValidationIssue> Check(DitaDocument document)
    {
        foreach (var node in document.Root.Descendants())
        {
            if (node.Kind != NodeKind.Element || !Blocks.Contains(node.Name))
            {
                continue;
            }

            var words = node.InnerText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var limit = node.Name == "cmd" ? MaxCommandWords : MaxWords;
            if (words > limit)
            {
                // Узел — для перехода двойным щелчком из вкладки «Проверка», путь к файлу — для проверки всего проекта.
                yield return new ValidationIssue(IssueSeverity.Info,
                    $"<{node.Name}>: {words} слов — длиннее {limit}. Разбейте на несколько предложений или блоков.",
                    node, document.FilePath);
            }
        }
    }
}

/// <summary>
/// Правило проверки по словарю стиля: слова, которых в технической документации лучше избегать («просто», «очевидно»…).
/// Код и имена файлов не проверяются — в них такие слова могут быть частью имени.
/// </summary>
public sealed class StopWordsRule : IValidationRulePlugin
{
    private static readonly string[] StopWords = { "просто", "очевидно", "легко", "всего лишь", "и т. д." };

    /// <summary>Элементы, текст которых не проверяется.</summary>
    private static readonly HashSet<string> CodeElements = new(StringComparer.Ordinal)
    {
        "codeph", "codeblock", "filepath", "cmdname", "varname", "parmname", "apiname", "userinput", "systemoutput", "msgph", "screen"
    };

    public string Name => "Пример: слова-паразиты";

    public IEnumerable<ValidationIssue> Check(DitaDocument document)
    {
        foreach (var text in document.Root.Descendants().Where(n => n.Kind == NodeKind.Text))
        {
            if (text.Ancestors().Any(a => CodeElements.Contains(a.Name)))
            {
                continue;
            }

            foreach (var word in StopWords)
            {
                if (ContainsWord(text.Value, word))
                {
                    // Замечание ставится на элемент, в котором стоит текст: к нему переходит двойной щелчок.
                    yield return new ValidationIssue(IssueSeverity.Warning,
                        $"«{word}»: в инструкции лучше без этого слова — читателю может быть совсем не просто.",
                        text.Parent, document.FilePath);
                }
            }
        }
    }

    /// <summary>Слово целиком, без учёта регистра: «просто» находится, «простой» — нет.</summary>
    private static bool ContainsWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var before = at == 0 || !char.IsLetter(text[at - 1]);
            var end = at + word.Length;
            var after = end >= text.Length || !char.IsLetter(text[end]);
            if (before && after)
            {
                return true;
            }
        }

        return false;
    }
}
