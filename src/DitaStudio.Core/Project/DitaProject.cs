using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Project;

/// <summary>
/// Проект документации: папка с топиками и картами. Отвечает за обход файлов, кэш разобранных документов и каталог элементов.
/// Остальное вынесено: служебные настройки — <see cref="ProjectSettings"/> (<see cref="Settings"/>; одноимённые члены ниже
/// переадресуют туда), ключи и проекты-источники — DitaProject.Keys.cs, поиск и замена — DitaProject.Search.cs,
/// проверка — DitaProject.Validation.cs.
/// </summary>
public sealed partial class DitaProject
{
    private static readonly string[] DitaExtensions = { ".dita", ".ditamap", ".bookmap", ".xml", ".ditaval" };

    private readonly Dictionary<string, DitaDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectFile> _files = new();
    private readonly KeySpace _rootKeySpace = new();
    private readonly List<DitaProject> _referencedProjects = new();
    private readonly bool _allowReferencedProjects;

    public DitaProject(string rootPath) : this(rootPath, allowReferencedProjects: true)
    {
    }

    /// <summary>allowReferencedProjects=false — для проектов, подключённых как источник ключей к
    /// другому проекту: они дают свои ключи наружу, но сами дальше не цепляют чужие — без этого
    /// ограничения циклическая связь A→B→A привела бы к бесконечной рекурсии в Scan().</summary>
    private DitaProject(string rootPath, bool allowReferencedProjects)
    {
        RootPath = System.IO.Path.GetFullPath(rootPath);
        Name = new DirectoryInfo(RootPath).Name;
        _allowReferencedProjects = allowReferencedProjects;
        Settings = new ProjectSettings(RootPath, loadReferencedProjects: allowReferencedProjects);
    }

    public string RootPath { get; }

    public string Name { get; }

    /// <summary>Служебные настройки проекта (файлы <c>.ditastudio-*</c>).</summary>
    public ProjectSettings Settings { get; }

    public IReadOnlyList<ProjectFile> Files => _files;

    /// <summary>Ключи корневой области (не внутри keyscope) — то, что видно из панели ключей.</summary>
    public IReadOnlyDictionary<string, KeyDefinition> Keys => _rootKeySpace.Keys;

    public IEnumerable<ProjectFile> Maps => _files.Where(f => f.Kind == DitaDocumentKind.Map);

    public IEnumerable<ProjectFile> Topics => _files.Where(f => f.Kind == DitaDocumentKind.Topic);

    public event EventHandler? Reloaded;

    // ------------------------------------------------------------ настройки (переадресация в Settings)

    public IReadOnlyList<string> SettingsWarnings => Settings.Warnings;

    public event Action<string>? SettingsWarning
    {
        add => Settings.Warning += value;
        remove => Settings.Warning -= value;
    }

    public string? CustomCssPath => Settings.CustomCssPath;

    public void SetCustomCssPath(string? relativePath) => Settings.SetCustomCssPath(relativePath);

    public string? ReadCustomCss(out string? warning) => Settings.ReadCustomCss(out warning);

    public IReadOnlyDictionary<string, HashSet<string>> ExcludedConditionValues => Settings.ExcludedConditionValues;

    public bool ShowDraftComments => Settings.ShowDraftComments;

    public static int MergeExcludeConditions(
        Dictionary<string, HashSet<string>> target, IReadOnlyDictionary<string, HashSet<string>> additional) =>
        ProjectSettings.MergeExcludeConditions(target, additional);

    public void SetConditions(Dictionary<string, HashSet<string>> exclude, bool showDraftComments) =>
        Settings.SetConditions(exclude, showDraftComments);

    public string? DitavalPath => Settings.DitavalPath;

    public void SetDitavalPath(string? relativePath) => Settings.SetDitavalPath(relativePath);

    public DitavalRules? ResolveLinkedDitaval() => Settings.ResolveLinkedDitaval();

    public DitavalRules? ResolveLinkedDitaval(out string? error) => Settings.ResolveLinkedDitaval(out error);

    public string? ExternalDtdPath => Settings.ExternalDtdPath;

    public void SetExternalDtdPath(string? relativePath)
    {
        Settings.SetExternalDtdPath(relativePath);
        _catalog = null; // каталог со старым DTD больше не действителен
    }

    public IReadOnlyList<string> ReferencedProjectPaths => Settings.ReferencedProjectPaths;

    public IReadOnlyList<ProductInfo> Products => Settings.Products;

    public void SetProducts(IEnumerable<ProductInfo> products) => Settings.SetProducts(products);

    public IReadOnlyList<string> PinnedFiles => Settings.PinnedFiles;

    public void SetPinnedFiles(IEnumerable<string> relativePaths) => Settings.SetPinnedFiles(relativePaths);

    public bool PdfShowHeaderFooter => Settings.PdfShowHeaderFooter;

    public string? PdfHeaderText => Settings.PdfHeaderText;

    public string? PdfFooterText => Settings.PdfFooterText;

    public void SetPdfHeaderFooter(bool show, string? headerText, string? footerText) =>
        Settings.SetPdfHeaderFooter(show, headerText, footerText);

    public DocxLayout DocxLayout => Settings.DocxLayout;

    public string? DocxLayoutWarning => Settings.DocxLayoutWarning;

    public void SetDocxLayout(DocxLayout layout) => Settings.SetDocxLayout(layout);

