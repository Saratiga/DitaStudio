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

        pane.PushUndo("Вставка таблицы");
        var table = TableNodeBuilder.Build(options);

        var parent = node.Parent;
        var index = EditCommands.ElementIndexOf(parent, node) + 1;
        if (!DitaCatalog.Default.CanInsert(parent, "table", index))
        {
            _shell.StatusText = "Таблицу здесь вставить нельзя.";
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
                TableBorderMode.All => "Границы таблицы: все линии.",
                TableBorderMode.OuterOnly => "Границы таблицы: только внешняя рамка.",
                TableBorderMode.HorizontalOnly => "Границы таблицы: только горизонтальные линии.",
                _ => "Границы таблицы убраны."
            }
            : "Поставьте курсор в ячейку таблицы (обычной, CALS).";
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

        _shell.StatusText = ok ? (visible ? "Линия под строкой показана." : "Линия под строкой убрана.") : "Поставьте курсор в ячейку таблицы.";
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

        _shell.StatusText = ok ? (visible ? "Линия справа от столбца показана." : "Линия справа от столбца убрана.") : "Поставьте курсор в ячейку таблицы.";
    }

    /// <summary>Выделить целиком таблицу под курсором (контур; затем Delete удаляет её).</summary>
    [RelayCommand]
    private void SelectTable()
    {
        _shell.StatusText = _docs.Current?.Author.SelectCurrentTable() == true
            ? "Таблица выделена: Delete удаляет её целиком, Esc снимает выделение."
            : "Поставьте курсор в ячейку таблицы.";
    }

    /// <summary>Удалить таблицу под курсором целиком.</summary>
    [RelayCommand]
    private void DeleteTable()
    {
        if (_docs.Current?.Author.DeleteCurrentTable() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshOutline?.Invoke();
            _shell.StatusText = "Таблица удалена (отмена — Ctrl+Alt+Z).";
        }
        else
        {
            _shell.StatusText = "Поставьте курсор в ячейку таблицы.";
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
            _shell.StatusText = TableCommands.Describe(operation) + " — выполнено.";
        }
        else
        {
            _shell.StatusText = operation switch
            {
                TableOperation.DeleteRow => "Строку удалить нельзя: в таблице должна остаться хотя бы одна строка.",
                TableOperation.DeleteColumn => "Столбец удалить нельзя: он последний или число столбцов задано типом таблицы.",
                TableOperation.SplitCell => "Ячейка не объединена — делить нечего.",
                TableOperation.InsertColumnLeft or TableOperation.InsertColumnRight => "В этой таблице число столбцов задано её типом.",
                _ => "Поставьте курсор в ячейку таблицы."
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
                _shell.StatusText = "Границы: поставьте курсор в ячейку или выделите ячейки обычной таблицы (CALS).";
                return;
            case false:
                _shell.StatusText = "Края таблицы заданы для всей стороны сразу: выделите всю строку или весь столбец. Линии между ячейками применены.";
                break;
            default:
                _shell.StatusText = "Границы ячеек изменены.";
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
            _shell.StatusText = "Поставьте курсор в ячейку обычной таблицы.";
        }
    }

    /// <summary>Выделяет столбец таблицы, в котором курсор.</summary>
    [RelayCommand]
    private void SelectTableColumn()
    {
        if (_docs.Current?.Author.SelectCurrentColumn() != true)
        {
            _shell.StatusText = "Поставьте курсор в ячейку обычной таблицы.";
        }
    }

    [RelayCommand]
    private void MergeCellRight()
    {
        if (_docs.Current?.Author.MergeCurrentCellRight() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Ячейки объединены по горизонтали.";
        }
        else
        {
            _shell.StatusText = "Выделите ячейку таблицы, у которой есть соседняя справа.";
        }
    }

    [RelayCommand]
    private void MergeCellDown()
    {
        if (_docs.Current?.Author.MergeCurrentCellDown() == true)
        {
            _docs.RefreshAllTabTitles();
            _hooks.RefreshAttributePanel?.Invoke();
            _shell.StatusText = "Ячейки объединены по вертикали.";
        }
        else
        {
            _shell.StatusText = "Выделите ячейку таблицы, у которой есть соседняя снизу.";
        }
    }

    [RelayCommand]
    private void ToggleTablePageBreakAuto()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null || node.Name != "table")
        {
            _shell.StatusText = "Выделите таблицу целиком (не отдельную ячейку).";
            return;
        }

        var enabled = _docs.Current!.Author.ToggleCurrentOutputClass("page-break-auto");
        _docs.RefreshAllTabTitles();
        _hooks.RefreshAttributePanel?.Invoke();
        _shell.StatusText = enabled == true
            ? "Таблица теперь может переноситься на страницы с повтором шапки."
            : "Таблица снова печатается как единый блок.";
    }
}
