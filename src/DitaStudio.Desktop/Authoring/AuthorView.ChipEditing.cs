using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Presentation.Authoring;

namespace DitaStudio.Desktop.Authoring;

// Правка атрибутов плашки (изображение, пустая ссылка) во всплывающем окне по щелчку.
public sealed partial class AuthorView
{
    /// <summary>Открытое сейчас окно правки плашки (или null).</summary>
    public ChipEditPopup? ChipEditor { get; private set; }

    /// <summary>
    /// Открывает под <paramref name="anchor"/> окно правки плашки <paramref name="node"/>; false — править у неё нечего.
    /// Запись — одна структурная правка (одна отмена); без изменений в полях модель и отмена не трогаются.
    /// <paramref name="editor"/> — блок, в тексте которого стоит плашка (у плашки-блока — null).
    /// </summary>
    public bool EditChip(DitaNode node, Control anchor, BlockEditor? editor)
    {
        if (!ChipEdit.CanEdit(node) || Document is null)
        {
            return false;
        }

        ChipEditor?.Commit();
        var popup = new ChipEditPopup(anchor, ChipEdit.Title(node), ChipEdit.Fields(node), values => ApplyChipEdit(node, values, editor),
            node.Name == "image" ? BrowseImageAsync : null);
        popup.Cancelled += (_, _) => editor?.FocusEditor(editor.CaretOffset);
        popup.Closed += (_, _) =>
        {
            _root.Children.Remove(popup);
            if (ReferenceEquals(ChipEditor, popup))
            {
                ChipEditor = null;
            }
        };
        _root.Children.Add(popup);
        ChipEditor = popup;
        popup.IsOpen = true;
        return true;
    }

    /// <summary>Выбор файла картинки для поля «Файл»: путь от документа (как пишет «Вставка → Изображение»).</summary>
    private async Task<string?> BrowseImageAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || Document?.FilePath is not { } documentPath)
        {
            return null;
        }

        var start = await storage.TryGetFolderFromPathAsync(new Uri(Path.GetDirectoryName(documentPath)! + Path.DirectorySeparatorChar));
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc.T("Msg_ChooseAnImage"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType(Loc.T("Dlg_Images")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.bmp" } } }
        });
        return files.Count == 1 && files[0].TryGetLocalPath() is { } chosen ? Core.Project.RefResolver.MakeRelative(documentPath, chosen) : null;
    }

    private void ApplyChipEdit(DitaNode node, IReadOnlyDictionary<string, string> values, BlockEditor? editor)
    {
        // Проба на копии: отмена и отметка «изменён» нужны только если что-то действительно поменяется.
        if (!ChipEdit.Apply(node.CloneDeep(), values).Changed)
        {
            return;
        }

        FlushPendingEdits(); // набранный, но ещё не записанный текст входит в снимок отмены
        BeforeStructuralEdit?.Invoke(this, ChipEdit.Title(node));
        var result = ChipEdit.Apply(node, values);
        Modified();
        if (editor is null)
        {
            RebuildAround(node, null);
        }
        else if (result.ChipDissolved)
        {
            RebuildAround(editor.Node, editor.Node);
        }
        else
        {
            editor.TextArea.TextView.Redraw();
        }
    }
}
