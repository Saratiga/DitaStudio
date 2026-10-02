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

// Файлы строк карты: открытие, битые ссылки, переименование, удаление, ссылки на файл.
public partial class MapViewModel
{
    /// <summary>Строка ссылается на файл, которого нет (красная в дереве).</summary>
    public bool SelectedIsBroken => SelectedNode?.IsBroken == true;

    /// <summary>
    /// Двойной щелчок: открывает топик узла. У «битой» строки (файла нет) карту молча не открывает, а объясняет причину и
    /// предлагает исправить: создать файл по ссылке, выбрать другой файл или убрать строку. Если топика нет и строка не
    /// битая (раздел), открывается сама карта.
    /// </summary>
    [RelayCommand]
    private async Task OpenSelected()
    {
        if (SelectedNode?.Item is { IsBroken: true })
        {
            await FixBrokenAsync();
        }
        else if (SelectedNode?.Item.TargetPath is { } target && File.Exists(target))
        {
            _documents.OpenDocument(target);
        }
        else if (SelectedMap is { } map)
        {
            _documents.OpenDocument(map.FullPath);
        }
    }

    private const string ChooseCreate = "Создать файл по ссылке";
    private const string ChooseReplace = "Выбрать другой файл…";
    private const string ChooseRemove = "Убрать строку из карты";

    /// <summary>Диалог исправления «битой» строки: причина ошибки и три способа её убрать.</summary>
    private async Task FixBrokenAsync()
    {
        if (SelectedNode?.Item is not { IsBroken: true } item)
        {
            return;
        }

        var options = new List<string>();
        if (item.TargetPath is not null && !File.Exists(item.TargetPath))
        {
            options.Add(ChooseCreate);
        }

        options.Add(ChooseReplace);
        options.Add(ChooseRemove);
        var choice = await _ui.Dialogs.PickOneAsync("Топик не найден", item.BrokenReason + "\n\nЧто сделать со строкой «" + item.Title + "»?", options, o => o);
        switch (choice)
        {
            case ChooseCreate:
                await CreateMissingFileAsync();
                break;
            case ChooseReplace:
                await ReplaceFileAsync();
                break;
            case ChooseRemove:
                await DeleteCommand.ExecuteAsync(null);
                break;
        }
    }

    /// <summary>Создаёт пустой топик по ссылке битой строки (файла по <c>href</c> нет) и открывает его.</summary>
    [RelayCommand(CanExecute = nameof(SelectedIsBroken))]
    private async Task CreateMissingFileAsync()
    {
        var project = _workspace.Project;
        if (project is null || SelectedNode?.Item is not { IsBroken: true, TargetPath: { } target } item || File.Exists(target))
        {
            await _ui.Dialogs.MessageAsync("Создание файла", "Файл по ссылке создать нельзя: у строки нет пути к файлу (ключ не определён). Выберите другой файл.");
            return;
        }

        // Название строки в карте (navtitle) — заголовок нового топика; без него — имя файла словами.
        var title = item.Node.GetAttribute("navtitle") is { Length: > 0 } navAttribute ? navAttribute
            : item.Node.FirstElement("topicmeta")?.FirstElement("navtitle")?.InnerText.Trim() is { Length: > 0 } navtitle ? navtitle
            : Path.GetFileNameWithoutExtension(target).Replace('_', ' ').Replace('-', ' ');
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            DocumentTemplates.Create("topic", title, DocumentTemplates.SuggestId(title, "topic")).Save(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _ui.Dialogs.MessageAsync("Создание файла", $"Не удалось создать {target}: {ex.Message}");
            return;
        }

        project.AddFile(target);
        RebuildTree();
        _hooks.RefreshProjectTree?.Invoke();
        _shell.StatusText = $"Создан файл по ссылке: {Path.GetFileName(target)}.";
        _documents.OpenDocument(target);
    }

