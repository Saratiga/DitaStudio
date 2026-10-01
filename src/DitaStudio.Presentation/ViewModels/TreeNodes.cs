using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Presentation.ViewModels;

/// <summary>Узел дерева файлов проекта: папка (<see cref="FolderPath"/>) или файл (<see cref="File"/>).</summary>
public sealed partial class ProjectTreeNode : ObservableObject
{
    public ProjectTreeNode(string name, string? folderPath, ProjectFile? file, DitaProject? project = null)
    {
        Name = name;
        FolderPath = folderPath;
        File = file;
        Project = project;
    }

    /// <summary>Проект, к которому относится узел (корень, папка, файл): выбор узла делает этот проект активным.</summary>
    public DitaProject? Project { get; }

    /// <summary>Узел — корень проекта (папка проекта целиком).</summary>
    public bool IsProjectRoot => Project is not null && File is null && string.Equals(FolderPath, Project.RootPath, StringComparison.OrdinalIgnoreCase);

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

/// <summary>Вкладка открытой карты в панели «Карта»: карта и проект, которому она принадлежит.</summary>
public sealed partial class OpenMapTab : ObservableObject
{
    public OpenMapTab(DitaProject project, ProjectFile file)
    {
        Project = project;
        this.file = file;
        title = file.FileName;
    }

    public DitaProject Project { get; }

    [ObservableProperty]
    private ProjectFile file;

    /// <summary>Имя файла карты; при нескольких открытых проектах — с названием проекта («guide.ditamap · Пример»).</summary>
    [ObservableProperty]
    private string title;

    public string ToolTip => File.FullPath;
}

/// <summary>Узел дерева карты — обёртка над <see cref="MapItem"/> для привязки.</summary>
public sealed partial class MapTreeNode : ObservableObject
{
    public MapTreeNode(MapItem item, bool isRoot)
    {
        Item = item;
        IsRoot = isRoot;
        Icon = isRoot ? "🗺" : item.Node.Name == "keydef" ? "🔑" : item.TargetPath is null ? "▸" : "📄";
    }

    public MapItem Item { get; }

    public string Icon { get; }

    public string Title => Item.Title;

    public string ElementName => "  " + Item.ElementName;

    public bool IsBroken => Item.IsBroken;

    /// <summary>Подсказка у строки: у битой — причина и что делать, иначе пусто.</summary>
    public string? ToolTip => Item.IsBroken ? Item.BrokenReason : null;

    public bool IsRoot { get; }

    /// <summary>Флажок «публиковать»: у строк-топиков и разделов, не у корня и keydef.</summary>
    public bool CanExclude => !IsRoot && Item.Node.Name != "keydef";

    /// <summary>Строка попадает в публикацию (флажок в карте установлен).</summary>
    public bool IsPublished => !Item.IsResourceOnly;

    /// <summary>Исключена флажком (сама или веткой выше) — в дереве показывается перечёркнутой.</summary>
    public bool IsExcluded => Item.IsExcluded;

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
