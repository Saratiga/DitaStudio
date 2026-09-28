using DitaStudio.Presentation.Services;
using Microsoft.Win32;

namespace DitaStudio.App.Services;

/// <summary>Системные окна выбора файлов Windows (Microsoft.Win32) для общих ViewModel'ей.</summary>
public sealed class WpfFilePicker : IFilePicker
{
    public Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ToWin32(filters) };
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> OpenFolderAsync(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (initialDirectory is not null)
        {
            dialog.DefaultDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    public Task<string?> SaveFileAsync(string title, IReadOnlyList<FileFilter> filters, string? suggestedFileName = null, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = ToWin32(filters) };
        if (suggestedFileName is not null)
        {
            dialog.FileName = suggestedFileName;
        }

        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    /// <summary>«Файлы DITA|*.dita;*.xml|Все файлы|*.*» — формат фильтра Win32.</summary>
    private static string ToWin32(IReadOnlyList<FileFilter> filters) =>
        string.Join("|", filters.Select(f => f.Name + "|" + string.Join(";", f.Patterns)));
}
