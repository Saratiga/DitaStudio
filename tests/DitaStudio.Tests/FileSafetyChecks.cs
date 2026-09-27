using System.Text;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Tests;

// Защита от потери данных: атомарная запись, отпечаток файла на диске
// (обнаружение чужих изменений) и копии для восстановления после сбоя.
internal static partial class CoreChecks
{
    internal static void FileSafetyTests()
    {
        Section("Защита файлов: атомарная запись, внешние изменения, восстановление");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            AtomicFileChecks(root);
            DiskStampChecks(root);
            RecoveryStoreChecks(root);
            ProjectSettingsChecks(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static void AtomicFileChecks(string root)
    {
        var path = Path.Combine(root, "atomic", "a.dita");
        AtomicFile.WriteAllText(path, "первая версия", new UTF8Encoding(false));
        Check(File.ReadAllText(path) == "первая версия", "atomic: создаёт новый файл (и недостающую папку)");

        AtomicFile.WriteAllText(path, "вторая", new UTF8Encoding(false));
        Check(File.ReadAllText(path) == "вторая", "atomic: заменяет существующий файл целиком, без хвоста старого текста");

        var bytes = File.ReadAllBytes(path);
        Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
            "atomic: UTF8Encoding(false) — без BOM, как прежний File.WriteAllText");

        var leftovers = Directory.GetFiles(Path.GetDirectoryName(path)!, AtomicFile.TempPrefix + "*");
        Check(leftovers.Length == 0, $"atomic: временных файлов не осталось ({leftovers.Length})");

        File.SetAttributes(path, FileAttributes.ReadOnly);
        var failed = false;
        try
        {
            AtomicFile.WriteAllText(path, "третья", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed = true;
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Check(failed, "atomic: файл только для чтения — ошибка, а не молчаливая подмена");
        Check(File.ReadAllText(path) == "вторая", "atomic: при ошибке записи оригинал остаётся прежним");
        leftovers = Directory.GetFiles(Path.GetDirectoryName(path)!, AtomicFile.TempPrefix + "*");
        Check(leftovers.Length == 0, "atomic: при ошибке временный файл удалён");

        // Запись через поток и через путь временного файла (для библиотек вроде OpenXML):
        // исключение посреди записи оставляет прежний файл и не оставляет мусора.
        var streamed = Path.Combine(root, "atomic", "export.xliff");
        AtomicFile.Write(streamed, stream => new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("xliff")).Save(stream));
        Check(File.ReadAllText(streamed).Contains("<xliff"), "atomic.Write: поток записан в файл");

        var threw = false;
        try
        {
            AtomicFile.Write(streamed, stream =>
            {
                stream.WriteByte((byte)'x');
                throw new InvalidOperationException("сбой посреди записи");
            });
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check(threw && File.ReadAllText(streamed).Contains("<xliff"), "atomic.Write: исключение при записи — оригинал цел");

        var viaPath = Path.Combine(root, "atomic", "book.docx");
        File.WriteAllText(viaPath, "прежний");
        string? tempSeen = null;
        AtomicFile.WriteVia(viaPath, temp =>
        {
            tempSeen = temp;
            File.WriteAllText(temp, "новый");
        });
        Check(File.ReadAllText(viaPath) == "новый" && tempSeen is not null &&
              Path.GetDirectoryName(tempSeen) == Path.GetDirectoryName(Path.GetFullPath(viaPath)) &&
              tempSeen.EndsWith(".tmp", StringComparison.Ordinal),
            "atomic.WriteVia: временный файл в той же папке с расширением .tmp, затем подменяет оригинал");

        try
        {
            AtomicFile.WriteVia(viaPath, temp =>
            {
                File.WriteAllText(temp, "обрезанный");
                throw new InvalidOperationException("сбой сборки DOCX");
            });
        }
        catch (InvalidOperationException)
        {
        }

        leftovers = Directory.GetFiles(Path.GetDirectoryName(path)!, AtomicFile.TempPrefix + "*");
        Check(File.ReadAllText(viaPath) == "новый" && leftovers.Length == 0,
            "atomic.WriteVia: сбой — оригинал цел, временный файл удалён");
    }

    // Сбои чтения/записи .ditastudio-* и подключённого .ditaval больше не проглатываются молча.
    private static void ProjectSettingsChecks(string root)
    {
        var projectRoot = Path.Combine(root, "settings-project");
        Directory.CreateDirectory(projectRoot);

        // Папка на месте файла настройки — запись гарантированно не удастся на любой ОС.
        Directory.CreateDirectory(Path.Combine(projectRoot, ".ditastudio-css"));
        var project = new DitaProject(projectRoot);
        var reported = new List<string>();
        project.SettingsWarning += reported.Add;
        project.SetCustomCssPath("custom.css");
        Check(reported.Count == 1 && reported[0].Contains(".ditastudio-css"),
            "настройки: сбой записи .ditastudio-css — событие SettingsWarning с именем файла");
        Check(project.CustomCssPath == "custom.css" && project.SettingsWarnings.Count == 1,
            "настройки: при сбое записи значение действует в сеансе, сбой есть в SettingsWarnings");

        // Файл настройки занят другой программой — прочитать не удаётся.
        var conditions = Path.Combine(projectRoot, ".ditastudio-conditions");
        File.WriteAllText(conditions, "1\naudience=admin\n");
        using (new FileStream(conditions, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = new DitaProject(projectRoot);
            Check(locked.SettingsWarnings.Any(w => w.Contains(".ditastudio-conditions")),
                "настройки: сбой чтения .ditastudio-conditions — предупреждение, значения по умолчанию");
            Check(locked.ExcludedConditionValues.Count == 0 && !locked.ShowDraftComments,
                "настройки: при сбое чтения условия не применены частично");
        }

        var readable = new DitaProject(projectRoot);
        Check(readable.ShowDraftComments && readable.ExcludedConditionValues.ContainsKey("audience"),
            "настройки: без блокировки условия читаются как обычно");

        readable.SetDitavalPath("missing.ditaval");
        Check(readable.ResolveLinkedDitaval(out var missingError) is null && missingError?.Contains("не найден") == true,
            "ditaval: подключённый файл пропал — причина сообщается, а не молчаливый null");

        File.WriteAllText(Path.Combine(projectRoot, "broken.ditaval"), "<val><prop");
        readable.SetDitavalPath("broken.ditaval");
        Check(readable.ResolveLinkedDitaval(out var brokenError) is null && brokenError?.Contains("не прочитан") == true,
            "ditaval: битый XML — причина сообщается, сборка не пройдёт молча без условий");
    }

    private static void DiskStampChecks(string root)
    {
        var path = Path.Combine(root, "stamp.dita");
        File.WriteAllText(path, "<topic id=\"t\"><title>Исходный</title></topic>");

        var doc = DitaDocument.Load(path);
        Check(doc.DiskStamp is not null, "stamp: Load запоминает отпечаток файла");
        Check(!doc.HasChangedOnDisk(), "stamp: сразу после чтения файл не считается изменённым");

        doc.Root.FirstElement("title")!.SetText("Мой");
        doc.IsDirty = true;
        doc.Save();
        Check(!doc.HasChangedOnDisk(), "stamp: собственное сохранение не считается чужим изменением");

        File.WriteAllText(path, "<topic id=\"t\"><title>Чужой, длиннее прежнего</title></topic>");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        Check(doc.HasChangedOnDisk(), "stamp: запись другой программой обнаруживается");

        var sameInstance = doc.Root;
        doc.IsDirty = true;
        doc.Reload();
        Check(doc.Title == "Чужой, длиннее прежнего", "reload: перечитывает содержимое с диска");
        Check(!doc.IsDirty, "reload: документ после перечитывания чистый");
        Check(!doc.HasChangedOnDisk(), "reload: отпечаток обновлён");
        Check(!ReferenceEquals(sameInstance, doc.Root), "reload: корень заменён новым деревом");

        File.Delete(path);
        Check(doc.HasChangedOnDisk(), "stamp: удалённый файл тоже считается изменённым");

        var unsaved = new DitaDocument(DitaNode.Element("topic"));
        Check(!unsaved.HasChangedOnDisk(), "stamp: документ без файла не изменён");
    }

    private static void RecoveryStoreChecks(string root)
    {
        var recoveryRoot = Path.Combine(root, "recovery");
        var projectA = Path.Combine(root, "projA");
        var projectB = Path.Combine(root, "projB");
        Directory.CreateDirectory(projectA);

        var storeA = RecoveryStore.ForProject(recoveryRoot, projectA);
        var storeA2 = RecoveryStore.ForProject(recoveryRoot, projectA + Path.DirectorySeparatorChar);
        var storeB = RecoveryStore.ForProject(recoveryRoot, projectB);
        Check(storeA.Directory == storeA2.Directory, "recovery: папка проекта не зависит от завершающего слеша");
        Check(storeA.Directory != storeB.Directory, "recovery: у разных проектов разные папки");
        Check(storeA.List().Count == 0, "recovery: пустое хранилище — пустой список (папки ещё нет)");

        var original = Path.Combine(projectA, "topic.dita");
        File.WriteAllText(original, "<topic/>");
        var stamp = FileStamp.Of(original);

        storeA.Save(original, "<topic id=\"x\">правка</topic>", stamp);
        storeA.Save(original.ToUpperInvariant(), "<topic id=\"x\">правка 2</topic>", stamp);
        var entries = storeA.List();
        Check(entries.Count == 1, $"recovery: повторная копия того же файла (в другом регистре) заменяет прежнюю ({entries.Count})");
        Check(entries.Count == 1 && entries[0].Content.Contains("правка 2"), "recovery: хранится последняя версия текста");
        Check(entries.Count == 1 && entries[0].OriginalStamp == stamp, "recovery: отпечаток оригинала сохраняется и читается обратно");
        Check(entries.Count == 1 && !entries[0].OriginalChangedSince, "recovery: оригинал не менялся после копии");
        var raw = File.ReadAllText(Directory.GetFiles(storeA.Directory, "*.recovery.json").Single());
        Check(raw.Contains("<topic id=\\\"x\\\">правка 2</topic>"), "recovery: копия читаема глазами — кириллица и теги не экранированы");

        File.WriteAllText(original, "<topic id=\"changed-elsewhere\"/>");
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(5));
        Check(storeA.List()[0].OriginalChangedSince, "recovery: замечает, что оригинал поменялся после копии");

        storeA.Save(Path.Combine(projectA, "other.dita"), "<topic/>", null);
        File.WriteAllText(Path.Combine(storeA.Directory, "broken" + ".recovery.json"), "{не json");
        Check(storeA.List().Count == 2, "recovery: повреждённая копия пропускается, остальные читаются");

        storeA.Remove(original);
        Check(storeA.List().All(e => !e.OriginalPath.EndsWith("topic.dita", StringComparison.OrdinalIgnoreCase)),
            "recovery: Remove удаляет копию документа");
        storeA.Remove(original);
        Check(true, "recovery: повторный Remove не падает");

        storeA.Clear();
        Check(storeA.List().Count == 0, "recovery: Clear удаляет все копии проекта");
    }
}