    /// <summary>Заменяет ссылку битой строки на выбранный файл проекта (<c>href</c> пересчитывается от файла карты).</summary>
    [RelayCommand(CanExecute = nameof(SelectedIsBroken))]
    private async Task ReplaceFileAsync()
    {
        var project = _workspace.Project;
        if (project is null || SelectedNode?.Item is not { IsBroken: true } item)
        {
            return;
        }

        var file = await _ui.Files.OpenFileAsync("Выберите файл для строки «" + item.Title + "»",
            new[] { new FileFilter("Топики и карты DITA", "*.dita", "*.xml", "*.ditamap"), FileFilter.All }, project.RootPath);
        if (file is null)
        {
            return;
        }

        var ownerMap = item.MapPath;
        StructureOperation(node =>
        {
            node.SetAttribute("href", RefResolver.MakeRelative(ownerMap, file));
            node.RemoveAttribute("keyref");
            return true;
        }, "Замена файла строки карты");
        _shell.StatusText = $"Строка «{item.Title}» теперь ссылается на {Path.GetFileName(file)}.";
    }

    /// <param name="activate">false — карта открывается во вкладке, но вкладка не выбирается: правка
    /// вроде флажка «публиковать» не должна уводить пользователя от документа, над которым он работает.</param>
    /// <param name="mapPath">Файл карты, в котором лежит правимая строка: у строк вложенной карты
    /// (<c>mapref</c>) это файл вложенной карты, а не выбранной. Правка идёт в его документ — иначе
    /// изменился бы узел, которого нет в открытой карте, и правка пропала бы. null — выбранная карта.</param>
    private IDocumentView? OpenMapPane(bool activate = true, string? mapPath = null)
    {
        if (SelectedMap is not { } map)
        {
            return null;
        }

        var previous = _documents.SelectedTab;
        var pane = _documents.OpenDocument(mapPath ?? map.FullPath);
        if (!activate && previous is not null)
        {
            _documents.SelectedTab = previous;
        }

        return pane;
    }

    /// <summary>Правка карты открывает её во вкладке: изменения видны, отменяются и сохраняются
    /// как обычные правки документа.</summary>
    private void AfterMapEdit(IDocumentView pane)
    {
        pane.Document.IsDirty = true;
        pane.ReloadViews();
        _documents.RefreshAllTabTitles();
        RebuildTree();
    }

    /// <summary>Все ссылки проекта на файл выбранной строки — на вкладку «Поиск».</summary>
    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private void FindReferences()
    {
        if (_workspace.Project is not { } project || SelectedNode?.Item.TargetPath is not { } target)
        {
            NoFileMessage();
            return;
        }

        foreach (var pane in _documents.Panes.Values)
        {
            pane.CommitPendingEdits();
        }

        var hits = project.FindReferencesTo(target);
        _search.ShowResults(hits);
        _shell.BottomTabIndex = 1;
        _shell.StatusText = $"Ссылок на {Path.GetFileName(target)}: {hits.Count}";
    }

    /// <summary>Команда, которой нужен файл, запущена на строке без файла (не из меню — там она
    /// недоступна): объясняем, а не молчим.</summary>
    private void NoFileMessage() =>
        _shell.StatusText = SelectedNode is null
            ? "Выберите строку карты."
            : $"У строки «{SelectedNode.Title}» нет файла. Чтобы убрать её из карты, выберите «Убрать из карты».";

    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private async Task RenameFile()
    {
        if (SelectedFile is { } file)
        {
            await _fileOps.MoveFileAsync(file);
            RebuildTree();
        }
    }

