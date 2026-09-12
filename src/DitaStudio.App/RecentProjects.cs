namespace DitaStudio.App;

/// <summary>Список недавно открытых папок проектов, сохраняется между запусками (по образцу ThemeManager).</summary>
public static class RecentProjects
{
    private const int MaxEntries = 8;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return Array.Empty<string>();
            }

            return File.ReadAllLines(SettingsPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && Directory.Exists(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Add(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var list = Load().Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Insert(0, full);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllLines(SettingsPath, list.Take(MaxEntries));
        }
        catch
        {
            // список недавних проектов не критичен — молча продолжаем без сохранения на диск
        }
    }
}
