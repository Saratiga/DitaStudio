using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Project;

/// <summary>Создание папок внутри проекта: проверка имени и сама папка (в том числе вложенная, «tasks/new»).</summary>
public static class ProjectFolders
{
    private static readonly char[] Separators = { '/', '\\' };
    private static readonly char[] Forbidden = { ':', '*', '?', '"', '<', '>', '|' };

    // Имена устройств Windows нельзя использовать для папок и файлов, даже с расширением.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Имя новой папки как набрал пользователь: части через <c>/</c> или <c>\</c> — вложенные папки.</summary>
    public static IReadOnlyList<string> Segments(string name) =>
        name.Split(Separators, StringSplitOptions.None).Select(s => s.Trim()).ToList();

    /// <summary>Сообщение, почему имя не годится; null — имя допустимо.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Loc.T("Core_TheFolderNameIsEmpty");
        }

        foreach (var segment in Segments(name))
        {
            if (segment.Length == 0)
            {
                return Loc.T("Core_TheFolderNameHasAnEmptyPart");
            }

            if (segment.IndexOfAny(Forbidden) >= 0 || segment.Any(char.IsControl))
            {
                return Loc.T("Core_TheFolderNameMustNotContain", string.Join(' ', Forbidden));
            }

            if (segment.EndsWith('.') || segment.All(c => c == '.'))
            {
                return Loc.T("Core_TheFolderNameMustNotEndWithADot", segment);
            }

            if (segment.StartsWith('.'))
            {
                return Loc.T("Core_TheFolderNameMustNotStartWithADot", segment); // такие папки проект считает служебными и не показывает
            }

            if (Reserved.Contains(segment.Split('.')[0]))
            {
                return Loc.T("Core_TheFolderName0IsReservedBy", segment);
            }

            if (segment.Length > 100)
            {
                return Loc.T("Core_TheFolderNameIsTooLong");
            }
        }

        return null;
    }

    /// <summary>
    /// Создаёт папку <paramref name="name"/> внутри <paramref name="parent"/> (она должна лежать в проекте). Возвращает полный путь самой глубокой
    /// папки. Исключения: <see cref="ArgumentException"/> — имя недопустимо или папка вне проекта, <see cref="IOException"/> — папка (или файл с таким именем) уже есть
    /// либо диск не дал создать.
    /// </summary>
    public static string Create(DitaProject project, string parent, string name)
    {
        if (Validate(name) is { } error)
        {
            throw new ArgumentException(error, nameof(name));
        }

        var root = Path.GetFullPath(project.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(parent, string.Join(Path.DirectorySeparatorChar, Segments(name))));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(Loc.T("Core_TheFolderMustBeInsideTheProject"), nameof(parent));
        }

        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new IOException(Loc.T("Core_TheFolder0AlreadyExists", Path.GetRelativePath(root, target)));
        }

        Directory.CreateDirectory(target);
        return target;
    }
}
