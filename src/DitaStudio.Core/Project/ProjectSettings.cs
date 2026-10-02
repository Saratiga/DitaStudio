using System.Text;
using DitaStudio.Core.IO;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Project;

/// <summary>
/// Настройки проекта, которые лежат рядом с ним в служебных файлах <c>.ditastudio-*</c>: пользовательский CSS, условия сборки,
/// связанный .ditaval, внешний DTD, проекты-источники ключей, продукты, закреплённые вкладки, колонтитулы PDF, вёрстка DOCX.
/// Читаются при создании, пишутся при каждой смене (через <see cref="AtomicFile"/>). Сбой чтения или записи не прерывает работу —
/// он попадает в <see cref="Warnings"/> и в событие <see cref="Warning"/>: настройка тогда берётся по умолчанию или действует
/// до закрытия проекта.
/// </summary>
public sealed class ProjectSettings
{
    private const string CustomCssFile = ".ditastudio-css";
    private const string ConditionsFile = ".ditastudio-conditions";
    private const string PdfHeaderFooterFile = ".ditastudio-pdf-header";
    private const string DitavalFile = ".ditastudio-ditaval";
    private const string ReferencedProjectsFile = ".ditastudio-references";
    private const string ExternalDtdFile = ".ditastudio-external-dtd";
    private const string DocxLayoutFile = ".ditastudio-docx";
    private const string PinnedTabsFile = ".ditastudio-pinned";
    private const string ProductsFile = ".ditastudio-products";

    private static string SaveFailedTail => Loc.T("Core_TheSettingAppliesUntilTheProject");
    private static string LoadFailedTail => Loc.T("Core_DefaultValuesAreUsed");

    private readonly string _root;
    private readonly List<string> _warnings = new();
    private readonly List<string> _referencedProjectPaths = new();

    /// <param name="rootPath">Папка проекта (полный путь).</param>
    /// <param name="loadReferencedProjects">false — для проектов, подключённых как источник ключей: они сами дальше чужие не цепляют.</param>
    public ProjectSettings(string rootPath, bool loadReferencedProjects = true)
    {
        _root = rootPath;
        LoadPathSetting(CustomCssFile, value => CustomCssPath = value);
        LoadConditions();
        LoadPdfHeaderFooter();
        LoadPathSetting(DitavalFile, value => DitavalPath = value);
        LoadPathSetting(ExternalDtdFile, value => ExternalDtdPath = value);
        LoadDocxLayout();
        LoadPinnedFiles();
        LoadProducts();
        if (loadReferencedProjects)
        {
            LoadReferencedProjects();
        }
    }

    // ------------------------------------------------------------ сбои служебных файлов

    /// <summary>Сбои чтения и записи служебных файлов проекта — чтобы пользователь знал, что, например, условия сборки не сохранились.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Срабатывает при каждом сбое из <see cref="Warnings"/> — чтобы сообщить сразу.</summary>
    public event Action<string>? Warning;

    internal void Report(string message)
    {
        _warnings.Add(message);
        Warning?.Invoke(message);
    }

    // ------------------------------------------------------------ пользовательский CSS

    /// <summary>Путь (относительно папки проекта, со слэшами вперёд) к подключённому файлу стилей публикации или null.</summary>
    public string? CustomCssPath { get; private set; }

    public void SetCustomCssPath(string? relativePath) => CustomCssPath = SavePathSetting(CustomCssFile, relativePath);

