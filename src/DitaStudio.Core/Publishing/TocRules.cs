using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Какие топики карты попадают в оглавление издания (DOCX, единый HTML и PDF) — одно правило
/// для всех форматов.
/// </summary>
public static class TocRules
{
    /// <summary>Класс заголовка «без номера и не в оглавлении».</summary>
    public const string NoNumberClass = "nonumber";

    /// <summary>
    /// false — топика в оглавлении нет: у него пустой заголовок (топик без заголовка, например из
    /// одной таблицы), заголовок помечен «без номера» (<c>outputclass="nonumber"</c>) или строка
    /// карты (либо её предок) — <c>toc="no"</c>.
    /// </summary>
    public static bool Includes(DitaProject project, MapItem item)
    {
        if (IsHiddenInMap(item.Node))
        {
            return false;
        }

        var doc = item.TargetPath is null ? null : project.TryGetDocument(item.TargetPath);
        var topic = doc is null ? null
            : item.TargetTopicId is null ? doc.Root
            : RefResolver.FindById(doc.Root, item.TargetTopicId) ?? doc.Root;
        return topic?.FirstElement("title") is not { } title || !DitaValidator.IsEmptyTitle(title) && !IsUnnumbered(title);
    }

    /// <summary>Заголовок помечен «без номера» — не нумеруется и не попадает в оглавление.</summary>
    public static bool IsUnnumbered(DitaNode title) =>
        (title.GetAttribute("outputclass") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(NoNumberClass);

    /// <summary>toc="no" у строки карты или её предка (атрибут наследуется по карте).</summary>
    public static bool IsHiddenInMap(DitaNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.GetAttribute("toc") is { } toc)
            {
                return toc == "no";
            }
        }

        return false;
    }
}
