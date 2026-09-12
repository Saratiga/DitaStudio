using System.Windows;
using DitaStudio.App.Views;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx;
using Microsoft.Win32;

namespace DitaStudio.App;

// Публикация: сборка HTML-сайта/одного файла, экспорт в PDF (с колонтитулами через WebView2)
// и в DOCX, условия сборки (в т.ч. импорт .ditaval), пользовательский CSS.
public partial class MainWindow
{
    private void OnPublishSite(object sender, RoutedEventArgs e) => PublishSite();

    private void PublishSite() => Publish(singleFile: false, exportPdf: false);

    private void OnPublishSingle(object sender, RoutedEventArgs e) => Publish(singleFile: true, exportPdf: false);

    private void OnPublishPdf(object sender, RoutedEventArgs e) => Publish(singleFile: true, exportPdf: true);

    private void OnExportDocx(object sender, RoutedEventArgs e)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
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
            InitialDirectory = _lastOutputDirectory ?? _project.RootPath
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var options = new PublishOptions
        {
            ShowDraftComments = _conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        if (_conditions is not null)
        {
            foreach (var (attribute, values) in _conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        BottomTabs.SelectedIndex = 2;
        BuildLog.Text = $"Сборка DOCX по карте {map.RelativePath}…\n";

        try
        {
            var publisher = new DocxPublisher(_project);
            var result = publisher.Publish(map.FullPath, options, dialog.FileName);

            foreach (var warning in result.Warnings)
            {
                BuildLog.AppendText("Предупреждение: " + warning + "\n");
            }

            BuildLog.AppendText("Результат: " + result.OutputFile + "\n");
            UpdateStatus("DOCX собран: " + result.OutputFile);
            OpenInShell(result.OutputFile);
        }
        catch (Exception ex)
        {
            BuildLog.AppendText("Ошибка: " + ex.Message + "\n");
            Dialogs.Message("Экспорт в DOCX", ex.Message);
        }
    }

    private void OnPublishConditions(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var result = Dialogs.PublishConditions(_project, _conditions);
        if (result is not null)
        {
            _conditions = result;
            _project.SetConditions(result.Exclude, result.ShowDraftComments);
            UpdateStatus($"Условия сборки обновлены: исключено значений {_conditions.Exclude.Sum(x => x.Value.Count)}.");
        }
    }

    private void OnImportDitaval(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            Dialogs.Message("Условия сборки", "Сначала откройте папку проекта.");
            return;
        }

        var dialog = new OpenFileDialog { Title = "Импорт условий из .ditaval", Filter = "Файлы DITAVAL|*.ditaval|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Dictionary<string, HashSet<string>> imported;
        try
        {
            imported = DitavalReader.ReadExcludeRules(dialog.FileName);
        }
        catch (Exception ex)
        {
            Dialogs.Message("Условия сборки", $"Не удалось прочитать файл: {ex.Message}");
            return;
        }

        var merged = new Dictionary<string, HashSet<string>>(_conditions?.Exclude ?? new Dictionary<string, HashSet<string>>());
        var importedValues = 0;
        foreach (var (attribute, values) in imported)
        {
            if (!merged.TryGetValue(attribute, out var set))
            {
                set = new HashSet<string>();
                merged[attribute] = set;
            }

            foreach (var value in values)
            {
                if (set.Add(value))
                {
                    importedValues++;
                }
            }
        }

        _conditions = new Dialogs.ConditionsResult(merged, _conditions?.ShowDraftComments ?? false);
        _project.SetConditions(merged, _conditions.ShowDraftComments);
        UpdateStatus($"Из .ditaval импортировано правил исключения: {importedValues}.");
    }

    private void OnPdfHeaderFooter(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            Dialogs.Message("Колонтитулы PDF", "Сначала откройте папку проекта.");
            return;
        }

        var result = Dialogs.PdfHeaderFooter(_project);
        if (result is null)
        {
            return;
        }

        _project.SetPdfHeaderFooter(result.Show, result.HeaderText, result.FooterText);
        UpdateStatus(result.Show ? "Колонтитулы PDF включены." : "Колонтитулы PDF отключены.");
    }

    private void OnCustomCss(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            Dialogs.Message("Пользовательский CSS", "Сначала откройте папку проекта.");
            return;
        }

        Dialogs.CustomCss(_project);
        UpdateStatus(_project.CustomCssPath is null
            ? "Пользовательский CSS отключён."
            : $"Пользовательский CSS: {_project.CustomCssPath}");
    }

