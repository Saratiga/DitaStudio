using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using DitaStudio.Core.Localization;

namespace DitaStudio.Tests;

// Язык интерфейса: выбор (сохранённое → система → английский), запоминание, откат строк, полнота ресурсов.
internal static partial class CoreChecks
{
    private static readonly ResourceManager UiStrings = new("DitaStudio.Core.Resources.Strings", typeof(Loc).Assembly);

    internal static void LocalizationTests()
    {
        Section("Язык интерфейса");

        // --- выбор языка
        Check(UiLanguages.Resolve(null, new[] { "ru-RU" }) == "ru", "язык системы ru-RU — русский");
        Check(UiLanguages.Resolve(null, new[] { "en-US" }) == "en", "язык системы en-US — английский");
        Check(UiLanguages.Resolve(null, new[] { "de-DE" }) == "en", "в системе только неподдерживаемый язык — английский");
        Check(UiLanguages.Resolve(null, new[] { "de-DE", "ru-RU" }) == "ru", "первый подходящий из языков системы");
        Check(UiLanguages.Resolve(null, Array.Empty<string>()) == "en", "системных языков нет — английский");
        Check(UiLanguages.Resolve("ru", new[] { "en-US" }) == "ru", "выбор пользователя важнее языка системы");
        Check(UiLanguages.Resolve("en", new[] { "ru-RU" }) == "en", "английский, выбранный пользователем, важнее русской системы");
        Check(UiLanguages.Resolve("fr", new[] { "ru-RU" }) == "ru", "сохранён язык, которого нет в редакторе, — берётся язык системы");
        Check(UiLanguages.Resolve("RU", Array.Empty<string>()) == "ru" && UiLanguages.Resolve("en_GB", Array.Empty<string>()) == "en",
            "регистр и регион в коде языка не мешают");
        Check(UiLanguages.Normalize("  ") is null && UiLanguages.Normalize(null) is null && UiLanguages.Normalize("zz") is null,
            "пустой и неизвестный язык — null");
        Check(UiLanguages.NativeName("ru") == "Русский" && UiLanguages.NativeName("en") == "English", "язык назван на себе самом");

        // --- запоминание выбора и переключение
        var originalPath = LanguageSettings.SettingsPath;
        var originalChoice = Loc.Instance.UserChoice;
        var originalLanguage = Loc.Instance.Language;
        var temp = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        LanguageSettings.SettingsPath = Path.Combine(temp, "language.txt");
        try
        {
            var loc = Loc.Instance;
            loc.Initialize(new[] { "ru-RU" });
            Check(loc.Language == "ru" && loc.UserChoice is null, "выбора нет, система русская — русский");
            Check(loc.Get("Language_Auto") == "Как в системе", "русская строка");

            var changes = 0;
            var itemChanged = false;
            loc.LanguageChanged += (_, _) => changes++;
            loc.PropertyChanged += (_, e) => itemChanged |= e.PropertyName == "Item[]";
            var entry = loc.Entry("Language_Auto");
            var entryChanges = 0;
            entry.PropertyChanged += (_, e) => entryChanges += e.PropertyName == "Value" ? 1 : 0;
            Check(ReferenceEquals(entry, loc.Entry("Language_Auto")) && entry.Value == "Как в системе", "строка для привязки: один объект на ключ");
            loc.SetUserLanguage("en", new[] { "ru-RU" });
            Check(loc.Language == "en" && loc.UserChoice == "en", "пользователь выбрал английский");
            Check(entry.Value == "System default" && entryChanges == 1, "привязанная строка сообщила о новом значении");
            Check(loc.Get("Language_Auto") == "System default", "строка стала английской");
            Check(changes == 1 && itemChanged, "смена языка сообщена (событие и индексатор — для привязок XAML)");
            Check(File.ReadAllText(LanguageSettings.SettingsPath).Trim() == "en", "выбор записан в файл");

            loc.SetUserLanguage("en", new[] { "ru-RU" });
            Check(changes == 1, "тот же язык ещё раз — не событие");

            loc.Initialize(new[] { "ru-RU" });
            Check(loc.Language == "en", "при следующем запуске выбор пользователя важнее языка системы");

            loc.SetUserLanguage(null, new[] { "ru-RU" });
            Check(loc.Language == "ru" && loc.UserChoice is null && !File.Exists(LanguageSettings.SettingsPath),
                "«как в системе» — выбор сброшен, файл удалён, язык снова по системе");

            Check(loc.Get("Нет_такого_ключа") == "Нет_такого_ключа", "ключа нет нигде — виден сам ключ");

            File.WriteAllText(LanguageSettings.SettingsPath, "klingon");
            loc.Initialize(new[] { "de-DE" });
            Check(loc.Language == "en", "в файле неизвестный язык, в системе тоже — английский");
        }
        finally
        {
            // Состояние возвращается, пока путь настройки ещё временный: настоящий файл пользователя не трогаем.
            Loc.Instance.SetUserLanguage(originalChoice, new[] { originalLanguage });
            LanguageSettings.SettingsPath = originalPath;
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
                // временная папка удалится системой
            }
        }

        // --- полнота ресурсов
        var neutral = ResourceKeys(CultureInfo.InvariantCulture);
        var russian = ResourceKeys(CultureInfo.GetCultureInfo("ru"));
        Check(neutral.Count > 0, "английские ресурсы найдены");
        var onlyEn = neutral.Keys.Except(russian.Keys).ToList();
        var onlyRu = russian.Keys.Except(neutral.Keys).ToList();
        Check(onlyEn.Count == 0, "нет русского перевода: " + string.Join(", ", onlyEn.Take(10)));
        Check(onlyRu.Count == 0, "нет английской строки (русская лишняя): " + string.Join(", ", onlyRu.Take(10)));
        Check(neutral.Values.All(v => v.Trim().Length > 0) && russian.Values.All(v => v.Trim().Length > 0), "пустых строк нет");
        var placeholderMismatch = neutral.Keys.Intersect(russian.Keys)
            .Where(k => Placeholders(neutral[k]) != Placeholders(russian[k])).ToList();
        Check(placeholderMismatch.Count == 0, "подстановки {0} различаются между языками: " + string.Join(", ", placeholderMismatch.Take(10)));

        // --- каждый ключ, на который ссылается код или разметка, есть в ресурсах
        var used = UsedResourceKeys(RepositoryRoot());
        var unknown = used.Where(u => !neutral.ContainsKey(u.Key)).Select(u => $"{u.Key} ({u.File})").ToList();
        Check(unknown.Count == 0, "ключи без строки в ресурсах: " + string.Join("; ", unknown.Take(10)));
    }

    private static Dictionary<string, string> ResourceKeys(CultureInfo culture)
    {
        var set = UiStrings.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set is null)
        {
            return result;
        }

        foreach (System.Collections.DictionaryEntry entry in set)
        {
            result[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        return result;
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).OrderBy(x => x));

    private static List<(string Key, string File)> UsedResourceKeys(string root)
    {
        var found = new List<(string, string)>();
        var code = new Regex(@"Loc\.(?:T|Instance\.Get)\(\s*""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
        var xaml = new Regex(@"\{loc:Tr\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}"))
            {
                continue;
            }

            Regex? pattern = file.EndsWith(".cs", StringComparison.Ordinal) ? code : file.EndsWith(".axaml", StringComparison.Ordinal) ? xaml : null;
            if (pattern is null)
            {
                continue;
            }

            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                found.Add((match.Groups[1].Value, Path.GetRelativePath(root, file)));
            }
        }

        return found;
    }
}
