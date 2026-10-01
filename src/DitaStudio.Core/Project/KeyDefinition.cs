using DitaStudio.Core.Model;

namespace DitaStudio.Core.Project;

/// <summary>Определение ключа из карты (keydef или topicref с @keys).</summary>
public sealed class KeyDefinition
{
    public KeyDefinition(string key, string? href, string? resolvedPath, DitaNode source, string sourceMap, string? scope, string? format)
    {
        Key = key;
        Href = href;
        ResolvedPath = resolvedPath;
        Source = source;
        SourceMap = sourceMap;
        Scope = scope;
        Format = format;
    }

    public string Key { get; }

    public string? Href { get; }

    public string? ResolvedPath { get; }

    public DitaNode Source { get; }

    public string SourceMap { get; }

    public string? Scope { get; }

    public string? Format { get; }

    /// <summary>Текст ключа из topicmeta/keywords/keyword — подставляется вместо keyref.</summary>
    public string? KeyText
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            var keywords = meta?.FirstElement("keywords");
            var keyword = keywords?.FirstElement("keyword");
            var text = keyword?.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            var navtitle = meta?.FirstElement("navtitle")?.InnerText.Trim();
            return string.IsNullOrEmpty(navtitle) ? null : navtitle;
        }
    }

    public DitaNode? KeyContent
    {
        get
        {
            var meta = Source.FirstElement("topicmeta");
            return meta?.FirstElement("keywords")?.FirstElement("keyword");
        }
    }

    public override string ToString() => $"{Key} -> {Href}";
}
