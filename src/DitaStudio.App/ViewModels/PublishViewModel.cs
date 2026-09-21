using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App;
using DitaStudio.App.Plugins;
using DitaStudio.App.Views;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using Microsoft.Win32;

namespace DitaStudio.App.ViewModels;

// Публикация: сборка HTML-сайта/одного файла, экспорт в PDF (с колонтитулами
// через WebView2) и в DOCX, условия сборки (в т.ч. импорт .ditaval),
// пользовательский CSS.
public partial class PublishViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string? _lastOutputDirectory;

    [ObservableProperty]
    private string buildLogText = string.Empty;

    public PublishViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void PublishSite() => Publish(singleFile: false, exportPdf: false);

    [RelayCommand]
    private void PublishSingle() => Publish(singleFile: true, exportPdf: false);

    [RelayCommand]
    private void PublishPdf() => Publish(singleFile: true, exportPdf: true);

    [RelayCommand]
    private void ExportDocx()
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            Dialogs.Message("Экспорт в DOCX", "Выберите карту на вкладке «Карта».");
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт в DOCX",
            Filter = "Документ Word|*.docx",
            FileName = Path.GetFileNameWithoutExtension(map.FullPath) + ".docx",
            InitialDirectory = _lastOutputDirectory ?? project.RootPath
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var options = new PublishOptions
        {
            ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Сборка DOCX по карте {map.RelativePath}…\n";

        try
        {
            var publisher = new DocxPublisher(project);
            var result = publisher.Publish(map.FullPath, options, dialog.FileName);

            foreach (var warning in result.Warnings)
            {
                BuildLogText += "Предупреждение: " + warning + "\n";
            }

            BuildLogText += "Результат: " + result.OutputFile + "\n";
            _main.StatusText = "DOCX собран: " + result.OutputFile;
            OpenInShell(result.OutputFile);
        }
        catch (Exception ex)
        {
            BuildLogText += "Ошибка: " + ex.Message + "\n";
            Dialogs.Message("Экспорт в DOCX", ex.Message);
        }
    }

    /// <summary>Публикует картой через выбранный плагин формата (см. IPublishFormatPlugin) —
    /// пункт меню один и тот же для любого числа подключённых плагинов, выбор через диалог.</summary>
    [RelayCommand]
    private void PublishWithPlugin()
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            Dialogs.Message("Публикация плагином", "Выберите карту на вкладке «Карта».");
            return;
        }

        var format = Dialogs.PickOne("Публикация плагином", "Выберите формат публикации:",
            PluginRegistry.PublishFormats, f => f.Name);
        if (format is null)
        {
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var dialog = new SaveFileDialog
        {
            Title = $"Публикация: {format.Name}",
            Filter = $"{format.Name}|*.{format.FileExtension}",
            FileName = Path.GetFileNameWithoutExtension(map.FullPath) + "." + format.FileExtension,
            InitialDirectory = _lastOutputDirectory ?? project.RootPath
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var options = new PublishOptions { ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false, Language = "ru" };
        ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Публикация плагином «{format.Name}» по карте {map.RelativePath}…\n";

        try
        {
            format.Publish(project, map.FullPath, options, dialog.FileName);
            BuildLogText += "Результат: " + dialog.FileName + "\n";
            _main.StatusText = $"Опубликовано плагином «{format.Name}»: {dialog.FileName}";
            OpenInShell(dialog.FileName);
        }
        catch (Exception ex)
        {
            BuildLogText += "Ошибка: " + ex.Message + "\n";
            Dialogs.Message("Публикация плагином", $"Плагин «{format.Name}» упал: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PublishConditions()
    {
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        var result = Dialogs.PublishConditions(project, _main.Conditions);
        if (result is not null)
        {
            _main.Conditions = result;
            project.SetConditions(result.Exclude, result.ShowDraftComments);
            _main.StatusText = $"Условия сборки обновлены: исключено значений {_main.Conditions.Exclude.Sum(x => x.Value.Count)}.";
        }
    }

    /// <summary>Подключает .ditaval к проекту (путь сохраняется вместе с проектом) — при каждой
    /// сборке файл перечитывается заново, руками переимпортировать не нужно.</summary>
    [RelayCommand]
    private void ImportDitaval()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Условия сборки", "Сначала откройте папку проекта.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Подключить файл условий (.ditaval)",
            Filter = "Файлы DITAVAL|*.ditaval|Все файлы|*.*",
            InitialDirectory = project.DitavalPath is null ? project.RootPath : Path.GetDirectoryName(Path.Combine(project.RootPath, project.DitavalPath))
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(project.RootPath, dialog.FileName).Replace('\\', '/');

        DitavalRules? rules;
        try
        {
            rules = DitavalReader.Read(dialog.FileName);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Условия сборки", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        project.SetDitavalPath(relativePath);
        var excludedCount = rules.Exclude.Sum(x => x.Value.Count);
        _main.StatusText = $"Подключён .ditaval: {relativePath} (исключений: {excludedCount}, правил подсветки: {rules.Flags.Count}). Изменения файла подхватываются автоматически.";
    }

    /// <summary>Правит правила исключения СВЯЗАННОГО .ditaval прямо в приложении, не выходя в
    /// текстовый редактор — записывает обратно в тот же файл (правила подсветки сохраняются).</summary>
    [RelayCommand]
    private void EditDitaval()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Редактирование .ditaval", "Сначала откройте папку проекта.");
            return;
        }

        if (Dialogs.EditDitaval(project))
        {
            _main.StatusText = $"Файл .ditaval обновлён: {project.DitavalPath}.";
        }
    }

    /// <summary>Дополняет условия сборки исключениями и правилами подсветки из связанного .ditaval
    /// (см. <see cref="ImportDitaval"/>) поверх вручную заданных в диалоге условий.</summary>
    private void ApplyConditions(PublishOptions options)
    {
        if (_main.Conditions is not null)
        {
            foreach (var (attribute, values) in _main.Conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        var linked = _main.Project?.ResolveLinkedDitaval();
        if (linked is null)
        {
            return;
        }

        DitaProject.MergeExcludeConditions(options.ExcludeConditions, linked.Exclude);
        options.FlagConditions.AddRange(linked.Flags);
    }

    [RelayCommand]
    private void PdfHeaderFooter()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Колонтитулы PDF", "Сначала откройте папку проекта.");
            return;
        }

        var result = Dialogs.PdfHeaderFooter(project);
        if (result is null)
        {
            return;
        }

        project.SetPdfHeaderFooter(result.Show, result.HeaderText, result.FooterText);
        _main.StatusText = result.Show ? "Колонтитулы PDF включены." : "Колонтитулы PDF отключены.";
    }

    [RelayCommand]
    private void CustomCss()
    {
        var project = _main.Project;
        if (project is null)
        {
            Dialogs.Message("Пользовательский CSS", "Сначала откройте папку проекта.");
            return;
        }

        Dialogs.CustomCss(project);
        _main.StatusText = project.CustomCssPath is null
            ? "Пользовательский CSS отключён."
            : $"Пользовательский CSS: {project.CustomCssPath}";
    }

    /// <summary>Экспортирует открытый документ в XLIFF 1.2 для перевода — сегмент на каждый
    /// блочный элемент с прямым текстом, фразовая разметка внутри как bpt/ept/ph (см. XliffConverter).</summary>
    [RelayCommand]
    private void ExportXliff()
    {
        var pane = _main.Current;
        if (pane is null)
        {
            Dialogs.Message("Экспорт в XLIFF", "Откройте документ.");
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            Dialogs.Message("Экспорт в XLIFF", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт в XLIFF",
            Filter = "Файлы XLIFF|*.xliff|Все файлы|*.*",
            FileName = Path.GetFileNameWithoutExtension(pane.FilePath ?? "document") + ".xliff"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var xliff = XliffConverter.Export(pane.Document, "ru", "en");
        xliff.Save(dialog.FileName);
        _main.StatusText = $"Экспортировано в XLIFF: {dialog.FileName}";
    }

    /// <summary>Подставляет перевод из &lt;target&gt; каждого сегмента XLIFF обратно в открытый
    /// документ — по номеру сегмента, тем же обходом дерева, что и при экспорте (см. XliffConverter).
    /// Требует, чтобы структура документа не менялась с момента экспорта.</summary>
    [RelayCommand]
    private void ImportXliff()
    {
        var pane = _main.Current;
        if (pane is null)
        {
            Dialogs.Message("Импорт из XLIFF", "Откройте документ, в который нужно подставить перевод.");
            return;
        }

        var dialog = new OpenFileDialog { Title = "Импортировать перевод из XLIFF", Filter = "Файлы XLIFF|*.xliff|Все файлы|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        XDocument xliff;
        try
        {
            xliff = XDocument.Load(dialog.FileName);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Импорт из XLIFF", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            Dialogs.Message("Импорт из XLIFF", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var warnings = new List<string>();
        var applied = XliffConverter.Import(pane.Document, xliff, warnings);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _main.Documents.RefreshAllTabTitles();

        _main.StatusText = $"Перевод импортирован: применено сегментов {applied}" +
                            (warnings.Count > 0 ? $", предупреждений {warnings.Count}." : ".");
        if (warnings.Count > 0)
        {
            Dialogs.Message("Импорт из XLIFF — предупреждения", string.Join("\n", warnings));
        }
    }

    /// <summary>Сбрасывает несохранённые правки во всех открытых вкладках на диск и пересобирает
    /// пространство ключей — общий первый шаг перед любой публикацией (HTML/PDF/DOCX).</summary>
    private void SaveAllPanesAndRebuildKeySpace()
    {
        foreach (var pane in _main.Panes.Values)
        {
            pane.CommitPendingEdits();
            if (pane.IsDirty)
            {
                pane.Save(out _);
            }
        }

        _main.Project!.RebuildKeySpace();
    }

    private async void Publish(bool singleFile, bool exportPdf)
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            Dialogs.Message("Публикация", "Выберите карту на вкладке «Карта».");
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var dialog = new OpenFolderDialog
        {
            Title = "Куда сохранить публикацию",
            DefaultDirectory = _lastOutputDirectory ?? Path.Combine(project.RootPath, "out")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _lastOutputDirectory = dialog.FolderName;

        var options = new PublishOptions
        {
            OutputDirectory = dialog.FolderName,
            SingleFile = singleFile,
            ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Сборка карты {map.RelativePath}…\n";

        try
        {
            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(map.FullPath, options);

            BuildLogText += $"Файлов записано: {result.Files.Count}\n";
            foreach (var warning in result.Warnings)
            {
                BuildLogText += "Предупреждение: " + warning + "\n";
            }

            BuildLogText += "Результат: " + result.EntryFile + "\n";

            if (exportPdf)
            {
                var pdfPath = Path.ChangeExtension(result.EntryFile, ".pdf");
                BuildLogText += "Печать в PDF…\n";
                var error = await ExportPdfAsync(project, result.EntryFile, pdfPath);
                if (error is null)
                {
                    BuildLogText += "PDF готов: " + pdfPath + "\n";
                    _main.StatusText = "PDF собран: " + pdfPath;
                    OpenInShell(pdfPath);
                    return;
                }

                BuildLogText += "PDF: " + error + "\n";
                Dialogs.Message("Экспорт в PDF", error);
            }

            _main.StatusText = "Публикация готова: " + result.EntryFile;
            OpenInShell(result.EntryFile);
        }
        catch (Exception ex)
        {
            BuildLogText += "Ошибка: " + ex.Message + "\n";
            Dialogs.Message("Публикация", ex.Message);
        }
    }

    /// <summary>Выбирает способ печати в PDF: через WebView2 (нужен для своих колонтитулов и как
    /// запасной путь без Edge/Chrome), иначе — обычная CLI-печать браузером без колонтитулов.</summary>
    private async Task<string?> ExportPdfAsync(DitaProject project, string htmlPath, string pdfPath)
    {
        var showHeaderFooter = project.PdfShowHeaderFooter;
        var browserAvailable = PdfExporter.IsAvailable;

        if (showHeaderFooter || !browserAvailable)
        {
            var error = await WebView2PdfExporter.ExportAsync(htmlPath, pdfPath, showHeaderFooter,
                project.PdfHeaderText, project.PdfFooterText);
            if (error is null || !browserAvailable)
            {
                return error;
            }

            BuildLogText += $"WebView2: {error} — печатаем через установленный браузер без колонтитулов.\n";
        }

        return PdfExporter.ExportToPdf(htmlPath, pdfPath);
    }

    private static void OpenInShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // если открыть нечем — файл всё равно собран
        }
    }
}
