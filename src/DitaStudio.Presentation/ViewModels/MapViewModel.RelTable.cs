using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Templates;
using DitaStudio.Presentation.Services;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

// Таблица соответствий карты.
public partial class MapViewModel
{
    [RelayCommand]
    private async Task EditRelTable()
    {
        var project = _workspace.Project;
        if (project is null || SelectedMap is not { } map)
        {
            return;
        }

        var pane = OpenMapPane();
        if (pane is null)
        {
            return;
        }

        var existing = pane.Document.Root.FirstElement("reltable");
        var rows = await _ui.Dialogs.EditRelTableAsync(project, RelTableConverter.Parse(project, existing, map.FullPath));
        if (rows is null)
        {
            return;
        }

        pane.PushUndo(Loc.T("Dlg_RelationshipTable"));
        var reltable = RelTableConverter.Build(rows, map.FullPath);
        if (existing is not null)
        {
            existing.ReplaceWith(reltable);
        }
        else
        {
            pane.Document.Root.Add(reltable);
        }

        pane.Document.IsDirty = true;
        pane.ReloadViews();
        _documents.RefreshAllTabTitles();
        _shell.StatusText = Loc.T("Msg_TheRelationshipTableWasUpdated");
    }
}
