using System.Globalization;
using DitaStudio.Core.IO;

namespace DitaStudio.Presentation;

/// <summary>
/// Размер окна подсказки элементов («Enter», «Ctrl+Enter», двойной щелчок по пустому месту): пользователь его растягивает,
/// и в следующий раз окно открывается таким же. Файл %APPDATA%\DitaStudio\suggestions-size.txt («ширина высота», px).
/// </summary>
public static class PopupSizeSettings
{
    public const double DefaultWidth = 560;
    public const double DefaultHeight = 260;
    public const double MinWidth = 380;
    public const double MinHeight = 200;
    public const double MaxWidth = 1400;
    public const double MaxHeight = 1000;

    /// <summary>Путь к файлу; тесты подменяют.</summary>
    public static string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "suggestions-size.txt");

    public static (double Width, double Height) Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var parts = File.ReadAllText(SettingsPath).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                {
                    return Clamp(width, height);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // не критично — окно откроется обычного размера
        }

        return (DefaultWidth, DefaultHeight);
    }

    public static void Save(double width, double height)
    {
        var (w, h) = Clamp(width, height);
        try
        {
            AtomicFile.WriteAllText(SettingsPath,
                w.ToString("0", CultureInfo.InvariantCulture) + " " + h.ToString("0", CultureInfo.InvariantCulture),
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // размер запомнится в следующий раз
        }
    }

    public static (double Width, double Height) Clamp(double width, double height) =>
        (double.IsFinite(width) ? Math.Clamp(width, MinWidth, MaxWidth) : DefaultWidth,
         double.IsFinite(height) ? Math.Clamp(height, MinHeight, MaxHeight) : DefaultHeight);
}