    // ------------------------------------------------------------ каталог элементов

    /// <summary>
    /// Каталог элементов этого проекта: встроенный DITA 1.3 плюс элементы подключённого внешнего
    /// DTD. Строится при первом обращении; после смены DTD — заново (<see cref="LoadCatalog"/>).
    /// </summary>
    public DitaCatalog Catalog => _catalog ?? LoadCatalog().Catalog;

    private DitaCatalog? _catalog;

    /// <summary>Перечитывает внешний DTD с диска и заново строит <see cref="Catalog"/>.
    /// Dtd — результат разбора (предупреждения для пользователя) или null без DTD.</summary>
    public (DitaCatalog Catalog, DtdLoadResult? Dtd) LoadCatalog()
    {
        var dtd = ResolveExternalDtd();
        var catalog = dtd is null ? DitaCatalog.Builtin : DitaCatalog.Builtin.WithElements(dtd.Elements);
        _catalog = catalog;
        return (catalog, dtd);
    }

    /// <summary>Разбирает связанный .dtd заново с диска (см. DtdCatalogLoader) — правки файлов
    /// подхватываются сами. Null, если внешний DTD не подключён. Каталог проекта из него
    /// строит <see cref="LoadCatalog"/>.</summary>
    public DtdLoadResult? ResolveExternalDtd()
    {
        if (ExternalDtdPath is null)
        {
            return null;
        }

        var fullPath = System.IO.Path.Combine(RootPath, ExternalDtdPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return DtdCatalogLoader.Load(fullPath);
    }

    // ------------------------------------------------------------------ обход

    public void Scan()
    {
        _files.Clear();
        _cache.Clear();

        foreach (var path in EnumerateFiles(RootPath))
        {
            var relative = System.IO.Path.GetRelativePath(RootPath, path);
            var kind = DitaDocumentKind.Unknown;
            var title = System.IO.Path.GetFileNameWithoutExtension(path);
            var rootName = string.Empty;

            try
            {
                var doc = GetDocument(path);
                kind = doc.Kind;
                title = doc.Title;
                rootName = doc.Root.Name;
            }
            catch
            {
                // Файл с ошибкой разбора всё равно показываем в дереве проекта. Ловим всё: один
                // испорченный файл не должен мешать открыть проект, а саму ошибку покажет проверка.
            }

            _files.Add(new ProjectFile(path, relative, kind, title) { RootElement = rootName });
        }

        _files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        RebuildReferencedProjects();
        RebuildKeySpace();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            IEnumerable<string> files;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // недоступная папка (права, удалена во время обхода) — пропускаем
            }

            foreach (var sub in subDirs)
            {
                var name = System.IO.Path.GetFileName(sub);
                if (name.StartsWith(".", StringComparison.Ordinal) ||
                    name is "out" or "bin" or "obj" or "temp" or "node_modules")
                {
                    continue;
                }

                stack.Push(sub);
            }

            foreach (var file in files)
            {
                var ext = System.IO.Path.GetExtension(file);
                if (DitaExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    // ----------------------------------------------------------- документы

    public DitaDocument GetDocument(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (_cache.TryGetValue(full, out var cached))
        {
            return cached;
        }

        var doc = DitaDocument.Load(full);
        _cache[full] = doc;
        return doc;
    }

    public DitaDocument? TryGetDocument(string path)
    {
        try
        {
            return GetDocument(path);
        }
        catch // Try-семантика: вызывающий получает null и сам сообщает о недоступном файле
        {
            return null;
        }
    }

    public bool IsOpen(string path) => _cache.ContainsKey(System.IO.Path.GetFullPath(path));

    public void Register(DitaDocument document)
    {
        if (document.FilePath is null)
        {
            return;
        }

        _cache[System.IO.Path.GetFullPath(document.FilePath)] = document;
    }

    public void Invalidate(string path)
    {
        _cache.Remove(System.IO.Path.GetFullPath(path));
    }

    public IEnumerable<DitaDocument> OpenDocuments => _cache.Values;

    public ProjectFile? FindFile(string fullPath)
    {
        var normalized = System.IO.Path.GetFullPath(fullPath);
        return _files.FirstOrDefault(f =>
            string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void AddFile(string fullPath)
    {
        if (FindFile(fullPath) is not null)
        {
            return;
        }

        var relative = System.IO.Path.GetRelativePath(RootPath, fullPath);
        var doc = TryGetDocument(fullPath);
        _files.Add(new ProjectFile(
            fullPath,
            relative,
            doc?.Kind ?? DitaDocumentKind.Unknown,
            doc?.Title ?? System.IO.Path.GetFileNameWithoutExtension(fullPath))
        {
            RootElement = doc?.Root.Name ?? string.Empty
        });
        _files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Убирает файл из проекта (после удаления с диска); ключи перестраиваются.</summary>
    public void RemoveFile(string fullPath)
    {
        if (FindFile(fullPath) is { } file)
        {
            _files.Remove(file);
        }

        Invalidate(fullPath);
        RebuildKeySpace();
    }

    public void RefreshFileInfo(string fullPath)
    {
        var file = FindFile(fullPath);
        if (file is null)
        {
            AddFile(fullPath);
            return;
        }

        var doc = TryGetDocument(fullPath);
        if (doc is not null)
        {
            file.Kind = doc.Kind;
            file.Title = doc.Title;
            file.RootElement = doc.Root.Name;
        }
    }
}
