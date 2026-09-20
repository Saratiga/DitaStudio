namespace DitaStudio.Core.Diff;

/// <summary>Чтение версий файла из git-истории через CLI (без библиотеки libgit2 — ядро
/// без внешних зависимостей). Требует установленный git в PATH; если его нет, файл не
/// в репозитории или не отслеживается на указанной ревизии — возвращает null.</summary>
public static class GitHistory
{
    /// <summary>Содержимое файла на ревизии (по умолчанию HEAD — последний коммит).</summary>
    public static string? ReadRevision(string filePath, string revision = "HEAD")
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (dir is null)
        {
            return null;
        }

        var repoRoot = RunGit(dir, "rev-parse", "--show-toplevel")?.Trim();
        if (string.IsNullOrEmpty(repoRoot))
        {
            return null;
        }

        var normalizedRoot = repoRoot!.Replace('/', Path.DirectorySeparatorChar);
        var relative = Path.GetRelativePath(normalizedRoot, filePath).Replace('\\', '/');
        return RunGit(normalizedRoot, "show", $"{revision}:{relative}");
    }

    /// <summary>Есть ли у файла путь в git-репозитории (для показа/скрытия пункта меню).</summary>
    public static bool IsInRepository(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return dir is not null && RunGit(dir, "rev-parse", "--show-toplevel") is not null;
    }

    private static string? RunGit(string workingDirectory, params string[] arguments) =>
        VcsProcess.Run("git", workingDirectory, arguments);
}
