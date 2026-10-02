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
    private readonly IShellState _shell;
    private readonly IWorkspace _workspace;
    private readonly UiServices _ui;
    private readonly ShellHooks _hooks;
    private readonly IDocumentHost _docs;
    private readonly MapViewModel _map;
    private string? _lastOutputDirectory;

    [ObservableProperty]
    private string buildLogText = string.Empty;

    public PublishViewModel(ShellContext context, IDocumentHost docs, MapViewModel map)
    {
        _shell = context.Shell;
        _workspace = context.Workspace;
        _ui = context.Ui;
        _hooks = context.Hooks;
        _docs = docs;
        _map = map;
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
        var project = _workspace.Project;
        var map = _map.SelectedMap;
        if (project is null || map is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToDOCX"), Loc.T("Msg_ChooseAMapOnTheMap"));
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputFile = await _ui.Files.SaveFileAsync(Loc.T("Msg_ExportToDOCX"),
            new[] { new FileFilter(Loc.T("Msg_WordDocument"), "*.docx") },
            Path.GetFileNameWithoutExtension(map.FullPath) + ".docx",
            _lastOutputDirectory ?? project.RootPath);
        if (outputFile is null)
        {
            return;
        }

        var options = new PublishOptions
        {
            ShowDraftComments = _workspace.Conditions?.ShowDraftComments ?? false,
            Language = Loc.Instance.Language
        };

        var conditionsWarning = ApplyConditions(options);

        _shell.BottomTabIndex = 2;
        BuildLogText = Loc.T("Msg_BuildingDOCXFromTheMap0", map.RelativePath);

        if (conditionsWarning is not null)
        {
            BuildLogText += Loc.T("Msg_Warning") + conditionsWarning + "\n";
        }

        try
        {
            var publisher = new DocxPublisher(project);
            var result = publisher.Publish(map.FullPath, options, outputFile);

            foreach (var warning in result.Warnings)
            {
                BuildLogText += Loc.T("Msg_Warning") + warning + "\n";
            }

            BuildLogText += Loc.T("Msg_Result") + result.OutputFile + "\n";
            _shell.StatusText = Loc.T("Msg_DOCXBuilt") + result.OutputFile;
            OpenInShell(result.OutputFile);
        }
        catch (Exception ex)
        {
            BuildLogText += Loc.T("Msg_Error") + ex.Message + "\n";
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToDOCX"), ex.Message);
        }
    }

    /// <summary>Публикует картой через выбранный плагин формата (см. IPublishFormatPlugin) —
    /// пункт меню один и тот же для любого числа подключённых плагинов, выбор через диалог.</summary>
    [RelayCommand]
    private async Task PublishWithPlugin()
    {
        var project = _workspace.Project;
        var map = _map.SelectedMap;
        if (project is null || map is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PublishWithAPlugin"), Loc.T("Msg_ChooseAMapOnTheMap"));
            return;
        }

        var format = await _ui.Dialogs.PickOneAsync(Loc.T("Msg_PublishWithAPlugin"), Loc.T("Msg_ChooseAPublicationFormat"),
            PluginRegistry.PublishFormats, f => f.Name);
        if (format is null)
        {
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputFile = await _ui.Files.SaveFileAsync(Loc.T("Msg_Publication0", format.Name),
            new[] { new FileFilter(format.Name, "*." + format.FileExtension) },
            Path.GetFileNameWithoutExtension(map.FullPath) + "." + format.FileExtension,
            _lastOutputDirectory ?? project.RootPath);
        if (outputFile is null)
        {
            return;
        }

        var options = new PublishOptions { ShowDraftComments = _workspace.Conditions?.ShowDraftComments ?? false, Language = Loc.Instance.Language };
        var conditionsWarning = ApplyConditions(options);

        _shell.BottomTabIndex = 2;
        BuildLogText = Loc.T("Msg_PublishingWithThePlugin0From", format.Name, map.RelativePath);

        if (conditionsWarning is not null)
        {
            BuildLogText += Loc.T("Msg_Warning") + conditionsWarning + "\n";
        }

        try
        {
            format.Publish(project, map.FullPath, options, outputFile);
            BuildLogText += Loc.T("Msg_Result") + outputFile + "\n";
            _shell.StatusText = Loc.T("Msg_PublishedWithThePlugin01", format.Name, outputFile);
            OpenInShell(outputFile);
        }
        catch (Exception ex)
        {
            BuildLogText += Loc.T("Msg_Error") + ex.Message + "\n";
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PublishWithAPlugin"), Loc.T("Msg_ThePlugin0Failed1", format.Name, ex.Message));
        }
    }

    /// <summary>Список продуктов проекта: значения для атрибута product; импорт из другого проекта.</summary>
    [RelayCommand]
    private async Task EditProducts()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_ProductList"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        if (await _ui.Dialogs.EditProductsAsync(project) is { } products)
        {
            project.SetProducts(products);
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = Loc.T("Msg_ProductList0", project.Products.Count);
        }
    }

    [RelayCommand]
    private async Task PublishConditions()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            return;
        }

        var result = await _ui.Dialogs.PublishConditionsAsync(project, _workspace.Conditions);
        if (result is not null)
        {
            _workspace.Conditions = result;
            project.SetConditions(result.Exclude, result.ShowDraftComments);
            _shell.StatusText = Loc.T("Msg_BuildConditionsUpdatedValuesExcluded0", _workspace.Conditions.Exclude.Sum(x => x.Value.Count));
        }
    }

    /// <summary>Подключает .ditaval к проекту (путь сохраняется вместе с проектом) — при каждой
    /// сборке файл перечитывается заново, руками переимпортировать не нужно.</summary>
    [RelayCommand]
    private async Task ImportDitaval()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_BuildConditions"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        var ditavalFile = await _ui.Files.OpenFileAsync(Loc.T("Msg_ConnectAConditionsFileDitaval"),
            new[] { new FileFilter(Loc.T("Msg_DITAVALFiles"), "*.ditaval"), FileFilter.All },
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
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_BuildConditions"), Loc.T("Msg_CouldNotReadTheFile0", ex.Message));
            return;
        }

        project.SetDitavalPath(relativePath);
        var excludedCount = rules.Exclude.Sum(x => x.Value.Count);
        _shell.StatusText = Loc.T("Msg_ConnectedDitaval0Exclusions1Flagging", relativePath, excludedCount, rules.Flags.Count);
    }

    /// <summary>Правит правила исключения СВЯЗАННОГО .ditaval прямо в приложении, не выходя в
    /// текстовый редактор — записывает обратно в тот же файл (правила подсветки сохраняются).</summary>
    [RelayCommand]
    private async Task EditDitaval()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_EditDitaval"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        if (await _ui.Dialogs.EditDitavalAsync(project))
        {
            _shell.StatusText = Loc.T("Msg_TheDitavalFileWasUpdated0", project.DitavalPath);
        }
    }

    /// <summary>Дополняет условия сборки исключениями и правилами подсветки из связанного .ditaval
    /// (см. <see cref="ImportDitaval"/>) поверх вручную заданных в диалоге условий.</summary>
    /// <returns>Предупреждение для журнала сборки, если подключённый .ditaval не прочитан.</returns>
    private string? ApplyConditions(PublishOptions options)
    {
        if (_workspace.Conditions is not null)
        {
            foreach (var (attribute, values) in _workspace.Conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        string? error = null;
        var linked = _workspace.Project?.ResolveLinkedDitaval(out error);
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
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_PDFHeadersAndFooters"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        var result = await _ui.Dialogs.PdfHeaderFooterAsync(project);
        if (result is null)
        {
            return;
        }

        project.SetPdfHeaderFooter(result.Show, result.HeaderText, result.FooterText);
        _shell.StatusText = result.Show ? Loc.T("Msg_PDFHeadersAndFootersAreOn") : Loc.T("Msg_PDFHeadersAndFootersAreOff");
    }

    /// <summary>«Оформление DOCX» — титул, оглавление, нумерация заголовков, колонтитулы и прочее,
    /// чего не выразить пользовательским CSS. Сохраняется вместе с проектом.</summary>
    [RelayCommand]
    private async Task DocxLayout()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_DOCXLayout"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        var result = await _ui.Dialogs.DocxLayoutSettingsAsync(project);
        if (result is null)
        {
            return;
        }

        try
        {
            project.SetDocxLayout(result);
            _shell.StatusText = Loc.T("Msg_TheDOCXLayoutWasSavedIt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_DOCXLayout"), Loc.T("Msg_CouldNotSaveTheSettings0", ex.Message));
        }
    }

    [RelayCommand]
    private async Task PageSetup()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PageSetup"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        var result = await _ui.Dialogs.PageSetupAsync(project);
        if (result is null)
        {
            return;
        }

        try
        {
            project.SetDocxLayout(result);
            _shell.StatusText = Loc.T("Msg_PageSetupSavedItAppliesAt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_PageSetup"), Loc.T("Msg_CouldNotSaveTheSettings0", ex.Message));
        }
    }

    [RelayCommand]
    private async Task CustomCss()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Dlg_CustomCSS"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        await _ui.Dialogs.CustomCssAsync(project);
        _shell.StatusText = project.CustomCssPath is null
            ? Loc.T("Msg_CustomCSSIsOff")
            : Loc.T("Msg_CustomCSS0", project.CustomCssPath);
    }

    /// <summary>Экспортирует открытый документ в XLIFF 1.2 для перевода — сегмент на каждый
    /// блочный элемент с прямым текстом, фразовая разметка внутри как bpt/ept/ph (см. XliffConverter).</summary>
    [RelayCommand]
    private async Task ExportXliff()
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToXLIFF"), Loc.T("Msg_OpenADocument"));
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToXLIFF"), Loc.T("Msg_TheDocumentCannotBeParsedAs", error));
            return;
        }

        var xliffFile = await _ui.Files.SaveFileAsync(Loc.T("Msg_ExportToXLIFF"),
            new[] { new FileFilter(Loc.T("Msg_XLIFFFiles"), "*.xliff"), FileFilter.All },
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
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToXLIFF"), Loc.T("Dlg_CouldNotWriteTheFile0", ex.Message));
            return;
        }

        _shell.StatusText = Loc.T("Msg_ExportedToXLIFF0", xliffFile);
    }

    /// <summary>Подставляет перевод из &lt;target&gt; каждого сегмента XLIFF обратно в открытый
    /// документ — по номеру сегмента, тем же обходом дерева, что и при экспорте (см. XliffConverter).
    /// Требует, чтобы структура документа не менялась с момента экспорта.</summary>
    [RelayCommand]
    private async Task ImportXliff()
    {
        var pane = _docs.Current;
        if (pane is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportFromXLIFF"), Loc.T("Msg_OpenTheDocumentToPutThe"));
            return;
        }

        var xliffFile = await _ui.Files.OpenFileAsync(Loc.T("Msg_ImportTranslationFromXLIFF"),
            new[] { new FileFilter(Loc.T("Msg_XLIFFFiles"), "*.xliff"), FileFilter.All },
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
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportFromXLIFF"), Loc.T("Msg_CouldNotReadTheFile0", ex.Message));
            return;
        }

        var error = pane.CommitPendingEdits();
        if (error is not null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportFromXLIFF"), Loc.T("Msg_TheDocumentCannotBeParsedAs", error));
            return;
        }

        var warnings = new List<string>();
        var applied = XliffConverter.Import(pane.Document, xliff, warnings);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _docs.RefreshAllTabTitles();

        _shell.StatusText = Loc.T("Msg_TranslationImportedSegmentsApplied0", applied) +
                            (warnings.Count > 0 ? Loc.T("Msg_Warnings02", warnings.Count) : ".");
        if (warnings.Count > 0)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportFromXLIFFWarnings"), string.Join("\n", warnings));
        }
    }

    /// <summary>Сбрасывает несохранённые правки во всех открытых вкладках на диск и пересобирает
    /// пространство ключей — общий первый шаг перед любой публикацией (HTML/PDF/DOCX).</summary>
    private void SaveAllPanesAndRebuildKeySpace()
    {
        foreach (var pane in _docs.Panes.Values)
        {
            pane.CommitPendingEdits();
            if (pane.IsDirty)
            {
                pane.Save(out _);
            }
        }

        _workspace.Project!.RebuildKeySpace();
    }

    private async Task PublishAsync(bool singleFile, bool exportPdf)
    {
        var project = _workspace.Project;
        var map = _map.SelectedMap;
        if (project is null || map is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_Publication"), Loc.T("Msg_ChooseAMapOnTheMap"));
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var outputDirectory = await _ui.Files.OpenFolderAsync(Loc.T("Msg_WhereToSaveThePublication"),
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
            ShowDraftComments = _workspace.Conditions?.ShowDraftComments ?? false,
            Language = Loc.Instance.Language
        };

        var conditionsWarning = ApplyConditions(options);

        _shell.BottomTabIndex = 2;
        BuildLogText = Loc.T("Msg_BuildingTheMap0", map.RelativePath);

        if (conditionsWarning is not null)
        {
            BuildLogText += Loc.T("Msg_Warning") + conditionsWarning + "\n";
        }

        try
        {
            var publisher = new HtmlPublisher(project);
            var result = publisher.Publish(map.FullPath, options);

            BuildLogText += Loc.T("Msg_FilesWritten0", result.Files.Count);
            foreach (var warning in result.Warnings)
            {
                BuildLogText += Loc.T("Msg_Warning") + warning + "\n";
            }

            BuildLogText += Loc.T("Msg_Result") + result.EntryFile + "\n";

            if (exportPdf)
            {
                var pdfPath = Path.ChangeExtension(result.EntryFile, ".pdf");
                BuildLogText += Loc.T("Msg_PrintingToPDF");
                var error = await ExportPdfAsync(project, result.EntryFile, pdfPath);
                if (error is null)
                {
                    BuildLogText += Loc.T("Msg_PDFReady") + pdfPath + "\n";
                    _shell.StatusText = Loc.T("Msg_PDFBuilt") + pdfPath;
                    OpenInShell(pdfPath);
                    return;
                }

                BuildLogText += "PDF: " + error + "\n";
                await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportToPDF"), error);
            }

            _shell.StatusText = Loc.T("Msg_PublicationReady") + result.EntryFile;
            OpenInShell(result.EntryFile);
        }
        catch (Exception ex)
        {
            BuildLogText += Loc.T("Msg_Error") + ex.Message + "\n";
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_Publication"), ex.Message);
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
            var error = await _ui.PdfPrinter.ExportAsync(htmlPath, pdfPath, PdfPageDecoration.For(project));
            if (error is null || !browserAvailable)
            {
                return error;
            }

            BuildLogText += Loc.T("Msg_BuiltInBrowser0PrintingWith", error);
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
