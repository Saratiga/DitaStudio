using DitaStudio.Core.IO;

namespace DitaStudio.Presentation;

/// <summary>Выбор темы (светлая/тёмная) между запусками — файл %APPDATA%\DitaStudio\theme.txt,
/// общий для WPF- и Avalonia-версий.</summary>
public static class ThemeSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "theme.txt");

    public static bool LoadDark()
    {
        try
        {
            return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "dark";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void SaveDark(bool dark)
    {
        try
        {
            AtomicFile.WriteAllText(SettingsPath, dark ? "dark" : "light", new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // выбор темы не критичен — в следующий раз откроется тема по умолчанию
        }
    }
}