    /// <summary>
    /// «Удалить файл»: убирает **выбранную** строку карты, а файл топика удаляет с диска, только когда эта строка была последней
    /// ссылкой на него (в этой карте и в других). Если на файл ссылаются другие строки (например, после «Дублировать»), уходит одна
    /// выбранная — остальные строки и файл остаются.
    /// </summary>
    [RelayCommand(CanExecute = nameof(SelectedHasFile))]
    private async Task DeleteFile()
    {
        if (_workspace.Project is not { } project || SelectedFile is not { } file || SelectedMap is not { } map)
        {
            NoFileMessage();
            return;
        }

        var target = Path.GetFullPath(file.FullPath);
        var pane = OpenMapPane();
        var selectedRow = SelectedNode?.Item.Node;
        var rows = pane is null
            ? new List<DitaNode>()
            : pane.Document.Root.DescendantsAndSelf()
                .Where(n => n.Kind == NodeKind.Element && n.GetAttribute("href") is { } href && !RefResolver.IsExternal(href) &&
                            RefResolver.Parse(map.FullPath, href).Path is { } path &&
                            string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                .ToList();
        var references = project.FindReferencesTo(target);
        var otherMapRows = references.Count(h => h.File.Kind == DitaDocumentKind.Map && h.Node.GetAttribute("href") is not null &&
                                                 !string.Equals(h.File.FullPath, map.FullPath, StringComparison.OrdinalIgnoreCase));
        var otherRowsHere = rows.Count(r => !ReferenceEquals(r, selectedRow));
        var remaining = otherRowsHere + otherMapRows;
        var rowTitle = SelectedNode?.Title ?? file.RelativePath;

        if (remaining > 0)
        {
            // Дубликат или несколько строк на один файл: убираем только выбранную, файл остаётся.
            if (!await _ui.Dialogs.ConfirmAsync("Убрать строку из карты",
                    $"Строка «{rowTitle}» будет убрана из карты. Файл {file.RelativePath} остаётся на диске: ссылок на него из карт — ещё {remaining}. " +
                    "Файл будет удалён, когда вы уберёте последнюю ссылку на него командой «Удалить файл…»."))
            {
                return;
            }

            if (pane is not null && selectedRow is not null && rows.Contains(selectedRow))
            {
                pane.PushUndo("Удаление строки из карты");
                RemoveMapRows(new[] { selectedRow });
                AfterMapEdit(pane);
            }

            RebuildTree();
            _shell.StatusText = $"Строка «{rowTitle}» убрана из карты; файл {file.RelativePath} оставлен — ссылок на него из карт — ещё {remaining}.";
            return;
        }

        var elsewhere = references
            .Where(h => !string.Equals(h.File.FullPath, map.FullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var warning = elsewhere.Count == 0
            ? string.Empty
            : $"\n\nЕщё {elsewhere.Count} ссыл. в других файлах ({string.Join(", ", elsewhere.Select(h => h.File.RelativePath).Distinct().Take(5))}) станут битыми.";
        if (!await _ui.Dialogs.ConfirmAsync("Удаление файла",
                $"Удалить файл {file.RelativePath} с диска? Отменить это будет нельзя. Строки этой карты, которые на него ссылаются, будут убраны.{warning}"))
        {
            return;
        }

        if (pane is not null && rows.Count > 0)
        {
            pane.PushUndo("Удаление файла из карты");
            RemoveMapRows(rows);
            AfterMapEdit(pane);
        }

        if (_fileOps.DeleteFile(file) is { } error)
        {
            await _ui.Dialogs.MessageAsync("Удаление файла", error);
            return;
        }

        RebuildTree();
        _shell.StatusText = $"Файл {file.RelativePath} удалён.";
    }

    /// <summary>Убирает строки карты; дочерние строки удаляемой не теряются — поднимаются на её место.</summary>
    private static void RemoveMapRows(IEnumerable<DitaNode> rows)
    {
        foreach (var row in rows)
        {
            var parent = row.Parent!;
            var index = parent.IndexOf(row);
            foreach (var child in row.ElementChildren().Where(c => c.Name is not "topicmeta").ToList())
            {
                child.RemoveSelf();
                parent.Insert(++index, child);
            }

            row.RemoveSelf();
        }
    }
}
