using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

/// <summary>Системные окна выбора файлов через StorageProvider окна (на Linux — портал
/// рабочего стола или GTK, на macOS — NSOpenPanel, на Windows — стандартный диалог).</summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    private readonly Func<TopLevel?> _topLevel;

    public AvaloniaFilePicker(Func<TopLevel?> topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters, string? initialDirectory = null)
    {
        if (_topLevel()?.StorageProvider is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ToFileTypes(filters),
            SuggestedStartLocation = await FolderAsync(storage, initialDirectory)
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenFolderAsync(string title, string? initialDirectory = null)
    {
        if (_topLevel()?.StorageProvider is not { } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(storage, initialDirectory)
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, IReadOnlyList<FileFilter> filters, string? suggestedFileName = null, string? initialDirectory = null)
    {
        if (_topLevel()?.StorageProvider is not { } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = ToFileTypes(filters),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(storage, initialDirectory)
        });

        return file?.TryGetLocalPath();
    }

    private static List<FilePickerFileType> ToFileTypes(IReadOnlyList<FileFilter> filters) =>
        filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList();

    private static async Task<IStorageFolder?> FolderAsync(IStorageProvider storage, string? path) =>
        path is not null && Directory.Exists(path) ? await storage.TryGetFolderFromPathAsync(path) : null;
}
