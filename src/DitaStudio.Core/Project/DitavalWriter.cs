using System.Text;

namespace DitaStudio.Core.Project;

/// <summary>Пишет .ditaval из DitavalRules — обратная операция к DitavalReader.Read. Сохраняет
/// оба вида правил (action="exclude" и action="flag"): правка исключений через диалог условий
/// не должна стирать правила подсветки, написанные вручную в том же файле.</summary>
public static class DitavalWriter
{
    public static void Write(string path, DitavalRules rules)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<val>\n");

        foreach (var (attribute, values) in rules.Exclude.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var value in values.OrderBy(v => v, StringComparer.Ordinal))
            {
                sb.Append("  <prop action=\"exclude\" att=\"").Append(Escape(attribute))
                  .Append("\" val=\"").Append(Escape(value)).Append("\"/>\n");
            }
        }

        foreach (var flag in rules.Flags)
        {
            sb.Append("  <prop action=\"flag\" att=\"").Append(Escape(flag.Attribute)).Append('"');
            AppendOptionalAttr(sb, "val", flag.Value);
            AppendOptionalAttr(sb, "color", flag.Color);
            AppendOptionalAttr(sb, "backgroundcolor", flag.BackgroundColor);
            AppendOptionalAttr(sb, "style", flag.Style);
            AppendOptionalAttr(sb, "changebar", flag.ChangeBar);
            sb.Append("/>\n");
        }

        sb.Append("</val>\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendOptionalAttr(StringBuilder sb, string name, string? value)
    {
        if (value is not null)
        {
            sb.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
