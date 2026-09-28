using System.Globalization;
using WeCantSpell.Hunspell;

namespace DitaStudio.Presentation.Authoring;

/// <summary>Слово с ошибкой в строке блока.</summary>
public readonly record struct Misspelling(int Start, int Length);

/// <summary>
/// Проверка орфографии по словарям Hunspell (WeCantSpell.Hunspell) — замена встроенной в WPF
/// проверки (<c>SpellCheck.IsEnabled</c>), которой в Avalonia нет. Словари — пары
/// <c>*.dic</c>/<c>*.aff</c> из папки; слово проверяется словарём своей письменности
/// (кириллица — ru, латиница — en). Словари грузятся в фоне при первом обращении: пока они не
/// готовы, <see cref="Find"/> ничего не находит, а <see cref="Changed"/> сообщает о готовности.
/// </summary>
public sealed class SpellChecker
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;
    private WordList? _cyrillic;
    private WordList? _latin;

    public SpellChecker(string directory)
    {
        _directory = directory;
    }

    /// <summary>Общий экземпляр со словарями из папки Dictionaries рядом с программой.</summary>
    public static SpellChecker Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "Dictionaries"));

    /// <summary>Проверка включена (Правка → Проверка орфографии).</summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsReady => _cyrillic is not null || _latin is not null;

    /// <summary>Словари загружены или слово добавлено в пропускаемые — пора перерисовать
    /// подчёркивания. После загрузки вызывается не из UI-потока.</summary>
    public event EventHandler? Changed;

    /// <summary>Начинает фоновую загрузку словарей (повторный вызов ничего не делает).</summary>
    public Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            return _loading ??= Task.Run(Load);
        }
    }

    private void Load()
    {
        _cyrillic = TryLoad("ru_RU");
        _latin = TryLoad("en_US");
        if (IsReady)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private WordList? TryLoad(string name)
    {
        var dic = Path.Combine(_directory, name + ".dic");
        var aff = Path.Combine(_directory, name + ".aff");
        if (!File.Exists(dic) || !File.Exists(aff))
        {
            return null;
        }

        try
        {
            return WordList.CreateFromFiles(dic, aff);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            // Повреждённый словарь — просто без проверки на этом языке.
            return null;
        }
    }

    /// <summary>«Пропустить все» — слово больше не подчёркивается до конца сеанса.</summary>
    public void Ignore(string word)
    {
        lock (_ignored)
        {
            _ignored.Add(word);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Слово написано верно (или проверить его нечем).</summary>
    public bool Check(string word)
    {
        if (!IsEnabled || !IsReady || word.Length < 2 || !IsCheckable(word))
        {
            return true;
        }

        lock (_ignored)
        {
            if (_ignored.Contains(word))
            {
                return true;
            }
        }

        var list = IsCyrillic(word) ? _cyrillic : _latin;
        if (list is null)
        {
            return true;
        }

        return CheckIn(list, word) ||
               // Составное через дефис («прокси-сервера»): в словаре часто только части.
               word.Contains('-') && word.Split('-').All(part => part.Length < 2 || CheckIn(list, part));
    }

    // Словарь ru_RU пишет «е» вместо «ё»: «ещё» проверяем и как «еще».
    private static bool CheckIn(WordList list, string word) =>
        list.Check(word) || list.Check(word.Replace('ё', 'е').Replace('Ё', 'Е'));

    /// <summary>Варианты исправления (до <paramref name="max"/>).</summary>
    public IReadOnlyList<string> Suggest(string word, int max = 6)
    {
        if (!IsReady)
        {
            return Array.Empty<string>();
        }

        var list = IsCyrillic(word) ? _cyrillic : _latin;
        return list is null ? Array.Empty<string>() : list.Suggest(word).Take(max).ToList();
    }

    /// <summary>
    /// Слова с ошибками в тексте. <paramref name="skip"/> — позиции, которые не проверяются
    /// (код, имена файлов, плашки).
    /// </summary>
    public List<Misspelling> Find(string text, Func<int, bool>? skip = null)
    {
        var result = new List<Misspelling>();
        if (!IsEnabled || !IsReady)
        {
            return result;
        }

        foreach (var (start, length) in Words(text))
        {
            if (skip is not null && skip(start))
            {
                continue;
            }

            if (!Check(text.Substring(start, length)))
            {
                result.Add(new Misspelling(start, length));
            }
        }

        return result;
    }

    /// <summary>Слова текста: буквы с апострофами и дефисами внутри («кое-где», «don't»).</summary>
    public static IEnumerable<(int Start, int Length)> Words(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) ||
                   (text[i] is '-' or '\'' or '’') && i + 1 < text.Length && char.IsLetter(text[i + 1]) && i > start))
            {
                i++;
            }

            yield return (start, i - start);
        }
    }

    /// <summary>
    /// Не проверяются: слова с цифрами (v2, x64), аббревиатуры в верхнем регистре (DITA, XML),
    /// слова с заглавной внутри (WebView, iPhone) и смешение алфавитов.
    /// </summary>
    private static bool IsCheckable(string word)
    {
        if (word.Any(char.IsDigit))
        {
            return false;
        }

        if (word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return false;
        }

        if (word.Skip(1).Any(char.IsUpper))
        {
            return false;
        }

        var cyrillic = word.Count(IsCyrillicLetter);
        var letters = word.Count(char.IsLetter);
        return cyrillic == 0 || cyrillic == letters;
    }

    private static bool IsCyrillic(string word) => word.Any(IsCyrillicLetter);

    private static bool IsCyrillicLetter(char c) =>
        char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter && c is >= 'Ѐ' and <= 'ӿ';
}
