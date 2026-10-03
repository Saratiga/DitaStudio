using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Core.Localization;

/// <summary>Результат пакетного экспорта в XLIFF: сколько файлов записано и какие топики пропущены.</summary>
public sealed record XliffBatchExportResult(string Folder, int FileCount, IReadOnlyList<string> Warnings);

/// <summary>Результат пакетного импорта: изменённые документы, число применённых сегментов, строки отчёта по файлам и предупреждения.</summary>
public sealed record XliffBatchImportResult(
    IReadOnlyList<DitaDocument> ChangedDocuments, int SegmentCount, IReadOnlyList<string> Report, IReadOnlyList<string> Warnings);

/// <summary>
/// Пакетный перевод всей карты: каждому топику карты — свой XLIFF-файл в одной папке, плюс <c>manifest.json</c> (путь топика → файл).
/// Импорт читает манифест и подставляет перевод в каждый топик тем же способом, что и одиночный (<see cref="XliffConverter"/>).
/// </summary>
public static class XliffBatch
{
    public const string ManifestName = "manifest.json";

    private sealed record ManifestEntry(string Path, string File);

    private sealed record Manifest(int Version, string Map, string Source, string Target, List<ManifestEntry> Files);

    /// <summary>
    /// Выгружает все топики карты (с вложенными картами, без битых ссылок и повторов) в <paramref name="folder"/>.
    /// Исходный язык файла — <c>xml:lang</c> топика, нет его — <paramref name="sourceLanguage"/>.
    /// </summary>
    public static XliffBatchExportResult Export(DitaProject project, string mapPath, string folder, string sourceLanguage, string targetLanguage)
    {
        var warnings = new List<string>();
        var tree = MapTree.Build(project, mapPath);
        var topics = tree.PublicationOrder
            .Select(i => System.IO.Path.GetFullPath(i.TargetPath!))
            .Where(p => !string.Equals(System.IO.Path.GetExtension(p), ".ditamap", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(System.IO.Path.GetExtension(p), ".bookmap", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Directory.CreateDirectory(folder);
        var manifest = new Manifest(1, RelativeOf(project, mapPath), sourceLanguage, targetLanguage, new List<ManifestEntry>());
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics)
        {
            DitaDocument document;
            try
            {
                document = project.GetDocument(topic);
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                warnings.Add(Loc.T("Core_TheTopic0CouldNotBe", RelativeOf(project, topic), ex.Message));
                continue;
            }

            var relative = RelativeOf(project, topic);
            var fileName = UniqueName(relative, targetLanguage, used);
            var xliff = XliffConverter.Export(document, DocumentLanguage.Of(document) ?? sourceLanguage, targetLanguage);
            AtomicFile.Write(System.IO.Path.Combine(folder, fileName), stream => xliff.Save(stream));
            manifest.Files.Add(new ManifestEntry(relative, fileName));
        }

        AtomicFile.WriteAllText(System.IO.Path.Combine(folder, ManifestName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));
        return new XliffBatchExportResult(folder, manifest.Files.Count, warnings);
    }

    /// <summary>
    /// Подставляет переводы из папки, выгруженной <see cref="Export"/>. Документы, в которых что-то изменилось, помечаются несохранёнными
    /// и возвращаются в результате — сохранение решает вызывающий (как у рефакторинга).
    /// </summary>
    public static XliffBatchImportResult Import(DitaProject project, string folder)
    {
        var warnings = new List<string>();
        var report = new List<string>();
        var changed = new List<DitaDocument>();
        var segments = 0;

        var manifestPath = System.IO.Path.Combine(folder, ManifestName);
        Manifest? manifest = null;
        try
        {
            manifest = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add(Loc.T("Core_TheManifestCouldNotBeRead", ex.Message));
        }

        if (manifest?.Files is not { Count: > 0 })
        {
            warnings.Add(Loc.T("Core_ThereIsNoManifestJsonIn"));
            return new XliffBatchImportResult(changed, 0, report, warnings);
        }

        foreach (var entry in manifest.Files)
        {
            var topicPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.RootPath, entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            var xliffPath = System.IO.Path.Combine(folder, entry.File);
            if (!File.Exists(topicPath))
            {
                warnings.Add(Loc.T("Core_TheTopic0IsNotIn", entry.Path));
                continue;
            }

            if (!File.Exists(xliffPath))
            {
                warnings.Add(Loc.T("Core_TheXLIFFFile0Is", entry.File));
                continue;
            }

            XDocument xliff;
            DitaDocument document;
            try
            {
                xliff = XDocument.Load(xliffPath);
                document = project.GetDocument(topicPath);
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                warnings.Add(Loc.T("Core_TheTopic0CouldNotBe", entry.Path, ex.Message));
                continue;
            }

            var fileWarnings = new List<string>();
            var beforeXml = document.ToXmlString();
            var applied = XliffConverter.Import(document, xliff, fileWarnings);
            warnings.AddRange(fileWarnings.Select(w => $"{entry.Path}: {w}"));

            // Изменённым считается только документ, который после импорта реально отличается (перевод совпал с исходным — не трогаем).
            if (applied > 0 && document.ToXmlString() != beforeXml)
            {
                document.IsDirty = true;
                changed.Add(document);
            }

            segments += applied;
            report.Add(Loc.T("Core_0SegmentsApplied1", entry.Path, applied));
        }

        return new XliffBatchImportResult(changed, segments, report, warnings);
    }

    private static string RelativeOf(DitaProject project, string path) =>
        System.IO.Path.GetRelativePath(project.RootPath, path).Replace('\\', '/');

    /// <summary>Имя XLIFF-файла из пути топика: «tasks/install.dita» → «tasks__install.dita.en.xliff»; повторы получают номер.</summary>
    private static string UniqueName(string relativePath, string targetLanguage, HashSet<string> used)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var stem = string.Concat(relativePath.Replace("/", "__").Select(c => invalid.Contains(c) ? '_' : c));
        var language = string.Concat(targetLanguage.Select(c => invalid.Contains(c) ? '_' : c));
        var name = $"{stem}.{language}.xliff";
        for (var n = 2; !used.Add(name); n++)
        {
            name = $"{stem}.{language}.{n}.xliff";
        }

        return name;
    }
}
