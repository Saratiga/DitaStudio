using System.Text.RegularExpressions;
using DitaStudio.Core.Model;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Project;

public sealed class ProjectFile
{
    public ProjectFile(string fullPath, string relativePath, DitaDocumentKind kind, string title)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
        Title = title;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public DitaDocumentKind Kind { get; internal set; }

    public string Title { get; internal set; }

    public string FileName => System.IO.Path.GetFileName(FullPath);

    public string RootElement { get; internal set; } = string.Empty;

    public override string ToString() => RelativePath;
}

/// <summary>Определение ключа из карты (keydef или topicref с @keys).</summary>
public sealed class KeyDefinition
{
    public KeyDefinition(string key, string? href, string? resolvedPath, DitaNode source, string sourceMap, string? scope, string? format)
    {
        Key = key;
        Href = href;
        ResolvedPath = resolvedPath;
        Source = source;
        SourceMap = sourceMap;
        Scope = scope;
        Format = format;
    }

    public string Key { get; }

    public string? Href { get; }

    public string? ResolvedPath { get; }

    public DitaNode Source { get; }

    public string SourceMap { get; }

    public string? Scope { get; }

    public string? Format { get; }

    /// <summary>Текст ключа из topicmeta/keywords/keyword — подставляется вместо keyref.</summary>
    public string? KeyText
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            var keywords = meta?.FirstElement("keywords");
            var keyword = keywords?.FirstElement("keyword");
            var text = keyword?.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            var navtitle = meta?.FirstElement("navtitle")?.InnerText.Trim();
            return string.IsNullOrEmpty(navtitle) ? null : navtitle;
        }
    }

    public DitaNode? KeyContent
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            return meta?.FirstElement("keywords")?.FirstElement("keyword");
        }
    }

    public override string ToString() => $"{Key} -> {Href}";
}

/// <summary>
/// Проект документации: папка с топиками и картами. Отвечает за обход файлов,
/// кэш разобранных документов, пространство ключей и поиск.
/// </summary>
public sealed class DitaProject
{
    private static readonly string[] DitaExtensions = { ".dita", ".ditamap", ".bookmap", ".xml", ".ditaval" };

    private readonly Dictionary<string, DitaDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectFile> _files = new();
    private readonly KeySpace _rootKeySpace = new();
    private readonly List<string> _referencedProjectPaths = new();
    private readonly List<DitaProject> _referencedProjects = new();
    private readonly bool _allowReferencedProjects;

