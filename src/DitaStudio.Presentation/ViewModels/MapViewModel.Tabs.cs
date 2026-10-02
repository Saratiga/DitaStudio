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

// Вкладки открытых карт: список, выбор, закрытие, забывание карт закрытого проекта.
public partial class MapViewModel
{
    /// <summary>
    /// Обновляет список карт активного проекта и его вкладки карт: исчезнувшие карты закрываются, у проекта без открытых карт открывается
    /// первая, выбирается последняя выбранная карта этого проекта.
    /// </summary>
    public void RefreshMaps()
    {
        var project = _workspace.Project;
        Maps.Clear();
        if (project is null)
        {
            SetSelection(null);
            return;
        }

        foreach (var map in project.Maps)
        {
            Maps.Add(map);
        }

        foreach (var tab in OpenMaps.Where(t => ReferenceEquals(t.Project, project)).ToList())
        {
            if (Maps.FirstOrDefault(m => string.Equals(m.FullPath, tab.File.FullPath, StringComparison.OrdinalIgnoreCase)) is { } current)
            {
                tab.File = current; // после пересканирования файлы проекта — новые объекты
            }
            else
            {
                OpenMaps.Remove(tab);
                _lastTab.Remove(project);
            }
        }

        if (!OpenMaps.Any(t => ReferenceEquals(t.Project, project)) && Maps.Count > 0)
        {
            OpenMaps.Add(new OpenMapTab(project, Maps[0]));
        }

        RefreshMapTitles();
        var target = _lastTab.TryGetValue(project, out var last) && OpenMaps.Contains(last)
            ? last
            : OpenMaps.FirstOrDefault(t => ReferenceEquals(t.Project, project));
        SetSelection(target);
        _hooks.RefreshMapTree?.Invoke();
    }

    // Выбор вкладки и карты одним действием, без обратной реакции обработчиков.
    private void SetSelection(OpenMapTab? tab)
    {
        _syncingTabs = true;
        try
        {
            SelectedMapTab = tab;
            SelectedMap = tab?.File;
            if (tab is not null)
            {
                _lastTab[tab.Project] = tab;
            }
        }
        finally
        {
            _syncingTabs = false;
        }
    }

    private void RefreshMapTitles()
    {
        var several = _workspace.Projects.Count > 1;
        foreach (var tab in OpenMaps)
        {
            tab.Title = several ? $"{tab.File.FileName} · {tab.Project.Name}" : tab.File.FileName;
        }
    }

    partial void OnSelectedMapChanged(ProjectFile? value)
    {
        // Карту выбрали напрямую (список «Открыть карту», команды, тесты): для неё есть вкладка.
        if (!_syncingTabs && value is not null && (_workspace.ProjectOf(value.FullPath) ?? _workspace.Project) is { } owner)
        {
            var tab = OpenMaps.FirstOrDefault(t => ReferenceEquals(t.Project, owner) && string.Equals(t.File.FullPath, value.FullPath, StringComparison.OrdinalIgnoreCase));
            if (tab is null)
            {
                tab = new OpenMapTab(owner, value);
                OpenMaps.Add(tab);
                RefreshMapTitles();
            }

            _syncingTabs = true;
            try
            {
                SelectedMapTab = tab;
                _lastTab[owner] = tab;
            }
            finally
            {
                _syncingTabs = false;
            }
        }

        _hooks.RefreshMapTree?.Invoke();
    }

    partial void OnSelectedMapTabChanged(OpenMapTab? value)
    {
        if (_syncingTabs || value is null)
        {
            return;
        }

        _lastTab[value.Project] = value;
        if (!ReferenceEquals(_workspace.Project, value.Project))
        {
            _workspace.ActivateProject(value.Project); // RefreshMaps выберет эту вкладку
            return;
        }

        _syncingTabs = true;
        try
        {
            SelectedMap = value.File;
        }
        finally
        {
            _syncingTabs = false;
        }

        _hooks.RefreshMapTree?.Invoke();
    }

    /// <summary>Закрывает вкладку карты (сам файл карты остаётся в проекте); выбирается соседняя вкладка или ничего.</summary>
    [RelayCommand]
    private void CloseMap(OpenMapTab? tab)
    {
        if (tab is null || !OpenMaps.Contains(tab))
        {
            return;
        }

        var index = OpenMaps.IndexOf(tab);
        var wasSelected = ReferenceEquals(SelectedMapTab, tab);
        OpenMaps.Remove(tab);
        if (_lastTab.TryGetValue(tab.Project, out var last) && ReferenceEquals(last, tab))
        {
            _lastTab.Remove(tab.Project);
        }

        if (!wasSelected)
        {
            return;
        }

        var next = OpenMaps.Skip(Math.Max(0, index - 1)).FirstOrDefault() ?? OpenMaps.LastOrDefault();
        if (next is null)
        {
            SetSelection(null);
            _hooks.RefreshMapTree?.Invoke();
            _shell.StatusText = $"Карта {tab.File.FileName} закрыта.";
            return;
        }

        if (!ReferenceEquals(next.Project, _workspace.Project))
        {
            _lastTab[next.Project] = next;
            _workspace.ActivateProject(next.Project);
            return;
        }

        SetSelection(next);
        _hooks.RefreshMapTree?.Invoke();
    }

    /// <summary>Закрывает все вкладки карт, кроме выбранной в меню.</summary>
    [RelayCommand]
    private void CloseOtherMaps(OpenMapTab? keep)
    {
        foreach (var tab in OpenMaps.Where(t => !ReferenceEquals(t, keep)).ToList())
        {
            OpenMaps.Remove(tab);
            if (_lastTab.TryGetValue(tab.Project, out var last) && ReferenceEquals(last, tab))
            {
                _lastTab.Remove(tab.Project);
            }
        }

        if (keep is not null)
        {
            if (!ReferenceEquals(keep.Project, _workspace.Project))
            {
                _lastTab[keep.Project] = keep;
                _workspace.ActivateProject(keep.Project);
            }
            else
            {
                SetSelection(keep);
                _hooks.RefreshMapTree?.Invoke();
            }
        }
    }

    /// <summary>Убирает карты закрытого проекта из вкладок.</summary>
    public void ForgetProject(DitaProject project)
    {
        foreach (var tab in OpenMaps.Where(t => ReferenceEquals(t.Project, project)).ToList())
        {
            OpenMaps.Remove(tab);
        }

        _lastTab.Remove(project);
        RefreshMapTitles();
        if (SelectedMapTab is null || ReferenceEquals(SelectedMapTab.Project, project))
        {
            SetSelection(null);
        }
    }
}
