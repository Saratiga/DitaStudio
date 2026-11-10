using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Core.Publishing;

/// <summary>Что ставится вместо пустой перекрёстной ссылки (как «Вставить ссылку на» в Word). Задаётся классом
/// <c>outputclass</c> у <c>xref</c>: <c>xref-full</c>, <c>xref-label</c>, <c>xref-number</c>, <c>xref-text</c>.</summary>
public enum XrefForm
{
    /// <summary>По виду цели: рисунок и таблица — «Рисунок 3», пункт и сноска — номер, раздел и топик — название.</summary>
    Default,

    /// <summary>Подпись целиком: «Рисунок 3. Схема подключения».</summary>
    Full,

    /// <summary>Постоянная часть и номер: «Рисунок 3».</summary>
    Label,

    /// <summary>Только номер: «3», «2.3.1».</summary>
    Number,

    /// <summary>Только название: «Схема подключения».</summary>
    Text
}

/// <summary>Вид элемента, на который ставится перекрёстная ссылка.</summary>
public enum XrefTargetKind
{
    Topic,
    Section,
    Figure,
    Table,
    NumberedItem,
    Footnote
}

/// <summary>
/// Номера и подписи целей перекрёстных ссылок в издании. Номер рисунка, таблицы, пункта или сноски становится известен
/// только при отрисовке цели, а ссылка может стоять раньше неё (и в другом топике). Поэтому пустая ссылка выводится
/// меткой-заместителем (<see cref="Token"/>), цели при отрисовке записывают свои номера (<see cref="Record"/>), а после
/// обхода всего издания метки заменяются текстом (<see cref="Resolve"/>). Общий механизм для HTML и DOCX.
/// </summary>
public sealed class CrossReferenceLabels
{
    private const char Open = '\u0002';
    private const char Close = '\u0003';

    private sealed record Entry(XrefTargetKind Kind, string? Label, string? Number, string Text, string? Separator);

    private sealed record Pending(string Key, XrefForm Form, string Fallback);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Pending> _pending = new();

    public const string FullClass = "xref-full";
    public const string LabelClass = "xref-label";
    public const string NumberClass = "xref-number";
    public const string TextClass = "xref-text";

    /// <summary>Вид текста ссылки по её <c>outputclass</c>.</summary>
    public static XrefForm FormOf(DitaNode xref)
    {
        var classes = (xref.GetAttribute("outputclass") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return classes.Contains(FullClass) ? XrefForm.Full
            : classes.Contains(LabelClass) ? XrefForm.Label
            : classes.Contains(NumberClass) ? XrefForm.Number
            : classes.Contains(TextClass) ? XrefForm.Text
            : XrefForm.Default;
    }

    /// <summary>Класс <c>outputclass</c> для вида текста (null — по умолчанию).</summary>
    public static string? ClassOf(XrefForm form) => form switch
    {
        XrefForm.Full => FullClass,
        XrefForm.Label => LabelClass,
        XrefForm.Number => NumberClass,
        XrefForm.Text => TextClass,
        _ => null
    };

    /// <summary>
    /// Цель ссылки внутри DITA-файла для подписи по номеру: путь и id элемента (у ссылки на топик с видом текста,
    /// отличным от названия, — id топика). null — keyref, внешняя ссылка или ссылка на топик целиком.
    /// </summary>
    public static (string? Path, string Id)? TargetOf(DitaNode xref, string? documentPath)
    {
        var href = xref.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) || !string.IsNullOrWhiteSpace(xref.GetAttribute("keyref")) || RefResolver.IsExternal(href!) ||
            xref.GetAttribute("scope") is "external" or "peer" || documentPath is null)
        {
            return null;
        }

        var reference = RefResolver.Parse(documentPath, href!);
        var path = reference.Path ?? documentPath;
        if (reference.ElementId is { Length: > 0 } elementId)
        {
            return (path, elementId);
        }