    /// <summary>Сбрасывает несохранённые правки во всех открытых вкладках на диск и пересобирает
    /// пространство ключей — общий первый шаг перед любой публикацией (HTML/PDF/DOCX).</summary>
    private void SaveAllPanesAndRebuildKeySpace()
    {
        foreach (var pane in _panes.Values)
        {
            pane.CommitPendingEdits();
            if (pane.IsDirty)
            {
                pane.Save(out _);
            }
        }

        _project!.RebuildKeySpace();
    }

    private async void Publish(bool singleFile, bool exportPdf)
    {
        if (_project is null || MapSelector.SelectedItem is not ProjectFile map)
        {
            Dialogs.Message("Публикация", "Выберите карту на вкладке «Карта».");
            return;
        }

        SaveAllPanesAndRebuildKeySpace();

        var dialog = new OpenFolderDialog
        {
            Title = "Куда сохранить публикацию",
            DefaultDirectory = _lastOutputDirectory ?? Path.Combine(_project.RootPath, "out")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _lastOutputDirectory = dialog.FolderName;

        var options = new PublishOptions
        {
            OutputDirectory = dialog.FolderName,
            SingleFile = singleFile,
            ShowDraftComments = _conditions?.ShowDraftComments ?? false,
            Language = "ru"
        };

        if (_conditions is not null)
        {
            foreach (var (attribute, values) in _conditions.Exclude)
            {
                options.ExcludeConditions[attribute] = values;
            }
        }

        BottomTabs.SelectedIndex = 2;
        BuildLog.Text = $"Сборка карты {map.RelativePath}…\n";

        try
        {
            var publisher = new HtmlPublisher(_project);
            var result = publisher.Publish(map.FullPath, options);

            BuildLog.AppendText($"Файлов записано: {result.Files.Count}\n");
            foreach (var warning in result.Warnings)
            {
                BuildLog.AppendText("Предупреждение: " + warning + "\n");
            }

            BuildLog.AppendText("Результат: " + result.EntryFile + "\n");

            if (exportPdf)
            {
                var pdfPath = Path.ChangeExtension(result.EntryFile, ".pdf");
                BuildLog.AppendText("Печать в PDF…\n");
                var error = await ExportPdfAsync(result.EntryFile, pdfPath);
                if (error is null)
                {
                    BuildLog.AppendText("PDF готов: " + pdfPath + "\n");
                    UpdateStatus("PDF собран: " + pdfPath);
                    OpenInShell(pdfPath);
                    return;
                }

                BuildLog.AppendText("PDF: " + error + "\n");
                Dialogs.Message("Экспорт в PDF", error);
            }

            UpdateStatus("Публикация готова: " + result.EntryFile);
            OpenInShell(result.EntryFile);
        }
        catch (Exception ex)
        {
            BuildLog.AppendText("Ошибка: " + ex.Message + "\n");
            Dialogs.Message("Публикация", ex.Message);
        }
    }

    /// <summary>Выбирает способ печати в PDF: через WebView2 (нужен для своих колонтитулов и как
    /// запасной путь без Edge/Chrome), иначе — обычная CLI-печать браузером без колонтитулов.</summary>
    private async Task<string?> ExportPdfAsync(string htmlPath, string pdfPath)
    {
        var showHeaderFooter = _project?.PdfShowHeaderFooter ?? false;
        var browserAvailable = PdfExporter.IsAvailable;

        if (showHeaderFooter || !browserAvailable)
        {
            var error = await WebView2PdfExporter.ExportAsync(htmlPath, pdfPath, showHeaderFooter,
                _project?.PdfHeaderText, _project?.PdfFooterText);
            if (error is null || !browserAvailable)
            {
                return error;
            }

            BuildLog.AppendText($"WebView2: {error} — печатаем через установленный браузер без колонтитулов.\n");
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
