namespace DitaStudio.Core.IO;

/// <summary>
/// Отпечаток файла на диске: время последней записи и размер. Запоминается при
/// чтении и сохранении документа; если отпечаток на диске стал другим — файл
/// поменяла другая программа (git pull, соседний редактор).
/// </summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
{
    /// <summary>Отпечаток файла или <c>null</c>, если файла нет или он недоступен.</summary>
    public static FileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
