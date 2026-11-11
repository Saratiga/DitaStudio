using System.Text;
using DitaStudio.Core.IO;

namespace DitaStudio.Core.Project;

/// <summary>
/// Папка списка продуктов — настройка редактора, а не проекта: путь к ней хранится в %APPDATA%\DitaStudio\products-folder.txt
/// и действует в любом открытом проекте. Если папка задана, список продуктов читается и пишется в файл
/// <see cref="FileName"/> в ней (один список на все проекты); не задана — как раньше, в <c>.ditastudio-products</c> папки проекта.
/// </summary>
public static class ProductListFolder
{
    /// <summary>Имя файла списка в выбранной папке.</summary>
    public const string FileName = "products.ditastudio-products";

    /// <summary>Путь к файлу настройки (меняется только в тестах).</summary>
    public static string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "products-folder.txt");

    /// <summary>Выбранная папка (полный путь) или null — список хранится в проекте.</summary>
    public static string? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            var value = File.ReadAllText(SettingsPath).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Запоминает папку; null или пусто — сброс (список снова хранится в проекте). Сбой записи — исключение IOException.</summary>
    public static void Save(string? folder)
    {
        var value = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());
        if (value is null)
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }

            return;
        }

        AtomicFile.WriteAllText(SettingsPath, value, new UTF8Encoding(false));
    }

    /// <summary>Файл списка в папке <paramref name="folder"/>.</summary>
    public static string FileIn(string folder) => Path.Combine(folder, FileName);
}
