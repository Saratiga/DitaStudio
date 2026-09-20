using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Schema.Dtd;

/// <summary>
/// Читает настоящий DTD-синтаксис: ENTITY/ELEMENT/ATTLIST, параметрические сущности (включая
/// внешние — SYSTEM/PUBLIC, с рекурсивным чтением файла), и «голые» ссылки %entity; на верхнем
/// уровне — реальные модульные DTD DITA именно так подключают целые группы деклараций домена
/// (например %ui-d-dec; в конце topic.dtd). Контент-модели и списки атрибутов не разбирает —
/// только извлекает их сырой текст с уже раскрытыми сущностями; разбором контент-модели
/// занимается уже существующий <see cref="ModelParser"/> (синтаксис DTD и внутреннего DSL
/// каталога для контент-моделей совпадает: (a,b?,c*), #PCDATA, |).
/// </summary>
public static class DtdReader
{
    public static DtdSchema Read(string rootPath)
    {
        var schema = new DtdSchema();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadFile(rootPath, schema, visited);
        return schema;
    }

    private static void ReadFile(string path, DtdSchema schema, HashSet<string> visited)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            schema.Warnings.Add($"Некорректный путь \"{path}\": {ex.Message}");
            return;
        }

        if (!visited.Add(full))
        {
            return; // уже читали — защита от циклических include
        }

        if (!File.Exists(full))
        {
            schema.Warnings.Add($"Файл не найден: {full}");
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex)
        {
            schema.Warnings.Add($"Не удалось прочитать {full}: {ex.Message}");
            return;
        }

        ReadText(StripComments(text), Path.GetDirectoryName(full) ?? ".", schema, visited);
    }

    private static void ReadText(string text, string baseDir, DtdSchema schema, HashSet<string> visited)
    {
        var pos = 0;
        while (pos < text.Length)
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }

            if (pos >= text.Length)
            {
                break;
            }

            if (text[pos] == '%')
            {
                var end = text.IndexOf(';', pos);
                if (end < 0)
                {
                    break;
                }

                var name = text[(pos + 1)..end].Trim();
                pos = end + 1;
                if (schema.Entities.TryGetValue(name, out var value))
                {
                    // Голая %entity; на верхнем уровне может раскрыться в целые ELEMENT/ATTLIST —
                    // рекурсивно сканируем подстановку как продолжение потока деклараций.
                    ReadText(value, baseDir, schema, visited);
                }

                continue;
            }

            if (pos + 1 < text.Length && text[pos] == '<' && text[pos + 1] == '!')
            {
                var end = FindDeclarationEnd(text, pos);
                var decl = text[pos..(end + 1)];
                pos = end + 1;
                HandleDeclaration(decl, baseDir, schema, visited);
                continue;
            }

            pos++;
        }
    }

    /// <summary>Ищет закрывающую '&gt;' декларации, не обращая внимание на '&gt;' внутри
    /// кавычек (значения атрибутов по умолчанию иногда его содержат).</summary>
    private static int FindDeclarationEnd(string text, int start)
    {
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    private static void HandleDeclaration(string decl, string baseDir, DtdSchema schema, HashSet<string> visited)
    {
        var inner = decl.Length >= 3 ? decl[2..^1].Trim() : string.Empty;

        if (inner.StartsWith("ENTITY", StringComparison.Ordinal))
        {
            HandleEntity(inner["ENTITY".Length..].TrimStart(), baseDir, schema, visited);
        }
        else if (inner.StartsWith("ELEMENT", StringComparison.Ordinal))
        {
            HandleElement(inner["ELEMENT".Length..].Trim(), schema);
        }
        else if (inner.StartsWith("ATTLIST", StringComparison.Ordinal))
        {
            HandleAttlist(inner["ATTLIST".Length..].Trim(), schema);
        }

        // NOTATION, DOCTYPE и прочее — не нужны для контент-моделей, пропускаем.
    }

    private static void HandleEntity(string rest, string baseDir, DtdSchema schema, HashSet<string> visited)
    {
        if (!rest.StartsWith("%", StringComparison.Ordinal))
        {
            return; // обычная (не параметрическая) сущность — контент-моделей не касается
        }

        rest = rest[1..].TrimStart();
        var nameEnd = IndexOfWhitespace(rest);
        if (nameEnd < 0)
        {
            return;
        }

        var name = rest[..nameEnd];
        rest = rest[nameEnd..].Trim();

        if (rest.StartsWith("SYSTEM", StringComparison.Ordinal))
        {
            var path = ExtractQuoted(rest["SYSTEM".Length..].TrimStart(), out _);
            if (path is not null)
            {
                ReadFile(Path.Combine(baseDir, path), schema, visited);
            }

            return;
        }

        if (rest.StartsWith("PUBLIC", StringComparison.Ordinal))
        {
            // Публичный идентификатор игнорируем — каталогов OASIS не резолвим, системный путь
            // у настоящих DITA-DTD присутствует всегда рядом с PUBLIC.
            var afterPublicId = ExtractQuoted(rest["PUBLIC".Length..].TrimStart(), out var afterPubIdRest);
            if (afterPublicId is null)
            {
                return;
            }

            var path = ExtractQuoted(afterPubIdRest.TrimStart(), out _);
            if (path is not null)
            {
                ReadFile(Path.Combine(baseDir, path), schema, visited);
            }

            return;
        }

        var value = ExtractQuoted(rest, out _);
        if (value is not null)
        {
            schema.Entities[name] = value;
        }
    }

    private static void HandleElement(string rest, DtdSchema schema)
    {
        var nameEnd = IndexOfWhitespace(rest);
        if (nameEnd < 0)
        {
            return;
        }

        var name = rest[..nameEnd];
        if (name.StartsWith("(", StringComparison.Ordinal))
        {
            return; // группа имён "(a|b) (...)" — редкий случай, не поддерживаем
        }

        var model = ModelParser.ExpandEntities(rest[nameEnd..].Trim(), schema.Entities);
        schema.ElementModels[name] = model;
    }

    private static void HandleAttlist(string rest, DtdSchema schema)
    {
        var nameEnd = IndexOfWhitespace(rest);
        if (nameEnd < 0)
        {
            return;
        }

        var name = rest[..nameEnd];
        var attrText = ModelParser.ExpandEntities(rest[nameEnd..].Trim(), schema.Entities);
        schema.AttlistText[name] = schema.AttlistText.TryGetValue(name, out var existing)
            ? existing + " " + attrText
            : attrText;
    }

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Читает ведущую "строку в кавычках", возвращает её содержимое (без кавычек) и
    /// остаток текста после неё. Null, если текст не начинается с кавычки.</summary>
    private static string? ExtractQuoted(string text, out string rest)
    {
        rest = text;
        if (text.Length == 0 || text[0] is not ('"' or '\''))
        {
            return null;
        }

        var quote = text[0];
        var end = text.IndexOf(quote, 1);
        if (end < 0)
        {
            return null;
        }

        rest = text[(end + 1)..];
        return text[1..end];
    }

    private static string StripComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (i + 3 < text.Length && text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 3;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }
}
