using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>Узел дерева файлов проекта: папка (<see cref="FolderPath"/>) или файл (<see cref="File"/>).</summary>
public sealed partial class ProjectTreeNode : ObservableObject
{
    public ProjectTreeNode(string name, string? folderPath, ProjectFile? file)
    {
        Name = name;
        FolderPath = folderPath;
        File = file;
    }

    public string Name { get; }

    public string? FolderPath { get; }

    public ProjectFile? File { get; }

    /// <summary>Значок по типу документа — тот же, что в WPF-версии.</summary>
    public string Icon => File is null ? string.Empty : File.Kind switch
    {
        DitaDocumentKind.Map => "🗺",
        DitaDocumentKind.Topic => "📄",
        _ => "•"
    };

    /// <summary>Заголовок документа мелким текстом после имени файла.</summary>
    public string Title => File is null ? string.Empty : "  " + File.Title;

    public string ToolTip => File?.RelativePath ?? FolderPath ?? Name;

    public ObservableCollection<ProjectTreeNode> Children { get; } = new();

    [ObservableProperty]
    private bool isExpanded = true;
}

/// <summary>Узел дерева карты — обёртка над <see cref="MapItem"/> для привязки.</summary>
public sealed partial class MapTreeNode : ObservableObject
{
    public MapTreeNode(MapItem item, bool isRoot)
    {
        Item = item;
        Icon = isRoot ? "🗺" : item.IsResourceOnly ? "🔑" : item.TargetPath is null ? "▸" : "📄";
    }

    public MapItem Item { get; }

    public string Icon { get; }

    public string Title => Item.Title;

    public string ElementName => "  " + Item.ElementName;

    public bool IsBroken => Item.IsBroken;

    public ObservableCollection<MapTreeNode> Children { get; } = new();

    [ObservableProperty]
    private bool isExpanded = true;
}

/// <summary>Узел дерева структуры документа (вкладка «Структура»).</summary>
public sealed partial class OutlineNode : ObservableObject
{
    public OutlineNode(DitaNode node, int depth)
    {
        Node = node;
        var text = node.InnerText.Trim();
        Text = text.Length == 0 ? string.Empty : "  " + (text.Length > 42 ? text[..42] + "…" : text);
        isExpanded = depth < 3;
    }

    public DitaNode Node { get; }

    public string Name => Node.Name;

    public string Text { get; }

    public ObservableCollection<OutlineNode> Children { get; } = new();

    [ObservableProperty]
    private bool isExpanded;
}
