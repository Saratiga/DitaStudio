using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.ViewModels;

// Контекстное меню карты: свойства, копирование, вырезание и вставка строк.
public partial class MapViewModel
{
    // Буфер карты: копия узла и карта, относительно которой записаны его ссылки.
    private (DitaNode Node, string MapPath)? _clipboard;

    public bool CanPaste => _clipboard is not null;

    /// <summary>«Свойства»: карта открывается с выделенной строкой — её атрибуты на панели «Атрибуты».</summary>
    [RelayCommand]
    private void ShowProperties()
    {
        if (SelectedNode?.Item.Node is { } node && OpenMapPane(mapPath: SelectedNode.Item.MapPath) is { } pane)
        {
            pane.FocusNode(node);
        }
    }

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void Copy()
    {
        if (SelectedNode?.Item.Node is not { Parent: not null } node || node.Name is "map" or "bookmap" || SelectedMap is not { } map)
        {
            return;
        }

        _clipboard = (node.CloneDeep(), map.FullPath);
        OnPropertyChanged(nameof(CanPaste));
        _shell.StatusText = $"Скопировано: {SelectedNode.Title}";
    }

    [RelayCommand(CanExecute = nameof(HasStructureSelection))]
    private void Cut()
    {
        Copy();
        if (_clipboard is not null)
        {
            StructureOperation(EditCommands.Delete, "Вырезание из карты");
        }
    }

    [RelayCommand]
    private void Paste() => PasteAt(Place.After);

    [RelayCommand]
    private void PasteBefore() => PasteAt(Place.Before);

    [RelayCommand]
    private void PasteAsChild() => PasteAt(Place.Child);

    private void PasteAt(Place place)
    {
        var ownerMap = SelectedNode?.Item.MapPath ?? SelectedMap?.FullPath;
        if (_clipboard is not { } clip || ownerMap is null || OpenMapPane(mapPath: ownerMap) is not { } pane)
        {
            return;
        }

        if (!CanInsertAtSelection(pane, clip.Node.Name, place, out var reason))
        {
            _shell.StatusText = reason;
            return;
        }

        pane.PushUndo("Вставка в карту");
        var copy = clip.Node.CloneDeep();
        RebaseHrefs(copy, clip.MapPath, ownerMap);
        InsertAtSelection(pane, copy, place);
        AfterMapEdit(pane);
    }

    /// <summary>Ссылки узла из другой карты пересчитываются от папки новой карты.</summary>
    private static void RebaseHrefs(DitaNode node, string fromMap, string toMap)
    {
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(fromMap)), Path.GetDirectoryName(Path.GetFullPath(toMap)), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var element in node.DescendantsAndSelf().Where(n => n.Kind == NodeKind.Element))
        {
            var href = element.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || RefResolver.IsExternal(href!) || element.GetAttribute("scope") is "external" or "peer")
            {
                continue;
            }

            var hash = href!.IndexOf('#');
            var filePart = hash >= 0 ? href[..hash] : href;
            if (filePart.Length == 0)
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fromMap))!, filePart));
            element.SetAttribute("href", RefResolver.MakeRelative(toMap, full) + (hash >= 0 ? href[hash..] : string.Empty));
        }
    }
}
