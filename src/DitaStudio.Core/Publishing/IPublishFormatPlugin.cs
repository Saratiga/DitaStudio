using DitaStudio.Core.Project;

namespace DitaStudio.Core.Publishing;

/// <summary>Контракт плагина формата публикации (помимо встроенных HTML/DOCX/PDF) — см.
/// IValidationRulePlugin для соглашений загрузки.</summary>
public interface IPublishFormatPlugin
{
    string Name { get; }

    /// <summary>Расширение выходного файла без точки, например "epub".</summary>
    string FileExtension { get; }

    void Publish(DitaProject project, string mapPath, PublishOptions options, string outputPath);
}
