using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Project;

namespace DitaStudio.App.ViewModels;

// Список карт проекта (выбор в комбобоксе вкладки «Карта»). Дерево карты
// самой карты (BuildMapTree/BuildMapItem), drag-and-drop, структурные
// операции и таблица соответствий остаются в MainWindow.Map.cs —
// императивное построение WPF-дерева вперемешку с состоянием
// drag-and-drop, тот же класс риска, что SidePanels/дерево проекта.
public partial class MapViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<ProjectFile> Maps { get; } = new();

    [ObservableProperty]
    private ProjectFile? selectedMap;

    public MapViewModel(MainViewModel main)
    {
        _main = main;
    }

    public void RefreshMaps()
    {
        var project = _main.Project;
        Maps.Clear();
        if (project is null)
        {
            return;
        }

        foreach (var map in project.Maps)
        {
            Maps.Add(map);
        }

        SelectedMap = Maps.Count > 0 ? Maps[0] : null;
    }

    partial void OnSelectedMapChanged(ProjectFile? value) => _main.RefreshMapTree?.Invoke();
}