    /// <summary>Текст пользовательского CSS проекта или null, если он не подключён или не читается
    /// (тогда в warning — причина). Общий источник для HTML, PDF и DOCX.</summary>
    public string? ReadCustomCss(out string? warning)
    {
        warning = null;
        if (string.IsNullOrEmpty(CustomCssPath))
        {
            return null;
        }

        var path = FullPathOf(CustomCssPath);
        if (!File.Exists(path))
        {
            warning = Loc.T("Core_TheCustomStyleFileWasNot", path);
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = Loc.T("Core_CouldNotReadTheStyleFile", path, ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------ условия сборки

    /// <summary>Значения атрибутов условной публикации (props/platform/product/audience/otherprops/deliveryTarget), исключаемые при сборке.</summary>
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

        if (exclude.Sum(kv => kv.Value.Count) == 0 && !showDraftComments)
        {
            Save(ConditionsFile, SaveFailedTail, null);
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

        Save(ConditionsFile, SaveFailedTail, path => WriteLines(path, lines));
    }

    private void LoadConditions() => Load(ConditionsFile, LoadFailedTail,
        path =>
        {
            var lines = File.ReadAllLines(path);
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
        },
        () =>
        {
            ExcludedConditionValues = new Dictionary<string, HashSet<string>>();
            ShowDraftComments = false;
        });

    // ------------------------------------------------------------ связанный .ditaval

    /// <summary>Путь (относительно папки проекта, со слэшами вперёд) к связанному .ditaval-файлу или null.</summary>
    public string? DitavalPath { get; private set; }

    public void SetDitavalPath(string? relativePath) => DitavalPath = SavePathSetting(DitavalFile, relativePath);

    /// <summary>Перечитывает связанный .ditaval с диска — правки файла подхватываются сами,
    /// вручную переимпортировать не нужно. Null, если файл не подключён, отсутствует или
    /// не читается.</summary>
    public DitavalRules? ResolveLinkedDitaval() => ResolveLinkedDitaval(out _);

    /// <param name="error">Почему подключённый файл не прочитан (нет файла, битый XML) — чтобы
    /// сборка не прошла молча без условий, которые пользователь считает включёнными.</param>
    public DitavalRules? ResolveLinkedDitaval(out string? error)
    {
        error = null;
        if (DitavalPath is null)
        {
            return null;
        }

        var fullPath = FullPathOf(DitavalPath);
        if (!File.Exists(fullPath))
        {
            error = Loc.T("Core_TheConnectedConditionsFile0Was", DitavalPath);
            return null;
        }

        try
        {
            return DitavalReader.Read(fullPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            error = Loc.T("Core_TheConnectedConditionsFile0Could", DitavalPath, ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------ внешний DTD

    /// <summary>Путь (относительно папки проекта, со слэшами вперёд) к подключённому внешнему .dtd —
    /// для проектов с кастомной специализацией DITA. Каталог из него строит <see cref="DitaProject.Catalog"/>.</summary>
    public string? ExternalDtdPath { get; private set; }

    public void SetExternalDtdPath(string? relativePath) => ExternalDtdPath = SavePathSetting(ExternalDtdFile, relativePath);

    // ------------------------------------------------------------ проекты-источники ключей

    /// <summary>Пути к другим проектам, чьи ключи корневой области видны из этого проекта: keyref/conref на ключ,
    /// не найденный в своём проекте, ищется там.</summary>
    public IReadOnlyList<string> ReferencedProjectPaths => _referencedProjectPaths;

    /// <summary>Подключает проект-источник; false — он уже подключён.</summary>
    public bool AddReferencedProject(string path)
    {
        var full = Path.GetFullPath(path);
        if (_referencedProjectPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _referencedProjectPaths.Add(full);
        SaveReferencedProjects();
        return true;
    }

    /// <summary>Отключает проект-источник; false — он не был подключён.</summary>
    public bool RemoveReferencedProject(string path)
    {
        var full = Path.GetFullPath(path);
        if (_referencedProjectPaths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        SaveReferencedProjects();
        return true;
    }

    private void SaveReferencedProjects() => Save(ReferencedProjectsFile, SaveFailedTail,
        _referencedProjectPaths.Count == 0 ? null : path => WriteLines(path, _referencedProjectPaths));

    private void LoadReferencedProjects() => Load(ReferencedProjectsFile, LoadFailedTail,
        path => _referencedProjectPaths.AddRange(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))),
        _referencedProjectPaths.Clear);

    // ------------------------------------------------------------ продукты

    /// <summary>Список продуктов проекта (значения атрибута <c>product</c>).</summary>
    public IReadOnlyList<ProductInfo> Products { get; private set; } = Array.Empty<ProductInfo>();

    public void SetProducts(IEnumerable<ProductInfo> products)
    {
        Products = ProductList.Clean(products);
        var list = Products;
        Save(ProductsFile, Loc.T("Core_TheProductListAppliesUntilThe"),
            list.Count == 0 ? null : path => ProductList.Write(path, list));
    }

    private void LoadProducts() => Load(ProductsFile, Loc.T("Core_TheProductListIsEmpty"),
        path => Products = ProductList.Read(path),
        () => Products = Array.Empty<ProductInfo>());

    // ------------------------------------------------------------ закреплённые вкладки

    /// <summary>Файлы закреплённых вкладок (пути от папки проекта, через «/»): при открытии проекта они открываются сами и стоят слева.</summary>
    public IReadOnlyList<string> PinnedFiles { get; private set; } = Array.Empty<string>();

    public void SetPinnedFiles(IEnumerable<string> relativePaths)
    {
        var files = relativePaths.Select(p => p.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PinnedFiles = files;
        Save(PinnedTabsFile, Loc.T("Core_PinningAppliesUntilTheProjectIs"),
            files.Count == 0 ? null : path => WriteLines(path, files));
    }

    private void LoadPinnedFiles() => Load(PinnedTabsFile, Loc.T("Core_ThereAreNoPinnedTabs"),
        path => PinnedFiles = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
        () => PinnedFiles = Array.Empty<string>());

    // ------------------------------------------------------------ колонтитулы PDF

    /// <summary>Показывать ли колонтитулы при экспорте в PDF. По умолчанию — нет.</summary>
    public bool PdfShowHeaderFooter { get; private set; }

    /// <summary>Текст в шапке страницы.</summary>
    public string? PdfHeaderText { get; private set; }

    /// <summary>Текст в подвале страницы.</summary>
    public string? PdfFooterText { get; private set; }

    public void SetPdfHeaderFooter(bool show, string? headerText, string? footerText)
    {
        PdfShowHeaderFooter = show;
        PdfHeaderText = headerText;
        PdfFooterText = footerText;

        if (!show && string.IsNullOrEmpty(headerText) && string.IsNullOrEmpty(footerText))
        {
            Save(PdfHeaderFooterFile, SaveFailedTail, null);
            return;
        }

        Save(PdfHeaderFooterFile, SaveFailedTail,
            path => WriteLines(path, new[] { show ? "1" : "0", headerText ?? string.Empty, footerText ?? string.Empty }));
    }

    private void LoadPdfHeaderFooter() => Load(PdfHeaderFooterFile, LoadFailedTail,
        path =>
        {
            var lines = File.ReadAllLines(path);
            PdfShowHeaderFooter = lines.Length > 0 && lines[0].Trim() == "1";
            PdfHeaderText = lines.Length > 1 ? lines[1] : string.Empty;
            PdfFooterText = lines.Length > 2 ? lines[2] : string.Empty;
        },
        () =>
        {
            PdfShowHeaderFooter = false;
            PdfHeaderText = null;
            PdfFooterText = null;
        });

    // ------------------------------------------------------------ оформление DOCX

    /// <summary>Вёрстка DOCX, которую не выразить через CSS (титул, оглавление, нумерация заголовков, колонтитулы…). Всегда не null.</summary>
    public DocxLayout DocxLayout { get; private set; } = new();

    /// <summary>Предупреждение, если файл .ditastudio-docx есть, но не разобрался.</summary>
    public string? DocxLayoutWarning { get; private set; }

    public void SetDocxLayout(DocxLayout layout)
    {
        var copy = layout.Clone();
        DocxLayout = copy;
        DocxLayoutWarning = null;
        AtomicFile.WriteAllText(PathOf(DocxLayoutFile), copy.ToJson(), new UTF8Encoding(false));
    }

    private void LoadDocxLayout()
    {
        var path = PathOf(DocxLayoutFile);
        try
        {
            if (File.Exists(path))
            {
                DocxLayout = DocxLayout.FromJson(File.ReadAllText(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            DocxLayout = new DocxLayout();
            DocxLayoutWarning = Loc.T("Core_CouldNotRead01Default", DocxLayoutFile, ex.Message);
        }
    }

    // ------------------------------------------------------------ общее чтение и запись

    private string PathOf(string settingsFile) => Path.Combine(_root, settingsFile);

    private string FullPathOf(string relativePath) => Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void WriteLines(string path, IEnumerable<string> lines) =>
        AtomicFile.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));

    /// <summary>Пишет настройку; write == null — настройка пуста, файл удаляется. Сбой записи — в <see cref="Warnings"/>.</summary>
    private void Save(string settingsFile, string failedTail, Action<string>? write)
    {
        var path = PathOf(settingsFile);
        try
        {
            if (write is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            write(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(Loc.T("Core_CouldNotSave012", settingsFile, ex.Message, failedTail));
        }
    }

    /// <summary>Читает настройку, если файл есть; при сбое чтения сбрасывает её (<paramref name="reset"/>) и сообщает.</summary>
    private void Load(string settingsFile, string failedTail, Action<string> read, Action reset)
    {
        try
        {
            var path = PathOf(settingsFile);
            if (File.Exists(path))
            {
                read(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reset();
            Report(Loc.T("Core_CouldNotRead012", settingsFile, ex.Message, failedTail));
        }
    }

    // Настройка из одного пути (css, ditaval, внешний DTD): текст файла без пробелов по краям.
    private string? SavePathSetting(string settingsFile, string? relativePath)
    {
        var value = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath.Replace('\\', '/');
        Save(settingsFile, SaveFailedTail, value is null ? null : path => AtomicFile.WriteAllText(path, value, new UTF8Encoding(false)));
        return value;
    }

    private void LoadPathSetting(string settingsFile, Action<string?> assign) => Load(settingsFile, LoadFailedTail,
        path =>
        {
            var value = File.ReadAllText(path).Trim();
            assign(value.Length == 0 ? null : value);
        },
        () => assign(null));
}
