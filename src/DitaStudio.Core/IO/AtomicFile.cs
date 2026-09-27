using System.Text;

namespace DitaStudio.Core.IO;

/// <summary>
/// Запись файла «всё или ничего»: текст пишется во временный файл в той же папке,
/// сбрасывается на диск и только потом подменяет оригинал. Если процесс упадёт
/// или кончится место посреди записи, на диске останется прежняя версия файла,
/// а не обрезанная новая.
/// </summary>
public static class AtomicFile
{
    /// <summary>Префикс временных файлов — по нему их можно опознать и не показывать в проекте.</summary>
    public const string TempPrefix = ".~dita-";

    public static void WriteAllText(string path, string text, Encoding encoding) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, encoding, bufferSize: -1, leaveOpen: true);
            writer.Write(text);
        });

    /// <summary>Запись через поток (например, <c>XDocument.Save(stream)</c>): поток ведёт во
    /// временный файл, оригинал подменяется, только если запись дошла до конца без исключения.</summary>
    public static void Write(string path, Action<Stream> write) =>
        WriteVia(path, temp =>
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            write(stream);
            stream.Flush(flushToDisk: true);
        });

    /// <summary>Для библиотек, которые сами создают файл по пути (OpenXML и т. п.):
    /// <paramref name="writeTemp"/> получает путь временного файла в той же папке и должен
    /// полностью записать и закрыть его; затем временный файл подменяет оригинал.</summary>
    public static void WriteVia(string path, Action<string> writeTemp)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);

        // Расширение .tmp обязательно: по нему недописанный файл не попадает в проект.
        var temp = Path.Combine(dir, TempPrefix + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            writeTemp(temp);

            if (File.Exists(full))
            {
                Replace(temp, full);
            }
            else
            {
                File.Move(temp, full);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void Replace(string temp, string target)
    {
        try
        {
            // File.Replace сохраняет атрибуты и права оригинала.
            File.Replace(temp, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (IOException) when (File.Exists(temp) && File.Exists(target))
        {
            // Некоторые файловые системы (сетевые папки, FAT) не умеют Replace —
            // перемещение с заменой тоже атомарно в пределах одного тома.
            File.Move(temp, target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // временный файл останется — оригинал при этом не тронут
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
