using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Изображения, рисунки, перекрёстные ссылки, id и conref.
public partial class InsertViewModel
{
    [RelayCommand]
    private async Task InsertImage()
    {
        var pane = _docs.Current;
        if (pane is null || pane.FilePath is null)
        {
            return;
        }

        var file = await _ui.Files.OpenFileAsync(Loc.T("Msg_ChooseAnImage"),
            new[] { new FileFilter(Loc.T("Dlg_Images"), "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.bmp"), FileFilter.All },
            Path.GetDirectoryName(pane.FilePath));
        if (file is null)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, file);
        var image = DitaNode.Element("image");
        image.SetAttribute("href", href);

        var alt = DitaNode.Element("alt");
        alt.SetText(Path.GetFileNameWithoutExtension(file));
        image.Add(alt);

        // Рисунок — блок fig с названием: при публикации у него подпись «Рисунок N. Название».
        // Там, где fig недопустим (например, в середине заголовка), изображение идёт в строку.
        if (pane.Author.InsertFigure(image))
        {
            _shell.StatusText = Loc.T("Msg_FigureInsertedReplaceTheTitleUnder");
            _docs.RefreshAllTabTitles();
            return;
        }

        image.SetAttribute("placement", "break");
        if (!pane.Author.InsertInlineNode(image))
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheParagraph");
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    /// <summary>Подпись рисунка или таблицы под курсором: добавить пустую («Рисунок N») или убрать совсем.</summary>
    [RelayCommand]
    private void ToggleCaption()
    {
        if (_docs.Current is not { } pane)
        {
            return;
        }

        switch (pane.Author.ToggleCaption())
        {
            case true:
                _shell.StatusText = Loc.T("Msg_CaptionAddedWhenPublishedFigureN");
                _docs.RefreshAllTabTitles();
                break;
            case false:
                _shell.StatusText = Loc.T("Msg_CaptionRemovedThisFigureTableWill");
                _docs.RefreshAllTabTitles();
                break;
            default:
                _shell.StatusText = Loc.T("Msg_PutTheCursorInAFigure");
                break;
        }
    }

    /// <summary>Изображение из абзаца под курсором — в рисунок с названием и номером.</summary>
    [RelayCommand]
    private void WrapImageAsFigure()
    {
        if (_docs.Current is not { } pane)
        {
            return;
        }

        if (!pane.Author.WrapImageAsFigure())
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAParagraph");
            return;
        }

        _shell.StatusText = Loc.T("Msg_TheImageIsNowAFigure");
        _docs.RefreshAllTabTitles();
    }

    [RelayCommand]
    private async Task InsertXref()
    {
        var pane = _docs.Current;
        var project = _workspace.Project;
        if (project is null || pane is null || pane.FilePath is null)
        {
            return;
        }

        var result = await _ui.Dialogs.InsertXrefAsync(project, pane.FilePath);
        if (result is null)
        {
            return;
        }

        var href = RefResolver.MakeRelative(pane.FilePath, result.File.FullPath);
        if (!string.IsNullOrEmpty(result.TopicId))
        {
            href += "#" + result.TopicId;
        }

        var xref = DitaNode.Element("xref");
        xref.SetAttribute("href", href);
        xref.SetAttribute("format", "dita");
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            xref.SetText(result.Text);
        }

        if (!pane.Author.InsertInlineNode(xref))
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInTheText");
            return;
        }

        _docs.RefreshAllTabTitles();
    }

    [RelayCommand]
    private async Task RenameId()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        var project = _workspace.Project;
        if (project is null || pane?.FilePath is null || node is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAnElement");
            return;
        }

        var oldId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(oldId))
        {
            _shell.StatusText = Loc.T("Msg_TheElementHasNoIdSet");
            return;
        }

        var newId = await _ui.Dialogs.RenameIdAsync(oldId!);
        if (string.IsNullOrWhiteSpace(newId) || newId == oldId)
        {
            return;
        }

        foreach (var p in _docs.Panes.Values)
        {
            p.CommitPendingEdits();
        }

        var result = RefactorService.RenameId(project, pane.FilePath, oldId!, newId!);
        _docs.ApplyRefactorResult(result);
        _shell.StatusText = Loc.T("Msg_Id0RenamedTo1References", oldId, newId, result.UpdatedReferences);
    }

    [RelayCommand]
    private async Task ExtractToConref()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        var project = _workspace.Project;
        if (project is null || pane?.FilePath is null || node is null || node.Parent is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAnElement");
            return;
        }

        var suggestedId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(suggestedId))
        {
            suggestedId = DocumentTemplates.SuggestId(node.InnerText, node.Name);
        }

        var dialogResult = await _ui.Dialogs.ExtractToConrefAsync(project, suggestedId!);
        if (dialogResult is null)
        {
            return;
        }

        foreach (var p in _docs.Panes.Values)
        {
            p.CommitPendingEdits();
        }

        RefactorResult result;
        try
        {
            result = RefactorService.ExtractToConref(
                project, pane.FilePath, node, dialogResult.ElementId,
                dialogResult.TargetFile?.FullPath, dialogResult.NewFileName);
        }
        catch (IOException ex)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExtractToConref"), ex.Message);
            return;
        }

        if (result.UpdatedReferences == 0)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_ExtractToConref"), Loc.T("Msg_CouldNotMoveTheElementCheck"));
            return;
        }

        _docs.ApplyRefactorResult(result);
        if (dialogResult.TargetFile is null)
        {
            project.Scan();
            _hooks.RefreshProjectTree?.Invoke();
        }

        _shell.StatusText = Loc.T("Msg_TheElementWasExtractedToConref", dialogResult.ElementId);
    }
}
