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

// Таблицы: вставка, границы, выделение, строки и столбцы, объединение ячеек.
public partial class InsertViewModel
{
    [RelayCommand]
    private async Task InsertTable()
    {
        var pane = _docs.Current;
        var node = pane?.Author.CurrentNode;
        if (pane is null || node?.Parent is null)
        {
            return;
        }

        var options = await _ui.Dialogs.InsertTableAsync();
        if (options is null)
        {
            return;
        }

        pane.PushUndo(Loc.T("Msg_InsertTable"));
        var table = TableNodeBuilder.Build(options);

        var parent = node.Parent;
        var index = EditCommands.ElementIndexOf(parent, node) + 1;
        if (!DitaCatalog.Default.CanInsert(parent, "table", index))
        {
            _shell.StatusText = Loc.T("Msg_ATableCannotBeInsertedHere");
            return;
        }

        parent.Insert(EditCommands.ChildIndexForElementIndex(parent, index), table);
        pane.Document.IsDirty = true;
        pane.Author.Rebuild();
        _docs.RefreshAllTabTitles();
        _hooks.RefreshOutline?.Invoke();
    }

    /// <summary>Границы таблицы под курсором: все, внешняя рамка, горизонтальные, без границ.</summary>
    [RelayCommand]
    private void SetTableBorders(TableBorderMode mode)
    {
        var ok = _docs.Current?.Author.SetTableBorders(mode) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok
            ? mode switch
            {
                TableBorderMode.All => Loc.T("Msg_TableBordersAllLines"),
                TableBorderMode.OuterOnly => Loc.T("Msg_TableBordersOuterFrameOnly"),
                TableBorderMode.HorizontalOnly => Loc.T("Msg_TableBordersHorizontalLinesOnly"),
                _ => Loc.T("Msg_TableBordersRemoved")
            }
            : Loc.T("Msg_PutTheCursorInACell");
    }

    /// <summary>Линия под строкой таблицы, в которой стоит курсор: true — показать, false — убрать.</summary>
    [RelayCommand]
    private void SetRowBorder(bool visible)
    {
        var ok = _docs.Current?.Author.SetRowBorder(visible) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok ? (visible ? Loc.T("Msg_TheLineBelowTheRowIs") : Loc.T("Msg_TheLineBelowTheRowIs2")) : Loc.T("Msg_PutTheCursorInATable");
    }

    /// <summary>Линия справа от столбца, в котором стоит курсор: true — показать, false — убрать.</summary>
    [RelayCommand]
    private void SetColumnBorder(bool visible)
    {
        var ok = _docs.Current?.Author.SetColumnBorder(visible) == true;
        if (ok)
        {
            _docs.RefreshAllTabTitles();
        }

        _shell.StatusText = ok ? (visible ? Loc.T("Msg_TheLineRightOfTheColumn") : Loc.T("Msg_TheLineRightOfTheColumn2")) : Loc.T("Msg_PutTheCursorInATable");
    }

    /// <summary>Выделить целиком таблицу под курсором (контур; затем Delete удаляет её).</summary>
    [RelayCommand]
    private void SelectTable()
    {
        _shell.StatusText = _docs.Current?.Author.SelectCurrentTable() == true
            ? Loc.T("Msg_TableSelectedDeleteRemovesItEntirely")
            : Loc.T("Msg_PutTheCursorInATable");
    }

    /// <summary>Удалить таблицу под курсором целиком.</summary>
    [RelayCommand]
    private void DeleteTable()
    {
        if (_docs.Current?.Author.DeleteCurrentTable() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
            _shell.StatusText = Loc.T("Msg_TableDeletedUndoCtrlAltZ");
        }
        else
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInATable");
        }
    }

    /// <summary>Строки и столбцы таблицы под курсором (меню ячейки, панель инструментов).</summary>
    [RelayCommand]
    private void EditTable(TableOperation operation)
    {
        if (_docs.Current?.Author.EditCurrentTable(operation) == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _hooks.RefreshOutline?.Invoke();
            _shell.StatusText = TableCommands.Describe(operation) + Loc.T("Msg_Done");
        }
        else
        {
            _shell.StatusText = operation switch
            {
                TableOperation.DeleteRow => Loc.T("Msg_TheRowCannotBeDeletedThe"),
                TableOperation.DeleteColumn => Loc.T("Msg_TheColumnCannotBeDeletedIt"),
                TableOperation.SplitCell => Loc.T("Msg_TheCellIsNotMergedNothing"),
                TableOperation.InsertColumnLeft or TableOperation.InsertColumnRight => Loc.T("Msg_InThisTableTheNumberOf"),
                _ => Loc.T("Msg_PutTheCursorInATable")
            };
        }
    }

    /// <summary>
    /// Границы выделенных ячеек (или ячейки под курсором) как в меню «Границы» Word: выбранные стороны показываются, а если все уже есть —
    /// убираются; «Нет границы» убирает все.
    /// </summary>
    [RelayCommand]
    private void SetCellBorders(DitaStudio.Core.Publishing.BorderEdges edges)
    {
        switch (_docs.Current?.Author.SetCellBorders(edges))
        {
            case null:
                _shell.StatusText = Loc.T("Msg_BordersPutTheCursorInA");
                return;
            case false:
                _shell.StatusText = Loc.T("Msg_TableEdgesAreSetForA");
                break;
            default:
                _shell.StatusText = Loc.T("Msg_CellBordersChanged");
                break;
        }

        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
    }

    /// <summary>Выделяет строку таблицы, в которой курсор (прямоугольник ячеек).</summary>
    [RelayCommand]
    private void SelectTableRow()
    {
        if (_docs.Current?.Author.SelectCurrentRow() != true)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInACell2");
        }
    }

    /// <summary>Выделяет столбец таблицы, в котором курсор.</summary>
    [RelayCommand]
    private void SelectTableColumn()
    {
        if (_docs.Current?.Author.SelectCurrentColumn() != true)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInACell2");
        }
    }

    [RelayCommand]
    private void MergeCellRight()
    {
        if (_docs.Current?.Author.MergeCurrentCellRight() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = Loc.T("Msg_CellsMergedHorizontally");
        }
        else
        {
            _shell.StatusText = Loc.T("Msg_SelectATableCellThatHas");
        }
    }

    [RelayCommand]
    private void MergeCellDown()
    {
        if (_docs.Current?.Author.MergeCurrentCellDown() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = Loc.T("Msg_CellsMergedVertically");
        }
        else
        {
            _shell.StatusText = Loc.T("Msg_SelectATableCellThatHas2");
        }
    }

    [RelayCommand]
    private void ToggleTablePageBreakAuto()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "table")
        {
            _shell.StatusText = Loc.T("Msg_SelectTheWholeTableNotA");
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-auto");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? Loc.T("Msg_TheTableCanNowBreakAcross")
            : Loc.T("Msg_TheTablePrintsAsASingle");
    }
}