    private const string CustomCssSettingsFile = ".ditastudio-css";
    private const string ConditionsSettingsFile = ".ditastudio-conditions";
    private const string PdfHeaderFooterSettingsFile = ".ditastudio-pdf-header";
    private const string DitavalSettingsFile = ".ditastudio-ditaval";
    private const string ReferencedProjectsSettingsFile = ".ditastudio-references";
    private const string ExternalDtdSettingsFile = ".ditastudio-external-dtd";

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
        LoadCustomCssSetting();
        LoadConditionsSetting();
        LoadPdfHeaderFooterSetting();
        LoadDitavalSetting();
        LoadExternalDtdSetting();
        if (_allowReferencedProjects)
        {
            LoadReferencedProjectsSetting();
        }
    }

    public string RootPath { get; }

    public string Name { get; }

    public IReadOnlyList<ProjectFile> Files => _files;

    /// <summary>Ключи корневой области (не внутри keyscope) — то, что видно из панели ключей.</summary>
    public IReadOnlyDictionary<string, KeyDefinition> Keys => _rootKeySpace.Keys;

    public IEnumerable<ProjectFile> Maps => _files.Where(f => f.Kind == DitaDocumentKind.Map);

    public IEnumerable<ProjectFile> Topics => _files.Where(f => f.Kind == DitaDocumentKind.Topic);

    public event EventHandler? Reloaded;

    // ------------------------------------------------------ пользовательский CSS

    /// <summary>Путь (относительно RootPath, со слэшами вперёд) к подключённому файлу стилей публикации,
    /// или null, если не подключён. Сохраняется рядом с проектом, переживает перезапуск редактора.</summary>
    public string? CustomCssPath { get; private set; }

    public void SetCustomCssPath(string? relativePath)
    {
        CustomCssPath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath.Replace('\\', '/');
        var settingsPath = System.IO.Path.Combine(RootPath, CustomCssSettingsFile);
        try
        {
            if (CustomCssPath is null)
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            else
            {
                File.WriteAllText(settingsPath, CustomCssPath);
            }
        }
        catch
        {
            // настройка не критична — молча продолжаем без сохранения на диск
        }
    }

    private void LoadCustomCssSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, CustomCssSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var value = File.ReadAllText(settingsPath).Trim();
            CustomCssPath = value.Length == 0 ? null : value;
        }
        catch
        {
            CustomCssPath = null;
        }
    }

    // ------------------------------------------------------ условия сборки

    /// <summary>Значения атрибутов условной публикации (props/platform/product/audience/otherprops/
    /// deliveryTarget), исключаемые при сборке. Сохраняется рядом с проектом, переживает перезапуск редактора.</summary>
    public IReadOnlyDictionary<string, HashSet<string>> ExcludedConditionValues { get; private set; } =
        new Dictionary<string, HashSet<string>>();

    public bool ShowDraftComments { get; private set; }

    /// <summary>Объединяет правила исключения (например, импортированные из .ditaval) с уже
    /// имеющимися — по каждому атрибуту объединяет множества значений. Возвращает, сколько
    /// новых значений реально добавилось (для сообщения пользователю).</summary>
    public static int MergeExcludeConditions(
        Dictionary<string, HashSet<string>> target, IReadOnlyDictionary<string, HashSet<string>> additional)
    {
        var addedCount = 0;
        foreach (var (attribute, values) in additional)
        {
            if (!target.TryGetValue(attribute, out var set))
            {
                set = new HashSet<string>();
                target[attribute] = set;
            }

            foreach (var value in values)
            {
                if (set.Add(value))
                {
                    addedCount++;
                }
            }
        }

        return addedCount;
    }

    public void SetConditions(Dictionary<string, HashSet<string>> exclude, bool showDraftComments)
    {
        ExcludedConditionValues = exclude;
        ShowDraftComments = showDraftComments;

        var settingsPath = System.IO.Path.Combine(RootPath, ConditionsSettingsFile);
        try
        {
            if (exclude.Sum(kv => kv.Value.Count) == 0 && !showDraftComments)
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }

                return;
            }

            var lines = new List<string> { showDraftComments ? "1" : "0" };
            foreach (var (attribute, values) in exclude)
            {
                foreach (var value in values)
                {
                    lines.Add($"{attribute}={value}");
                }
            }

            File.WriteAllLines(settingsPath, lines);
        }
        catch
        {
            // условия сборки не критичны — молча продолжаем без сохранения на диск
        }
    }

    private void LoadConditionsSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, ConditionsSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var lines = File.ReadAllLines(settingsPath);
            if (lines.Length == 0)
            {
                return;
            }

            ShowDraftComments = lines[0].Trim() == "1";

            var exclude = new Dictionary<string, HashSet<string>>();
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var attribute = line[..separator];
                var value = line[(separator + 1)..];
                if (!exclude.TryGetValue(attribute, out var values))
                {
                    values = new HashSet<string>();
                    exclude[attribute] = values;
                }

                values.Add(value);
            }

            ExcludedConditionValues = exclude;
        }
        catch
        {
            ExcludedConditionValues = new Dictionary<string, HashSet<string>>();
            ShowDraftComments = false;
        }
    }

    // ------------------------------------------------------ связанный .ditaval

    /// <summary>Путь (относительно RootPath, со слэшами вперёд) к связанному .ditaval-файлу,
    /// или null, если не подключён. Сохраняется рядом с проектом, переживает перезапуск редактора.</summary>
    public string? DitavalPath { get; private set; }

    public void SetDitavalPath(string? relativePath)
    {
        DitavalPath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath.Replace('\\', '/');
        var settingsPath = System.IO.Path.Combine(RootPath, DitavalSettingsFile);
        try
        {
            if (DitavalPath is null)
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            else
            {
                File.WriteAllText(settingsPath, DitavalPath);
            }
        }
        catch
        {
            // настройка не критична — молча продолжаем без сохранения на диск
        }
    }

    private void LoadDitavalSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, DitavalSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var value = File.ReadAllText(settingsPath).Trim();
            DitavalPath = value.Length == 0 ? null : value;
        }
        catch
        {
            DitavalPath = null;
        }
    }

    /// <summary>Перечитывает связанный .ditaval с диска — правки файла подхватываются сами,
    /// вручную переимпортировать не нужно. Null, если файл не подключён, отсутствует или
    /// не читается.</summary>
    public DitavalRules? ResolveLinkedDitaval()
    {
        if (DitavalPath is null)
        {
            return null;
        }

        var fullPath = System.IO.Path.Combine(RootPath, DitavalPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return DitavalReader.Read(fullPath);
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------- внешний DTD

    /// <summary>Путь (относительно RootPath, со слэшами вперёд) к подключённому внешнему .dtd —
    /// для проектов с кастомной специализацией DITA, которую нет смысла вписывать во встроенный
    /// каталог. Сохраняется вместе с проектом, переживает перезапуск редактора.</summary>
    public string? ExternalDtdPath { get; private set; }

    public void SetExternalDtdPath(string? relativePath)
    {
        ExternalDtdPath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath.Replace('\\', '/');
        var settingsPath = System.IO.Path.Combine(RootPath, ExternalDtdSettingsFile);
        try
        {
            if (ExternalDtdPath is null)
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            else
            {
                File.WriteAllText(settingsPath, ExternalDtdPath);
            }
        }
        catch
        {
            // настройка не критична — молча продолжаем без сохранения на диск
        }
    }

    private void LoadExternalDtdSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, ExternalDtdSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var value = File.ReadAllText(settingsPath).Trim();
            ExternalDtdPath = value.Length == 0 ? null : value;
        }
        catch
        {
            ExternalDtdPath = null;
        }
    }

    /// <summary>Разбирает связанный .dtd заново с диска (см. DtdCatalogLoader) — правки файлов
    /// подхватываются сами. Null, если внешний DTD не подключён. Результат нужно самостоятельно
    /// влить в каталог через DitaCatalog.Default.Merge(...) — сам метод глобальный каталог не
    /// трогает.</summary>
    public DtdLoadResult? ResolveExternalDtd()
    {
        if (ExternalDtdPath is null)
        {
            return null;
        }

        var fullPath = System.IO.Path.Combine(RootPath, ExternalDtdPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return DtdCatalogLoader.Load(fullPath);
    }

    // ------------------------------------------------- проекты-источники ключей

    /// <summary>Пути к другим проектам, чьи ключи корневой области видны из этого проекта —
    /// мультипроектный workspace без полного объединения деревьев файлов: свои карты и топики
    /// публикуются как обычно, но keyref/conref на ключ, не найденный в своём проекте, ищется
    /// здесь. Сохраняется вместе с проектом, переживает перезапуск редактора.</summary>
    public IReadOnlyList<string> ReferencedProjectPaths => _referencedProjectPaths;

    public void AddReferencedProject(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (_referencedProjectPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _referencedProjectPaths.Add(full);
        SaveReferencedProjectsSetting();
        RebuildReferencedProjects();
    }

    public void RemoveReferencedProject(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (_referencedProjectPaths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        SaveReferencedProjectsSetting();
        RebuildReferencedProjects();
    }

    private void SaveReferencedProjectsSetting()
    {
        var settingsPath = System.IO.Path.Combine(RootPath, ReferencedProjectsSettingsFile);
        try
        {
            if (_referencedProjectPaths.Count == 0)
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }

                return;
            }

            File.WriteAllLines(settingsPath, _referencedProjectPaths);
        }
        catch
        {
            // настройка не критична — молча продолжаем без сохранения на диск
        }
    }

    private void LoadReferencedProjectsSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, ReferencedProjectsSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            _referencedProjectPaths.AddRange(File.ReadAllLines(settingsPath).Where(l => !string.IsNullOrWhiteSpace(l)));
        }
        catch
        {
            _referencedProjectPaths.Clear();
        }
    }

    /// <summary>Пересканирует все подключённые проекты-источники — вызывается при каждом Scan(),
    /// правки в них подхватываются сами, как и связанный .ditaval.</summary>
    private void RebuildReferencedProjects()
    {
        _referencedProjects.Clear();
        if (!_allowReferencedProjects)
        {
            return;
        }

        foreach (var path in _referencedProjectPaths)
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            try
            {
                var referenced = new DitaProject(path, allowReferencedProjects: false);
                referenced.Scan();
                _referencedProjects.Add(referenced);
            }
            catch
            {
                // недоступный проект-источник пропускаем — не мешаем работе с основным
            }
        }
    }

    private KeyDefinition? ResolveKeyInReferencedProjects(string key)
    {
        foreach (var referenced in _referencedProjects)
        {
            if (referenced.Keys.TryGetValue(key, out var def))
            {
                return def;
            }
        }

        return null;
    }

    // -------------------------------------------------------- колонтитулы PDF

    /// <summary>Показывать ли колонтитулы при экспорте в PDF. По умолчанию — нет (как и раньше).
    /// Сохраняется вместе с проектом, переживает перезапуск редактора.</summary>
    public bool PdfShowHeaderFooter { get; private set; }

    /// <summary>Текст в шапке страницы (поддерживается только при печати через WebView2).</summary>
    public string? PdfHeaderText { get; private set; }

    /// <summary>Текст в подвале страницы (поддерживается только при печати через WebView2).</summary>
    public string? PdfFooterText { get; private set; }

    public void SetPdfHeaderFooter(bool show, string? headerText, string? footerText)
    {
        PdfShowHeaderFooter = show;
        PdfHeaderText = headerText;
        PdfFooterText = footerText;

        var settingsPath = System.IO.Path.Combine(RootPath, PdfHeaderFooterSettingsFile);
        try
        {
            if (!show && string.IsNullOrEmpty(headerText) && string.IsNullOrEmpty(footerText))
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }

                return;
            }

            File.WriteAllLines(settingsPath, new[]
            {
                show ? "1" : "0",
                headerText ?? string.Empty,
                footerText ?? string.Empty
            });
        }
        catch
        {
            // настройка колонтитулов не критична — молча продолжаем без сохранения на диск
        }
    }

    private void LoadPdfHeaderFooterSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(RootPath, PdfHeaderFooterSettingsFile);
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var lines = File.ReadAllLines(settingsPath);
            PdfShowHeaderFooter = lines.Length > 0 && lines[0].Trim() == "1";
            PdfHeaderText = lines.Length > 1 ? lines[1] : string.Empty;
            PdfFooterText = lines.Length > 2 ? lines[2] : string.Empty;
        }
        catch
        {
            PdfShowHeaderFooter = false;
            PdfHeaderText = null;
            PdfFooterText = null;
        }
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
                // Файл с ошибкой разбора всё равно показываем в дереве проекта.
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
            catch
            {
                continue;
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
        catch
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

    // ---------------------------------------------------------------- ключи

    /// <summary>Область видимости ключей (keyscope): свои ключи плюс именованные дочерние
    /// области. Несколько имён keyscope на одном узле — алиасы одной и той же области.</summary>
    private sealed class KeySpace
    {
        public Dictionary<string, KeyDefinition> Keys { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, KeySpace> Scopes { get; } = new(StringComparer.Ordinal);
    }

    public void RebuildKeySpace()
    {
        _rootKeySpace.Keys.Clear();
        _rootKeySpace.Scopes.Clear();
        foreach (var map in Maps.ToList())
        {
            var doc = TryGetDocument(map.FullPath);
            if (doc is null)
            {
                continue;
            }

            CollectKeys(doc, doc.Root, map.FullPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), _rootKeySpace);
        }
    }

    private void CollectKeys(DitaDocument mapDoc, DitaNode node, string mapPath, HashSet<string> visited, KeySpace space)
    {
        foreach (var child in node.ElementChildren())
        {
            var childSpace = ResolveChildKeySpace(child, space);
            RegisterKeys(child, mapPath, childSpace);
            CollectKeysFromNestedMap(child, mapPath, visited, childSpace);
            CollectKeys(mapDoc, child, mapPath, visited, childSpace);
        }
    }

    /// <summary>Если у узла задан keyscope — заводит для него новую область и регистрирует её
    /// под всеми именами-алиасами в родительской; иначе ключи узла идут в ту же область.</summary>
    private static KeySpace ResolveChildKeySpace(DitaNode child, KeySpace space)
    {
        var keyscope = child.GetAttribute("keyscope");
        if (string.IsNullOrWhiteSpace(keyscope))
        {
            return space;
        }

        var childSpace = new KeySpace();
        foreach (var name in keyscope!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            space.Scopes[name] = childSpace;
        }

        return childSpace;
    }

    private static void RegisterKeys(DitaNode child, string mapPath, KeySpace childSpace)
    {
        var keys = child.GetAttribute("keys");
        if (string.IsNullOrWhiteSpace(keys))
        {
            return;
        }

        var href = child.GetAttribute("href");
        var resolved = href is null ? null : RefResolver.ResolvePath(mapPath, href);
        foreach (var key in keys!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!childSpace.Keys.ContainsKey(key))
            {
                childSpace.Keys[key] = new KeyDefinition(
                    key, href, resolved, child, mapPath,
                    child.GetAttribute("scope"), child.GetAttribute("format"));
            }
        }
    }

    /// <summary>Вложенные карты добавляют свои ключи в ту же область (свою — если у узла задан
    /// keyscope).</summary>
    private void CollectKeysFromNestedMap(DitaNode child, string mapPath, HashSet<string> visited, KeySpace childSpace)
    {
        if (child.Name != "mapref" && child.GetAttribute("format") != "ditamap")
        {
            return;
        }

        var href = child.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        var target = RefResolver.ResolvePath(mapPath, href!);
        if (target is null || !File.Exists(target) || !visited.Add(target))
        {
            return;
        }

        var sub = TryGetDocument(target);
        if (sub is not null)
        {
            CollectKeys(sub, sub.Root, target, visited, childSpace);
        }
    }

    /// <summary>Разрешает ключ в корневой области (без учёта keyscope) — как раньше.</summary>
    public KeyDefinition? ResolveKey(string key) => ResolveKey(key, null);

    /// <summary>Разрешает ключ с учётом области (keyscope), в которой находится ссылающийся
    /// топик (см. MapItem.KeyScopeChain) — так у веток с одинаковыми именами ключей могут быть
    /// разные значения. Ключ вида "область.ключ" ищется строго в указанной области (без подъёма
    /// наверх); ключ без точки ищется от ближайшей области цепочки к корневой — первое совпадение
    /// побеждает. Без цепочки (null) — как <see cref="ResolveKey(string)"/>, только в корне.</summary>
    public KeyDefinition? ResolveKey(string key, IReadOnlyList<string>? scopeChain)
    {
        if (key.Contains('.'))
        {
            var segments = key.Split('.');
            var space = _rootKeySpace;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!space.Scopes.TryGetValue(segments[i], out space!))
                {
                    return null;
                }
            }

            return space.Keys.TryGetValue(segments[^1], out var qualified) ? qualified : null;
        }

        if (scopeChain is null || scopeChain.Count == 0)
        {
            return _rootKeySpace.Keys.TryGetValue(key, out var rootDef) ? rootDef : ResolveKeyInReferencedProjects(key);
        }

        var chain = new List<KeySpace> { _rootKeySpace };
        var current = _rootKeySpace;
        foreach (var name in scopeChain)
        {
            if (!current.Scopes.TryGetValue(name, out current!))
            {
                break;
            }

            chain.Add(current);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Keys.TryGetValue(key, out var def))
            {
                return def;
            }
        }

        return ResolveKeyInReferencedProjects(key);
    }

    /// <summary>Сколько всего ключей в проекте, считая вложенные keyscope-области — в отличие от
    /// Keys (только корневая область). Разница между ними — повод показать в UI подсказку, что
    /// часть ключей объявлена внутри keyscope и не попадает в плоский список.</summary>
    public int TotalKeyCount => CountKeys(_rootKeySpace, new HashSet<KeySpace>());

    private static int CountKeys(KeySpace space, HashSet<KeySpace> visited)
    {
        var count = space.Keys.Count;
        foreach (var child in space.Scopes.Values)
        {
            if (visited.Add(child))
            {
                count += CountKeys(child, visited);
            }
        }

        return count;
    }

    /// <summary>Есть ли такой ключ хоть в какой-то области проекта (корневой или вложенной по
    /// keyscope)? В отличие от ResolveKey не требует знания конкретной цепочки областей — для
    /// проверки "ключ вообще существует" при валидации ссылок вне контекста карты, где топик
    /// может встречаться сразу в нескольких ветках с разными областями.</summary>
    public bool KeyExistsAnywhere(string key)
    {
        if (key.Contains('.'))
        {
            return ResolveKey(key) is not null;
        }

        return KeyExistsInSpace(_rootKeySpace, key) || ResolveKeyInReferencedProjects(key) is not null;
    }

    private static bool KeyExistsInSpace(KeySpace space, string key)
    {
        if (space.Keys.ContainsKey(key))
        {
            return true;
        }

        foreach (var child in space.Scopes.Values)
        {
            if (KeyExistsInSpace(child, key))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- поиск

    public sealed record SearchHit(ProjectFile File, DitaNode Node, string Context);

    public sealed record ReplaceResult(int ReplacementCount, IReadOnlyList<ProjectFile> ChangedFiles);

    private static Regex? TryBuildRegex(string pattern, bool caseSensitive)
    {
        try
        {
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public IReadOnlyList<SearchHit> Search(string query, bool caseSensitive = false, bool elementNames = false,
        bool regex = false)
    {
        var result = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? pattern = null;
        if (regex)
        {
            pattern = TryBuildRegex(query, caseSensitive);
            if (pattern is null)
            {
                return result;
            }
        }

        foreach (var file in _files)
        {
            var doc = TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                var hit = elementNames
                    ? MatchElementName(node, file, pattern, query, comparison)
                    : MatchText(node, file, pattern, query, comparison);

                if (hit is not null)
                {
                    result.Add(hit);
                }
            }
        }

        return result;
    }

    private static SearchHit? MatchElementName(
        DitaNode node, ProjectFile file, Regex? pattern, string query, StringComparison comparison)
    {
        if (node.Kind != NodeKind.Element)
        {
            return null;
        }

        var isMatch = pattern?.IsMatch(node.Name) ?? node.Name.Equals(query, comparison);
        return isMatch ? new SearchHit(file, node, node.Path) : null;
    }

    private static SearchHit? MatchText(
        DitaNode node, ProjectFile file, Regex? pattern, string query, StringComparison comparison)
    {
        if (node.Kind != NodeKind.Text)
        {
            return null;
        }

        int index;
        int matchLength;
        if (pattern is not null)
        {
            var match = pattern.Match(node.Value);
            if (!match.Success)
            {
                return null;
            }

            index = match.Index;
            matchLength = match.Length;
        }
        else
        {
            index = node.Value.IndexOf(query, comparison);
            if (index < 0)
            {
                return null;
            }

            matchLength = query.Length;
        }

        var start = Math.Max(0, index - 30);
        var length = Math.Min(node.Value.Length - start, matchLength + 60);
        var context = node.Value.Substring(start, length).Replace('\n', ' ').Trim();
        return new SearchHit(file, node.Parent ?? node, context);
    }

    /// <summary>Заменяет все вхождения запроса во всех текстовых узлах проекта. Изменённые
    /// документы помечаются как несохранённые (IsDirty) — запись на диск делает пользователь
    /// (Ctrl+Shift+S), как и после любой другой структурной правки.</summary>
    public ReplaceResult ReplaceAll(string query, string replacement, bool caseSensitive = false, bool regex = false)
    {
        var changed = new List<ProjectFile>();
        var count = 0;
        if (string.IsNullOrEmpty(query))
        {
            return new ReplaceResult(0, changed);
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? pattern = null;
        if (regex)
        {
            pattern = TryBuildRegex(query, caseSensitive);
            if (pattern is null)
            {
                return new ReplaceResult(0, changed);
            }
        }

        foreach (var file in _files)
        {
            var doc = TryGetDocument(file.FullPath);
            if (doc is null)
            {
                continue;
            }

            var fileChanged = false;
            foreach (var node in doc.Root.DescendantsAndSelf())
            {
                if (node.Kind != NodeKind.Text)
                {
                    continue;
                }

                if (pattern is not null)
                {
                    var matches = pattern.Matches(node.Value);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    node.Value = pattern.Replace(node.Value, replacement);
                    count += matches.Count;
                }
                else
                {
                    var occurrences = CountOccurrences(node.Value, query, comparison);
                    if (occurrences == 0)
                    {
                        continue;
                    }

                    node.Value = node.Value.Replace(query, replacement, comparison);
                    count += occurrences;
                }

                fileChanged = true;
            }

            if (fileChanged)
            {
                doc.IsDirty = true;
                changed.Add(file);
            }
        }

        return new ReplaceResult(count, changed);
    }

    private static int CountOccurrences(string text, string query, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(query, index, comparison)) >= 0)
        {
            count++;
            index += query.Length;
        }

        return count;
    }

    // ------------------------------------------------------------- проверка

    public IReadOnlyList<ValidationIssue> ValidateAll()
    {
        var validator = new DitaValidator();
        var issues = new List<ValidationIssue>();

        foreach (var file in _files)
        {
            DitaDocument doc;
            try
            {
                doc = GetDocument(file.FullPath);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"Файл не разбирается как XML: {ex.Message}",
                    null,
                    file.FullPath));
                continue;
            }

            issues.AddRange(validator.Validate(doc));
            issues.AddRange(RefResolver.ValidateReferences(this, doc));
        }

        return issues;
    }
}
