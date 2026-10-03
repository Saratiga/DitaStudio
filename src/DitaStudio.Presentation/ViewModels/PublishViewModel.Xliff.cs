using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Project;

namespace DitaStudio.Presentation.ViewModels;

// Пакетный перевод: вся карта в XLIFF (по файлу на топик) и обратно.
public partial class PublishViewModel
{
    /// <summary>Выгружает все топики выбранной карты в папку: по XLIFF-файлу на топик и manifest.json.</summary>
    [RelayCommand]
    private async Task ExportXliffBatch()
    {
        var project = _workspace.Project;
        var map = _map.SelectedMap;
        if (project is null || map is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportMapToXLIFF"), Loc.T("Msg_ChooseAMapOnThe"));
            return;
        }

        foreach (var pane in _docs.Panes.Values.ToList())
        {
            pane.CommitPendingEdits(); // несохранённые правки открытых топиков входят в выгрузку
        }

        var folder = await _ui.Files.OpenFolderAsync(Loc.T("Msg_FolderForXLIFFFiles"), project.RootPath);
        if (folder is null)
        {
            return;
        }

        var sourceLanguage = DocumentLanguage.Of(project.TryGetDocument(map.FullPath)) ?? Loc.Instance.Language;
        var defaultTarget = DocumentLanguage.Primary(sourceLanguage) == "en" ? UiLanguages.Russian : UiLanguages.English;
        if (await _ui.Dialogs.PickXliffLanguagesAsync(sourceLanguage, defaultTarget) is not { } languages)
        {
            return;
        }

        XliffBatchExportResult result;
        try
        {
            result = XliffBatch.Export(project, map.FullPath, folder, languages.Source, languages.Target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportMapToXLIFF"), Loc.T("Dlg_CouldNotWriteTheFile0", ex.Message));
            return;
        }

        _shell.StatusText = Loc.T("Msg_MapExportedToXLIFF0", result.FileCount, folder);
        if (result.Warnings.Count > 0)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExportWarnings"), string.Join("\n", result.Warnings));
        }
    }

    /// <summary>Подставляет переводы из папки, выгруженной «Экспортом карты в XLIFF», в топики проекта.</summary>
    [RelayCommand]
    private async Task ImportXliffBatch()
    {
        var project = _workspace.Project;
        if (project is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportMapFromXLIFF"), Loc.T("Msg_OpenAProjectFolderFirst"));
            return;
        }

        var folder = await _ui.Files.OpenFolderAsync(Loc.T("Msg_FolderWithXLIFFFiles"), project.RootPath);
        if (folder is null)
        {
            return;
        }

        if (!await _ui.Dialogs.ConfirmAsync(Loc.T("Msg_ImportMapFromXLIFF"), Loc.T("Msg_ApplyTranslationsFromXLIFF")))
        {
            return;
        }

        foreach (var pane in _docs.Panes.Values.ToList())
        {
            pane.CommitPendingEdits();
        }

        var result = XliffBatch.Import(project, folder);

        // Тот же хвост, что у рефакторинга: открытые топики — несохранённые и перерисованные, закрытые — сохранены на диск.
        _docs.ApplyRefactorResult(new RefactorResult(0, result.ChangedDocuments));
        project.Scan();
        _hooks.RefreshProjectTree?.Invoke();
        _hooks.RefreshMapTree?.Invoke();

        _shell.StatusText = Loc.T("Msg_TranslationsImportedTopicsChanged", result.ChangedDocuments.Count, result.SegmentCount);
        await _ui.Dialogs.MessageAsync(Loc.T("Msg_ImportFromXLIFFReport"),
            string.Join("\n", result.Report.Concat(result.Warnings.Select(w => "! " + w))));
    }
}
