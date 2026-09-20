using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Core.Publishing;

/// <summary>Общая фильтрация узлов при публикации — используется и HtmlPublisher, и
/// DocxPublisher, чтобы условная сборка и track changes работали одинаково в HTML и DOCX.</summary>
public static class PublishFilter
{
    /// <summary>Условная фильтрация по props/platform/product/audience/otherprops, плюс track
    /// changes: содержимое, помеченное на удаление (status="deleted"), в итоговую публикацию не
    /// попадает — только в предпросмотр (showTrackedDeletions: true), где его можно принять/отклонить.</summary>
    public static bool IsIncluded(DitaNode node, PublishOptions options, bool showTrackedDeletions = false)
    {
        if (!showTrackedDeletions && TrackChanges.IsDeleted(node))
        {
            return false;
        }

        if (options.ExcludeConditions.Count == 0)
        {
            return true;
        }

        foreach (var (attribute, excluded) in options.ExcludeConditions)
        {
            var value = node.GetAttribute(attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var tokens = value!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens.All(excluded.Contains))
            {
                return false;
            }
        }

        return true;
    }
}
