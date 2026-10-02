using System.Globalization;
using System.Text;
using DitaStudio.Core.IO;

namespace DitaStudio.Core.Localization;

/// <summary>Языки интерфейса, которые есть в редакторе, и выбор между ними.</summary>
public static class UiLanguages
{
    public const string English = "en";
    public const string Russian = "ru";

    /// <summary>Поддерживаемые языки; английский — основной (на него откат, если ничего не подошло).</summary>
    public static IReadOnlyList<string> Supported { get; } = new[] { English, Russian };

    /// <summary>Название языка на нём самом — так его узнают в меню, на каком бы языке ни был интерфейс.</summary>
    public static string NativeName(string code) => code switch
    {
        Russian => "Русский",
        _ => "English"
    };

    /// <summary>"ru-RU" → "ru"; язык, которого в редакторе нет, или пустая строка — null.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var primary = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return Supported.Contains(primary) ? primary : null;
    }

    /// <summary>
    /// Какой язык взять: сохранённый выбор пользователя; нет его — первый из языков системы, который есть в редакторе;
    /// нет и такого — английский.
    /// </summary>
    public static string Resolve(string? userChoice, IEnumerable<string> systemLanguages)
    {
        if (Normalize(userChoice) is { } chosen)
        {
            return chosen;
        }

        foreach (var system in systemLanguages)
        {
            if (Normalize(system) is { } supported)
            {
                return supported;
            }
        }

        return English;
    }

    /// <summary>Языки системы по порядку предпочтения: язык интерфейса, затем язык региональных настроек.</summary>
    public static IEnumerable<string> SystemLanguages()
    {
        yield return CultureInfo.CurrentUICulture.Name;
        yield return CultureInfo.CurrentCulture.Name;
        yield return CultureInfo.InstalledUICulture.Name;
    }
}

/// <summary>Выбор языка между запусками — файл %APPDATA%\DitaStudio\language.txt (код языка); нет файла — «как в системе».</summary>
public static class LanguageSettings
{
    /// <summary>Путь к файлу настройки (меняется только в тестах).</summary>
    public static string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "language.txt");

    public static string? Load()
    {
        try
        {
            return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Запоминает выбор; null — выбор сброшен, язык снова определяется по системе.</summary>
    public static void Save(string? code)
    {
        try
        {
            if (code is null)
            {
                if (File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath);
                }

                return;
            }

            AtomicFile.WriteAllText(SettingsPath, code, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // выбор языка не критичен — в следующий раз язык определится по системе
        }
    }
}
