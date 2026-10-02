using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace DitaStudio.Core.Localization;

/// <summary>
/// Язык интерфейса редактора и доступ к его строкам. Строки лежат в ресурсах <c>Resources/Strings.resx</c> (английский — основной)
/// и <c>Strings.ru.resx</c>; ключ, которого нет в языке, берётся из английского, а если нет и там — показывается сам ключ.
/// Язык выбирается так: сохранённый выбор пользователя, иначе язык системы (если он поддерживается), иначе английский.
/// Смена языка на лету: <see cref="LanguageChanged"/> и <see cref="INotifyPropertyChanged"/> (индексатор) —
/// привязки в XAML обновляются сами.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("DitaStudio.Core.Resources.Strings", typeof(Loc).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo(UiLanguages.English);

    public static Loc Instance { get; } = new();

    /// <summary>Код действующего языка интерфейса ("en", "ru").</summary>
    public string Language => _culture.Name;

    /// <summary>Выбор пользователя: код языка или null — «как в системе».</summary>
    public string? UserChoice { get; private set; }

    /// <summary>Строка интерфейса по ключу — индексатор для кода.</summary>
    public string this[string key] => Get(key);

    private readonly Dictionary<string, LocEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Строка как наблюдаемый объект с одним свойством <see cref="LocEntry.Value"/> — для привязок в XAML: при смене языка
    /// свойство сообщает об изменении, и подпись обновляется без перезапуска.
    /// </summary>
    public LocEntry Entry(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new LocEntry(this, key);
            _entries[key] = entry;
        }

        return entry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Срабатывает после смены языка: код, построенный один раз (меню, подписи), перестраивается.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Строка на текущем языке; нет ни в нём, ни в английском — сам ключ (видно сразу, что строка не добавлена).</summary>
    public string Get(string key) =>
        Resources.GetString(key, _culture) ?? Resources.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>Строка на текущем языке или null, если для ключа её нет (описания элементов DITA откатываются на текст каталога).</summary>
    public string? Find(string key) =>
        Resources.GetString(key, _culture) ?? Resources.GetString(key, CultureInfo.InvariantCulture);

    public static string T(string key) => Instance.Get(key);

    /// <summary>Строка с подстановкой: ключ и значения для {0}, {1}… из ресурса.</summary>
    public static string T(string key, params object?[] args) =>
        string.Format(Instance._culture, Instance.Get(key), args);

    /// <summary>Выбирает язык при запуске: сохранённый выбор пользователя, иначе язык системы, иначе английский.</summary>
    public void Initialize(IEnumerable<string>? systemLanguages = null)
    {
        UserChoice = UiLanguages.Normalize(LanguageSettings.Load());
        Apply(UiLanguages.Resolve(UserChoice, systemLanguages ?? UiLanguages.SystemLanguages()));
    }

    /// <summary>
    /// Пользователь выбрал язык: код — запоминается между запусками; null — «как в системе», сохранённый выбор сбрасывается.
    /// </summary>
    public void SetUserLanguage(string? code, IEnumerable<string>? systemLanguages = null)
    {
        UserChoice = UiLanguages.Normalize(code);
        LanguageSettings.Save(UserChoice);
        Apply(UiLanguages.Resolve(UserChoice, systemLanguages ?? UiLanguages.SystemLanguages()));
    }

    private void Apply(string code)
    {
        var changed = !string.Equals(_culture.Name, code, StringComparison.Ordinal);
        // CurrentUICulture не трогаем: по нему определяется язык системы («Как в системе»), и подмена сломала бы возврат к нему.
        _culture = CultureInfo.GetCultureInfo(code);
        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            foreach (var entry in _entries.Values.ToList())
            {
                entry.Refresh();
            }

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Одна строка интерфейса для привязки: <c>Value</c> меняется вместе с языком.</summary>
public sealed class LocEntry : INotifyPropertyChanged
{
    private readonly Loc _loc;
    private readonly string _key;

    internal LocEntry(Loc loc, string key)
    {
        _loc = loc;
        _key = key;
    }

    public string Value => _loc.Get(_key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
