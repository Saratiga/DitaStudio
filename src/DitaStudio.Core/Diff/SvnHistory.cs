namespace DitaStudio.Core.Diff;

/// <summary>Чтение версий файла из истории SVN через CLI (без библиотеки — ядро без внешних
/// зависимостей). Требует установленный svn в PATH; если его нет, файл не под версионным
/// контролем или ревизии нет — возвращает null. В отличие от git, svn принимает путь рабочей
/// копии напрямую — репозиторий/relative-путь искать не нужно.</summary>
public static class SvnHistory
{
    /// <summary>Содержимое файла на ревизии (по умолчанию BASE — последняя версия, полученная
    /// из репозитория при обновлении рабочей копии; не требует сети, в отличие от HEAD).</summary>
    public static string? ReadRevision(string filePath, string revision = "BASE")
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return dir is null ? null : RunSvn(dir, "cat", "-r", revision, filePath);
    }

    /// <summary>Под версионным контролем ли файл (для показа/скрытия пункта меню).</summary>
    public static bool IsInRepository(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return dir is not null && RunSvn(dir, "info", filePath) is not null;
    }

    private static string? RunSvn(string workingDirectory, params string[] arguments) =>
        VcsProcess.Run("svn", workingDirectory, arguments);
}
