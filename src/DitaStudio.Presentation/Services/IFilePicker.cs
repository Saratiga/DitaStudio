namespace DitaStudio.Presentation.Services;

/// <summary>Фильтр файлов: название и маски вида <c>*.dita</c>.</summary>
public sealed record FileFilter(string Name, params string[] Patterns)
{
    public static readonly FileFilter All = new("Все файлы", "*.*");
}

/// <summary>Системные окна выбора файлов и папок. null — пользователь отказался.</summary>
public interface IFilePicker
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters, string? initialDirectory = null);

    Task<string?> OpenFolderAsync(string title, string? initialDirectory = null);

    Task<string?> SaveFileAsync(string title, IReadOnlyList<FileFilter> filters, string? suggestedFileName = null, string? initialDirectory = null);
}