        return FormOf(xref) is not XrefForm.Default && reference.TopicId is { Length: > 0 } topicId
            ? (path, topicId)
            : null;
    }

    /// <summary>Имя скрытой закладки Word на цель ссылки (рисунок, таблицу, пункт, раздел) — одинаковое у цели и у ссылки.</summary>
    public static string BookmarkName(string? path, string id)
    {
        var hash = 2166136261u;
        foreach (var ch in Key(path, id))
        {
            hash = (hash ^ ch) * 16777619u;
        }

        return "_Ref" + hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Key(string? path, string id) =>
        (path is null ? string.Empty : Path.GetFullPath(path).ToUpperInvariant()) + "#" + id;

    /// <summary>
    /// Цель отрисована: <paramref name="label"/> — постоянная часть с номером («Рисунок 3»; null — подписи без номеров),
    /// <paramref name="number"/> — номер («3», «2.3.1»), <paramref name="text"/> — название, <paramref name="separator"/> —
    /// разделитель между номером и названием в подписи. Цель без id не записывается: на неё не сослаться.
    /// </summary>
    public void Record(string? path, string? id, XrefTargetKind kind, string? label, string? number, string text, string? separator = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var key = Key(path, id!);
        _entries.TryAdd(key, new Entry(kind, label, number, text.Trim(), separator)); // первое появление — как в издании
    }

    /// <summary>Есть ли записанная цель (для проверок).</summary>
    public bool Has(string? path, string id) => _entries.ContainsKey(Key(path, id));

    /// <summary>Метка-заместитель текста ссылки на <paramref name="id"/> в <paramref name="path"/>; <paramref name="fallback"/> —
    /// если цели в издании не окажется (исключена условием, не входит в карту).</summary>
    public string Token(string? path, string id, XrefForm form, string fallback)
    {
        _pending.Add(new Pending(Key(path, id), form, fallback));
        return Open + (_pending.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + Close;
    }

    /// <summary>Текст ссылки по записанной цели (null — цель не записана).</summary>
    public string? TextFor(string? path, string id, XrefForm form) =>
        _entries.TryGetValue(Key(path, id), out var entry) ? Format(entry, form) : null;

    private static string Format(Entry entry, XrefForm form)
    {
        var number = entry.Number ?? string.Empty;
        if (form == XrefForm.Default)
        {
            form = entry.Kind switch
            {
                XrefTargetKind.Figure or XrefTargetKind.Table => entry.Label is null ? XrefForm.Text : XrefForm.Label,
                XrefTargetKind.NumberedItem or XrefTargetKind.Footnote => number.Length > 0 ? XrefForm.Number : XrefForm.Text,
                _ => XrefForm.Text
            };
        }

        return form switch
        {
            XrefForm.Label => entry.Label ?? (number.Length > 0 ? number : entry.Text),
            XrefForm.Number => number.Length > 0 ? number : entry.Text,
            XrefForm.Full => entry.Label is { } label
                ? entry.Text.Length > 0 ? label + (entry.Separator ?? ". ") + entry.Text : label
                : number.Length > 0 && entry.Kind is XrefTargetKind.Section or XrefTargetKind.Topic or XrefTargetKind.NumberedItem
                    ? (entry.Text.Length > 0 ? number + " " + entry.Text : number)
                    : entry.Text,
            _ => entry.Text.Length > 0 ? entry.Text : entry.Label ?? number
        };
    }

    /// <summary>Есть ли в тексте метки-заместители.</summary>
    public static bool HasTokens(string text) => text.IndexOf(Open) >= 0;

    /// <summary>Заменяет метки в <paramref name="text"/> текстом ссылок; <paramref name="escape"/> — экранирование (HTML).</summary>
    public string Resolve(string text, Func<string, string>? escape = null)
    {
        if (!HasTokens(text))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf(Open, i);
            var close = open < 0 ? -1 : text.IndexOf(Close, open + 1);
            if (open < 0 || close < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            sb.Append(text, i, open - i);
            var replacement = int.TryParse(text.AsSpan(open + 1, close - open - 1), out var index) && index >= 0 && index < _pending.Count
                ? _entries.TryGetValue(_pending[index].Key, out var entry) ? Format(entry, _pending[index].Form) : _pending[index].Fallback
                : string.Empty;
            sb.Append(escape is null ? replacement : escape(replacement));
            i = close + 1;
        }

        return sb.ToString();
    }
}
