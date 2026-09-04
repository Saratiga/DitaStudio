using System.Text;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Model;

/// <summary>
/// Сериализация DOM в XML. Блочные элементы печатаются с отступами,
/// смешанное содержимое (p, title, ph и т.п.) — «как есть», чтобы не появлялись
/// лишние пробелы в тексте.
/// </summary>
public static class XmlSerializer
{
    public const string Indent = "  ";

    public static string ToXml(DitaNode node)
    {
        var sb = new StringBuilder();
        Write(node, sb, 0);
        return sb.ToString();
    }

    public static void Write(DitaNode node, StringBuilder sb, int level)
    {
        switch (node.Kind)
        {
            case NodeKind.Text:
                sb.Append(EscapeText(node.Value));
                return;

            case NodeKind.Comment:
                sb.Append("<!--").Append(node.Value).Append("-->");
                return;

            case NodeKind.ProcessingInstruction:
                sb.Append("<?").Append(node.Name);
                if (!string.IsNullOrEmpty(node.Value))
                {
                    sb.Append(' ').Append(node.Value);
                }

                sb.Append("?>");
                return;
        }

        sb.Append('<').Append(node.Name);
        foreach (var a in node.Attributes)
        {
            sb.Append(' ').Append(a.Name).Append("=\"").Append(EscapeAttribute(a.Value)).Append('"');
        }

        if (node.Children.Count == 0)
        {
            sb.Append("/>");
            return;
        }

        sb.Append('>');

        if (IsInlineContainer(node))
        {
            foreach (var c in node.Children)
            {
                Write(c, sb, level);
            }
        }
        else
        {
            foreach (var c in node.Children)
            {
                if (c.Kind == NodeKind.Text && string.IsNullOrWhiteSpace(c.Value))
                {
                    continue;
                }

                sb.Append('\n');
                AppendIndent(sb, level + 1);
                Write(c, sb, level + 1);
            }

            sb.Append('\n');
            AppendIndent(sb, level);
        }

        sb.Append("</").Append(node.Name).Append('>');
    }

    /// <summary>Печатать содержимое элемента в одну строку?</summary>
    private static bool IsInlineContainer(DitaNode node)
    {
        var def = DitaCatalog.Default.Get(node.Name);
        if (def is not null)
        {
            return def.IsMixed;
        }

        // Неизвестный элемент: если среди детей есть непустой текст — считаем смешанным.
        foreach (var c in node.Children)
        {
            if (c.Kind == NodeKind.Text && !string.IsNullOrWhiteSpace(c.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendIndent(StringBuilder sb, int level)
    {
        for (var i = 0; i < level; i++)
        {
            sb.Append(Indent);
        }
    }

    public static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    public static string EscapeAttribute(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\n':
                    sb.Append("&#10;");
                    break;
                case '\t':
                    sb.Append("&#9;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
