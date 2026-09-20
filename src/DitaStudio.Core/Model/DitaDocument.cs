using System.Text;
using System.Xml;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Model;

/// <summary>Тип документа DITA, определённый по корневому элементу.</summary>
public enum DitaDocumentKind
{
    Unknown,
    Topic,
    Map,
    DitaVal
}

/// <summary>
/// Документ DITA: корневой элемент, объявление DOCTYPE и сведения о файле.
/// </summary>
public sealed class DitaDocument
{
    public DitaDocument(DitaNode root)
    {
        Root = root;
    }

    public DitaNode Root { get; set; }

    public string? FilePath { get; set; }

    /// <summary>Публичный идентификатор из DOCTYPE.</summary>
    public string? DoctypePublicId { get; set; }

    public string? DoctypeSystemId { get; set; }

    public string? DoctypeName { get; set; }

    public string XmlEncoding { get; set; } = "UTF-8";

    /// <summary>Узлы, идущие до корневого элемента (комментарии, PI).</summary>
    public List<DitaNode> Prolog { get; } = new();

    public bool IsDirty { get; set; }

    public DitaDocumentKind Kind
    {
        get
        {
            var def = DitaCatalog.Default.Get(Root.Name);
            if (def is null)
            {
                return DitaDocumentKind.Unknown;
            }

            if (def.IsMapType)
            {
                return DitaDocumentKind.Map;
            }

            if (def.IsTopicType)
            {
                return DitaDocumentKind.Topic;
            }

            return Root.Name == "val" ? DitaDocumentKind.DitaVal : DitaDocumentKind.Unknown;
        }
    }

    public string Title
    {
        get
        {
            var t = Root.FirstElement("title") ?? Root.FindDescendant("title");
            var text = t?.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text!;
            }

            return FilePath is null ? Root.Name : System.IO.Path.GetFileName(FilePath);
        }
    }

    public string? Id => Root.GetAttribute("id");

    // ------------------------------------------------------------------ чтение

    public static DitaDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        var doc = Parse(text);
        doc.FilePath = path;
        return doc;
    }

    public static DitaDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null, // внешние DTD не загружаем — работаем по встроенному каталогу
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            MaxCharactersFromEntities = 0
        };

        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        var info = (IXmlLineInfo)reader;

        DitaNode? root = null;
        var doc = new DitaDocument(DitaNode.Element("topic"));
        var stack = new Stack<DitaNode>();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.XmlDeclaration:
                    ReadXmlDeclaration(reader, doc);
                    break;

                case XmlNodeType.DocumentType:
                    ReadDoctype(reader, doc);
                    break;

                case XmlNodeType.Element:
                    ReadElement(reader, info, stack, ref root);
                    break;

                case XmlNodeType.EndElement:
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    ReadText(reader, stack);
                    break;

                case XmlNodeType.Comment:
                    ReadComment(reader, doc, stack, root);
                    break;

                case XmlNodeType.ProcessingInstruction:
                    ReadProcessingInstruction(reader, doc, stack, root);
                    break;
            }
        }

        if (root is null)
        {
            throw new XmlException("В документе нет корневого элемента.");
        }

        doc.Root = root;
        return doc;
    }

    private static void ReadXmlDeclaration(XmlReader reader, DitaDocument doc)
    {
        var enc = reader.GetAttribute("encoding");
        if (!string.IsNullOrEmpty(enc))
        {
            doc.XmlEncoding = enc!;
        }
    }

    private static void ReadDoctype(XmlReader reader, DitaDocument doc)
    {
        doc.DoctypeName = reader.Name;
        doc.DoctypePublicId = reader.GetAttribute("PUBLIC");
        doc.DoctypeSystemId = reader.GetAttribute("SYSTEM");
    }

    private static void ReadElement(XmlReader reader, IXmlLineInfo info, Stack<DitaNode> stack, ref DitaNode? root)
    {
        var el = DitaNode.Element(reader.LocalName);
        el.Line = info.HasLineInfo() ? info.LineNumber : 0;
        el.Column = info.HasLineInfo() ? info.LinePosition : 0;

        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Name.StartsWith("xmlns", StringComparison.Ordinal))
                {
                    continue;
                }

                el.SetAttribute(reader.Name, reader.Value);
            }

            reader.MoveToElement();
        }

        var empty = reader.IsEmptyElement;

        if (stack.Count == 0)
        {
            root ??= el;
        }
        else
        {
            stack.Peek().Add(el);
        }

        if (!empty)
        {
            stack.Push(el);
        }
    }

    private static void ReadText(XmlReader reader, Stack<DitaNode> stack)
    {
        if (stack.Count == 0)
        {
            return;
        }

        var parent = stack.Peek();
        var value = reader.Value;
        if (reader.NodeType is XmlNodeType.Whitespace)
        {
            // Пробельный узел значим только внутри смешанного содержимого.
            var def = DitaCatalog.Default.Get(parent.Name);
            if (def is null || !def.IsMixed)
            {
                return;
            }
        }

        parent.Add(DitaNode.Text(value));
    }

    private static void ReadComment(XmlReader reader, DitaDocument doc, Stack<DitaNode> stack, DitaNode? root)
    {
        var c = DitaNode.Comment(reader.Value);
        if (stack.Count == 0)
        {
            if (root is null)
            {
                doc.Prolog.Add(c);
            }
        }
        else
        {
            stack.Peek().Add(c);
        }
    }

    private static void ReadProcessingInstruction(XmlReader reader, DitaDocument doc, Stack<DitaNode> stack, DitaNode? root)
    {
        var pi = DitaNode.Pi(reader.Name, reader.Value);
        if (stack.Count == 0)
        {
            if (root is null)
            {
                doc.Prolog.Add(pi);
            }
        }
        else
        {
            stack.Peek().Add(pi);
        }
    }

    // ------------------------------------------------------------------ запись

    public void Save(string? path = null)
    {
        var target = path ?? FilePath ?? throw new InvalidOperationException("Не задан путь к файлу.");
        var dir = System.IO.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(target, ToXmlString(), new UTF8Encoding(false));
        FilePath = target;
        IsDirty = false;
    }

    public string ToXmlString()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"").Append(XmlEncoding).Append("\"?>\n");

        var doctypeName = DoctypeName ?? Root.Name;
        var pub = DoctypePublicId ?? DitaCatalog.Default.PublicIdFor(Root.Name);
        var sys = DoctypeSystemId ?? DitaCatalog.Default.SystemIdFor(Root.Name);
        if (pub is not null && sys is not null)
        {
            sb.Append("<!DOCTYPE ").Append(doctypeName)
              .Append(" PUBLIC \"").Append(pub).Append("\" \"").Append(sys).Append("\">\n");
        }

        foreach (var n in Prolog)
        {
            XmlSerializer.Write(n, sb, 0);
            sb.Append('\n');
        }

        XmlSerializer.Write(Root, sb, 0);
        sb.Append('\n');
        return sb.ToString();
    }
}
