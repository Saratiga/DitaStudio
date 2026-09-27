using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DitaStudio.Core.IO;

/// <summary>Сохранённая копия несохранённых правок одного документа.</summary>
/// <param name="OriginalPath">Полный путь к исходному файлу.</param>
/// <param name="SavedAtUtc">Когда снята копия.</param>
/// <param name="OriginalStamp">Отпечаток исходного файла на момент копии — чтобы заметить,
/// что файл на диске успел поменяться после сбоя.</param>
/// <param name="Content">Текст документа (XML; может быть недописанным, если правили в режиме исходного кода).</param>
public sealed record RecoveryEntry(string OriginalPath, DateTime SavedAtUtc, FileStamp? OriginalStamp, string Content)
{
    /// <summary>Исходный файл изменился (или пропал) после того, как была снята копия.</summary>
    public bool OriginalChangedSince => FileStamp.Of(OriginalPath) != OriginalStamp;
}

/// <summary>
/// Копии несохранённых документов одного проекта для восстановления после сбоя.
/// Оригиналы не трогаются: копии лежат отдельно (по умолчанию в
/// <c>%LOCALAPPDATA%\DitaStudio\Recovery\&lt;проект&gt;</c>), по файлу на документ.
/// Копия удаляется, когда документ сохранён или пользователь сам отказался от правок.
/// </summary>
public sealed class RecoveryStore
{
    private const string Extension = ".recovery.json";

    // Файл локальный и в HTML не попадает — кириллицу и разметку оставляем читаемыми,
    // а не экранированными: копию можно открыть руками, если что-то пошло не так.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RecoveryStore(string directory)
    {
        Directory = directory;
    }

    /// <summary>Папка копий этого проекта.</summary>
    public string Directory { get; }

    /// <summary>Общая папка копий всех проектов.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DitaStudio", "Recovery");

    /// <summary>Хранилище для проекта: своя подпапка, чтобы копии разных проектов не смешивались.</summary>
    public static RecoveryStore ForProject(string rootDirectory, string projectRoot)
    {
        var full = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(name))
        {
            name = "project";
        }

        return new RecoveryStore(Path.Combine(rootDirectory, name + "-" + Hash(full)));
    }

    public void Save(string originalPath, string content, FileStamp? originalStamp)
    {
        var full = Path.GetFullPath(originalPath);
        var dto = new Dto
        {
            OriginalPath = full,
            SavedAtUtc = DateTime.UtcNow,
            StampLastWriteUtc = originalStamp?.LastWriteUtc,
            StampLength = originalStamp?.Length,
            Content = content
        };

        AtomicFile.WriteAllText(FileFor(full), JsonSerializer.Serialize(dto, JsonOptions), new UTF8Encoding(false));
    }

    public void Remove(string originalPath)
    {
        var file = FileFor(Path.GetFullPath(originalPath));
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    /// <summary>Все копии проекта, старые первыми. Повреждённые файлы пропускаются.</summary>
    public IReadOnlyList<RecoveryEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return Array.Empty<RecoveryEntry>();
        }

        var result = new List<RecoveryEntry>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(file), JsonOptions);
                if (dto?.OriginalPath is null || dto.Content is null)
                {
                    continue;
                }

                FileStamp? stamp = dto.StampLastWriteUtc is { } time && dto.StampLength is { } length
                    ? new FileStamp(DateTime.SpecifyKind(time, DateTimeKind.Utc), length)
                    : null;
                result.Add(new RecoveryEntry(dto.OriginalPath, dto.SavedAtUtc, stamp, dto.Content));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // недочитанная или чужая копия — не повод мешать открытию проекта
            }
        }

        return result.OrderBy(e => e.SavedAtUtc).ToList();
    }

    public void Clear()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            File.Delete(file);
        }
    }

    private string FileFor(string fullPath) => Path.Combine(Directory, Hash(fullPath) + Extension);

    // Регистр в путях Windows не значим — один и тот же файл даёт одно имя копии.
    private static string Hash(string fullPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private sealed class Dto
    {
        public string? OriginalPath { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public DateTime? StampLastWriteUtc { get; set; }
        public long? StampLength { get; set; }
        public string? Content { get; set; }
    }
}
