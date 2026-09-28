using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;

namespace DitaStudio.Core.Publishing;

/// <summary>
/// Какие топики карты попадают в оглавление издания (DOCX, единый HTML и PDF) — одно правило
/// для всех форматов.
/// </summary>
public static class TocRules
{
    /// <summary>
    /// false — топика в оглавлении нет: у него пустой заголовок (топик без заголовка, например из
    /// одной таблицы).
    /// </summary>
    public static bool Includes(DitaProject project, MapItem item)
    {
        var doc = item.TargetPath is null ? null : project.TryGetDocument(item.TargetPath);
        var topic = doc is null ? null
            : item.TargetTopicId is null ? doc.Root
            : RefResolver.FindById(doc.Root, item.TargetTopicId) ?? doc.Root;
        return topic?.FirstElement("title") is not { } title || !DitaValidator.IsEmptyTitle(title);
    }
}
