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

        var file = await _ui.Files.OpenFileAsync("Выберите изображение",
            new[] { new FileFilter("Изображения", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.bmp"), FileFilter.All },
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
            _shell.StatusText = "Рисунок вставлен: замените название под ним. Подпись «Рисунок N» появится при публикации.";
            _docs.RefreshAllTabTitles();
            return;
        }

        image.SetAttribute("placement", "break");
        if (!pane.Author.InsertInlineNode(image))
        {
            _shell.StatusText = "Поставьте курсор в абзац, куда вставить изображение.";
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
                _shell.StatusText = "Подпись добавлена: при публикации — «Рисунок N» / «Таблица N»; впишите название в заголовок.";
                _docs.RefreshAllTabTitles();
                break;
            case false:
                _shell.StatusText = "Подпись убрана: у этого рисунка (таблицы) подписи и номера не будет.";
                _docs.RefreshAllTabTitles();
                break;
            default:
                _shell.StatusText = "Поставьте курсор в рисунок или таблицу, чтобы добавить или убрать подпись.";
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
            _shell.StatusText = "Поставьте курсор в абзац с изображением (вне рисунка), чтобы оформить его как рисунок.";
            return;
        }

        _shell.StatusText = "Изображение оформлено как рисунок: замените название под ним.";
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
            _shell.StatusText = "Поставьте курсор в текст, куда вставить ссылку.";
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
            _shell.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var oldId = node.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(oldId))
        {
            _shell.StatusText = "У элемента нет id — задайте его в панели «Атрибуты», затем переименовывайте.";
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
        _shell.StatusText = $"id «{oldId}» переименован в «{newId}». Обновлено ссылок: {result.UpdatedReferences}.";
    }

    [RelayCommand]
    private async Task ExtractToConref()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        var project = _workspace.Project;
        if (project is null || pane?.FilePath is null || node is null || node.Parent is null)
        {
            _shell.StatusText = "Поставьте курсор в элемент.";
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
            await _ui.Dialogs.MessageAsync("Вынесение в conref", ex.Message);
            return;
        }

        if (result.UpdatedReferences == 0)
        {
            await _ui.Dialogs.MessageAsync("Вынесение в conref", "Не удалось перенести элемент — проверьте цель.");
            return;
        }

        _docs.ApplyRefactorResult(result);
        if (dialogResult.TargetFile is null)
        {
            project.Scan();
            _hooks.RefreshProjectTree?.Invoke();
        }

        _shell.StatusText = $"Элемент вынесен в conref (id «{dialogResult.ElementId}»).";
    }
}
