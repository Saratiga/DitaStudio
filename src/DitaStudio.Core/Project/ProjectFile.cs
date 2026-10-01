using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

/// <summary>Файл проекта: путь, тип документа, заголовок.</summary>
public sealed class ProjectFile
{
    public ProjectFile(string fullPath, string relativePath, DitaDocumentKind kind, string title)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
        Title = title;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public DitaDocumentKind Kind { get; internal set; }

    public string Title { get; internal set; }

    public string FileName => System.IO.Path.GetFileName(FullPath);

    public string RootElement { get; internal set; } = string.Empty;

    public override string ToString() => RelativePath;
}
