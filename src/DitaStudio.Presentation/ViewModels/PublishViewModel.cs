using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.IO;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Публикация: сборка HTML-сайта/одного файла, экспорт в PDF (с колонтитулами
// через встроенный браузер оболочки — IPdfPrinter) и в DOCX, условия сборки (в т.ч. импорт .ditaval),
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
    private Task PublishSite() => PublishAsync(singleFile: false, exportPdf: false);

    [RelayCommand]
    private Task PublishSingle() => PublishAsync(singleFile: true, exportPdf: false);

    [RelayCommand]
    private Task PublishPdf() => PublishAsync(singleFile: true, exportPdf: true);

    [RelayCommand]
    private async Task ExportDocx()
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            await _main.Dialogs.MessageAsync("Экспорт в DOCX", "Выберите карту на вкладке «Карта».");
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputFile = await _main.Files.SaveFileAsync("Экспорт в DOCX",
            new[] { new FileFilter("Документ Word", "*.docx") },
            Path.GetFileNameWithoutExtension(map.FullPath) + ".docx",
            _lastOutputDirectory ?? project.RootPath);
        if (outputFile is null)
        {
            return;
        }

        var options = new PublishOptions
        {
            ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        var conditionsWarning = ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Сборка DOCX по карте {map.RelativePath}…\n";

        if (conditionsWarning is not null)
        {
            BuildLogText += "Предупреждение: " + conditionsWarning + "\n";
        }

        try
        {
            var publisher = new DocxPublisher(project);
            var result = publisher.Publish(map.FullPath, options, outputFile);

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
            await _main.Dialogs.MessageAsync("Экспорт в DOCX", ex.Message);
        }
    }

    /// <summary>Публикует картой через выбранный плагин формата (см. IPublishFormatPlugin) —
    /// пункт меню один и тот же для любого числа подключённых плагинов, выбор через диалог.</summary>
    [RelayCommand]
    private async Task PublishWithPlugin()
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            await _main.Dialogs.MessageAsync("Публикация плагином", "Выберите карту на вкладке «Карта».");
            return;
        }

        var format = await _main.Dialogs.PickOneAsync("Публикация плагином", "Выберите формат публикации:",
            PluginRegistry.PublishFormats, f => f.Name);
        if (format is null)
        {
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputFile = await _main.Files.SaveFileAsync($"Публикация: {format.Name}",
            new[] { new FileFilter(format.Name, "*." + format.FileExtension) },
            Path.GetFileNameWithoutExtension(map.FullPath) + "." + format.FileExtension,
            _lastOutputDirectory ?? project.RootPath);
        if (outputFile is null)
        {
            return;
        }

        var options = new PublishOptions { ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false, Language = "ru" };
        var conditionsWarning = ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Публикация плагином «{format.Name}» по карте {map.RelativePath}…\n";

        if (conditionsWarning is not null)
        {
            BuildLogText += "Предупреждение: " + conditionsWarning + "\n";
        }

        try
        {
            format.Publish(project, map.FullPath, options, outputFile);
            BuildLogText += "Результат: " + outputFile + "\n";
            _main.StatusText = $"Опубликовано плагином «{format.Name}»: {outputFile}";
            OpenInShell(outputFile);
        }
        catch (Exception ex)
        {
            BuildLogText += "Ошибка: " + ex.Message + "\n";
            await _main.Dialogs.MessageAsync("Публикация плагином", $"Плагин «{format.Name}» упал: {ex.Message}");
        }
    }

    /// <summary>Список продуктов проекта: значения для атрибута product; импорт из другого проекта.</summary>
    [RelayCommand]
    private async Task EditProducts()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Список продуктов", "Сначала откройте папку проекта.");
            return;
        }

        if (await _main.Dialogs.EditProductsAsync(project) is { } products)
        {
            project.SetProducts(products);
            _main.RefreshAttributePanel?.Invoke();
            _main.StatusText = $"Список продуктов: {project.Products.Count}.";
        }
    }

    [RelayCommand]
    private async Task PublishConditions()
    {
        var project = _main.Project;
        if (project is null)
        {
            return;
        }

        var result = await _main.Dialogs.PublishConditionsAsync(project, _main.Conditions);
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
    private async Task ImportDitaval()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Условия сборки", "Сначала откройте папку проекта.");
            return;
        }

        var ditavalFile = await _main.Files.OpenFileAsync("Подключить файл условий (.ditaval)",
            new[] { new FileFilter("Файлы DITAVAL", "*.ditaval"), FileFilter.All },
            project.DitavalPath is null ? project.RootPath : Path.GetDirectoryName(Path.Combine(project.RootPath, project.DitavalPath)));
        if (ditavalFile is null)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(project.RootPath, ditavalFile).Replace('\\', '/');

        DitavalRules? rules;
        try
        {
            rules = DitavalReader.Read(ditavalFile);
        }
        catch (Exception ex)
        {
            await _main.Dialogs.MessageAsync("Условия сборки", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        project.SetDitavalPath(relativePath);
        var excludedCount = rules.Exclude.Sum(x => x.Value.Count);
        _main.StatusText = $"Подключён .ditaval: {relativePath} (исключений: {excludedCount}, правил подсветки: {rules.Flags.Count}). Изменения файла подхватываются автоматически.";
    }

    /// <summary>Правит правила исключения СВЯЗАННОГО .ditaval прямо в приложении, не выходя в
    /// текстовый редактор — записывает обратно в тот же файл (правила подсветки сохраняются).</summary>
    [RelayCommand]
    private async Task EditDitaval()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Редактирование .ditaval", "Сначала откройте папку проекта.");
            return;
        }

        if (await _main.Dialogs.EditDitavalAsync(project))
        {
            _main.StatusText = $"Файл .ditaval обновлён: {project.DitavalPath}.";
        }
    }

    /// <summary>Дополняет условия сборки исключениями и правилами подсветки из связанного .ditaval
    /// (см. <see cref="ImportDitaval"/>) поверх вручную заданных в диалоге условий.</summary>
    /// <returns>Предупреждение для журнала сборки, если подключённый .ditaval не прочитан.</returns>
    private string? ApplyConditions(PublishOptions options)
    {
        if (_main.Conditions is not null)
        {
            foreach (var (attribute, values) in _main.Conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        string? error = null;
        var linked = _main.Project?.ResolveLinkedDitaval(out error);
        if (linked is null)
        {
            return error;
        }

        DitaProject.MergeExcludeConditions(options.ExcludeConditions, linked.Exclude);
        options.FlagConditions.AddRange(linked.Flags);
        return null;
    }

    [RelayCommand]
    private async Task PdfHeaderFooter()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Колонтитулы PDF", "Сначала откройте папку проекта.");
            return;
        }

        var result = await _main.Dialogs.PdfHeaderFooterAsync(project);
        if (result is null)
        {
            return;
        }

        project.SetPdfHeaderFooter(result.Show, result.HeaderText, result.FooterText);
        _main.StatusText = result.Show ? "Колонтитулы PDF включены." : "Колонтитулы PDF отключены.";
    }

    /// <summary>«Оформление DOCX» — титул, оглавление, нумерация заголовков, колонтитулы и прочее,
    /// чего не выразить пользовательским CSS. Сохраняется вместе с проектом.</summary>
    [RelayCommand]
    private async Task DocxLayout()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Оформление DOCX", "Сначала откройте папку проекта.");
            return;
        }

        var result = await _main.Dialogs.DocxLayoutSettingsAsync(project);
        if (result is null)
        {
            return;
        }

        try
        {
            project.SetDocxLayout(result);
            _main.StatusText = "Оформление DOCX сохранено — применится при следующем экспорте в DOCX.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.Dialogs.MessageAsync("Оформление DOCX", $"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PageSetup()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Параметры страницы", "Сначала откройте папку проекта.");
            return;
        }

        var result = await _main.Dialogs.PageSetupAsync(project);
        if (result is null)
        {
            return;
        }

        try
        {
            project.SetDocxLayout(result);
            _main.StatusText = "Параметры страницы сохранены — применятся при следующем экспорте в DOCX и PDF.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.Dialogs.MessageAsync("Параметры страницы", $"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CustomCss()
    {
        var project = _main.Project;
        if (project is null)
        {
            await _main.Dialogs.MessageAsync("Пользовательский CSS", "Сначала откройте папку проекта.");
            return;
        }

        await _main.Dialogs.CustomCssAsync(project);
        _main.StatusText = project.CustomCssPath is null
            ? "Пользовательский CSS отключён."
            : $"Пользовательский CSS: {project.CustomCssPath}";
    }

    /// <summary>Экспортирует открытый документ в XLIFF 1.2 для перевода — сегмент на каждый
    /// блочный элемент с прямым текстом, фразовая разметка внутри как bpt/ept/ph (см. XliffConverter).</summary>
    [RelayCommand]
    private async Task ExportXliff()
    {
        var pane = _main.Current;
        if (pane is null)
        {
            await _main.Dialogs.MessageAsync("Экспорт в XLIFF", "Откройте документ.");
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            await _main.Dialogs.MessageAsync("Экспорт в XLIFF", $"Документ не разбирается как XML:\n\n{error}");
            return;
        }

        var xliffFile = await _main.Files.SaveFileAsync("Экспорт в XLIFF",
            new[] { new FileFilter("Файлы XLIFF", "*.xliff"), FileFilter.All },
            Path.GetFileNameWithoutExtension(pane.FilePath ?? "document") + ".xliff",
            pane.FilePath is null ? null : Path.GetDirectoryName(pane.FilePath));
        if (xliffFile is null)
        {
            return;
        }

        var xliff = XliffConverter.Export(pane.Document, "ru", "en");
        try
        {
            AtomicFile.Write(xliffFile, stream => xliff.Save(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _main.Dialogs.MessageAsync("Экспорт в XLIFF", $"Не удалось записать файл: {ex.Message}");
            return;
        }

        _main.StatusText = $"Экспортировано в XLIFF: {xliffFile}";
    }

    /// <summary>Подставляет перевод из &lt;target&gt; каждого сегмента XLIFF обратно в открытый
    /// документ — по номеру сегмента, тем же обходом дерева, что и при экспорте (см. XliffConverter).
    /// Требует, чтобы структура документа не менялась с момента экспорта.</summary>
    [RelayCommand]
    private async Task ImportXliff()
    {
        var pane = _main.Current;
        if (pane is null)
        {
            await _main.Dialogs.MessageAsync("Импорт из XLIFF", "Откройте документ, в который нужно подставить перевод.");
            return;
        }

        var xliffFile = await _main.Files.OpenFileAsync("Импортировать перевод из XLIFF",
            new[] { new FileFilter("Файлы XLIFF", "*.xliff"), FileFilter.All },
            pane.FilePath is null ? null : Path.GetDirectoryName(pane.FilePath));
        if (xliffFile is null)
        {
            return;
        }

        XDocument xliff;
        try
        {
            xliff = XDocument.Load(xliffFile);
        }
        catch (Exception ex)
        {
            await _main.Dialogs.MessageAsync("Импорт из XLIFF", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            await _main.Dialogs.MessageAsync("Импорт из XLIFF", $"Документ не разбирается как XML:\n\n{error}");
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
            await _main.Dialogs.MessageAsync("Импорт из XLIFF — предупреждения", string.Join("\n", warnings));
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

    private async Task PublishAsync(bool singleFile, bool exportPdf)
    {
        var project = _main.Project;
        var map = _main.Map.SelectedMap;
        if (project is null || map is null)
        {
            await _main.Dialogs.MessageAsync("Публикация", "Выберите карту на вкладке «Карта».");
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputDirectory = await _main.Files.OpenFolderAsync("Куда сохранить публикацию",
            _lastOutputDirectory ?? Path.Combine(project.RootPath, "out"));
        if (outputDirectory is null)
        {
            return;
        }

        _lastOutputDirectory = outputDirectory;

        var options = new PublishOptions
        {
            OutputDirectory = outputDirectory,
            SingleFile = singleFile,
            ShowDraftComments = _main.Conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        var conditionsWarning = ApplyConditions(options);

        _main.BottomTabIndex = 2;
        BuildLogText = $"Сборка карты {map.RelativePath}…\n";

        if (conditionsWarning is not null)
        {
            BuildLogText += "Предупреждение: " + conditionsWarning + "\n";
        }

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
                await _main.Dialogs.MessageAsync("Экспорт в PDF", error);
            }

            _main.StatusText = "Публикация готова: " + result.EntryFile;
            OpenInShell(result.EntryFile);
        }
        catch (Exception ex)
        {
            BuildLogText += "Ошибка: " + ex.Message + "\n";
            await _main.Dialogs.MessageAsync("Публикация", ex.Message);
        }
    }

    /// <summary>Выбирает способ печати в PDF: встроенным браузером оболочки (нужен для своих
    /// колонтитулов и как запасной путь без Edge/Chrome), иначе — обычная CLI-печать браузером
    /// без колонтитулов.</summary>
    private async Task<string?> ExportPdfAsync(DitaProject project, string htmlPath, string pdfPath)
    {
        var showHeaderFooter = project.PdfShowHeaderFooter;
        var browserAvailable = PdfExporter.IsAvailable;

        if (showHeaderFooter || !browserAvailable)
        {
            var error = await _main.Services.PdfPrinter.ExportAsync(htmlPath, pdfPath, PdfPageDecoration.For(project));
            if (error is null || !browserAvailable)
            {
                return error;
            }

            BuildLogText += $"Встроенный браузер: {error} — печатаем через установленный браузер без колонтитулов.\n";
        }

        // Печать браузером идёт до двух минут — в фоне, чтобы окно не замирало.
        return await Task.Run(() => PdfExporter.ExportToPdf(htmlPath, pdfPath));
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
