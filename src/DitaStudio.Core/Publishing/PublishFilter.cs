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

        if (options.ExcludeConditions.Count == 0 && options.ExcludeUnlistedConditions.Count == 0)
        {
            return true;
        }

        foreach (var attribute in options.ExcludeConditions.Keys.Concat(options.ExcludeUnlistedConditions).Distinct(StringComparer.Ordinal))
        {
            var value = node.GetAttribute(attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue; // элемент без атрибута входит всегда
            }

            options.ExcludeConditions.TryGetValue(attribute, out var excluded);
            var unlisted = options.ExcludeUnlistedConditions.Contains(attribute);
            options.IncludeConditions.TryGetValue(attribute, out var included);

            // Значение исключено: названо в exclude, либо исключены все значения атрибута и это не возвращено через include.
            bool IsExcluded(string token) =>
                excluded?.Contains(token) == true || (unlisted && included?.Contains(token) != true);

            var tokens = value!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens.All(IsExcluded))
            {
                return false;
            }
        }

        return true;
    }
}
