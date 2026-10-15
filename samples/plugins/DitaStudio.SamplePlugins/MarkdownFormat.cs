using System.Text;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.SamplePlugins;

/// <summary>
/// Формат публикации: вся карта одним файлом Markdown — для README, вики или передачи текста туда, где DITA не читают.
/// Показывает, как плагин публикации обходит карту (<see cref="MapTree"/>), подставляет conref и ключи и учитывает условия сборки
/// (<see cref="PublishOptions"/> приходит из редактора уже с отмеченными в «Условиях сборки» исключениями).
/// </summary>
public sealed class MarkdownFormat : IPublishFormatPlugin
{
    public string Name => "Пример: Markdown (один файл)";

    public string FileExtension => "md";

    public void Publish(DitaProject project, string mapPath, PublishOptions options, string outputPath)
    {
        // Дерево карты без исключённых условиями строк — как у встроенных форматов.
        var tree = MapTree.Build(project, mapPath, node => PublishFilter.IsIncluded(node, options));
        var writer = new Writer(project, options, Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        writer.Line("# " + tree.Root.Title).Blank();

        // Топики и разделы без своего топика (topichead) — в порядке карты.
        foreach (var item in tree.PublicationOrderWithHeadings)
        {
            var level = Math.Clamp(item.Level + 1, 2, 6); // «#» — название карты, главы — «##»
            if (item.IsHeadingOnly)
            {
                writer.Line(new string('#', level) + " " + item.Title).Blank();
                continue;
            }

            if (project.TryGetDocument(item.TargetPath!) is not { } document)
            {
                continue;
            }

            // conref подставлены, ключи — в контексте области ключей строки карты.
            var expanded = RefResolver.ExpandConrefs(project, document);
            writer.Topic(expanded.Root, level, item.KeyScopeChain, Path.GetDirectoryName(item.TargetPath!)!);
        }

        // Запись через временный файл: сбой посреди записи не оставит обрезанный результат.
        AtomicFile.WriteAllText(outputPath, writer.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Перевод топика в Markdown: самые частые блоки и фразы DITA, остальное — просто текстом.</summary>
    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private readonly DitaProject _project;
        private readonly PublishOptions _options;
        private readonly string _outputDirectory;
        private IReadOnlyList<string>? _scope;
        private string _topicDirectory = string.Empty;

        public Writer(DitaProject project, PublishOptions options, string outputDirectory)
        {
            _project = project;
            _options = options;
            _outputDirectory = outputDirectory;
        }

        public Writer Line(string text)
        {
            _sb.Append(text).Append('\n');
            return this;
        }

        public Writer Blank() => Line(string.Empty);

        public override string ToString() => _sb.ToString().TrimEnd() + "\n";

        public void Topic(DitaNode topic, int level, IReadOnlyList<string> scope, string topicDirectory)
        {
            _scope = scope;
            _topicDirectory = topicDirectory;
            foreach (var child in Included(topic))
            {
                switch (child.Name)
                {
                    case "title":
                    case "glossterm":
                        Line(new string('#', level) + " " + Inline(child)).Blank();
                        break;
                    case "shortdesc":
                        Line("_" + Inline(child) + "_").Blank();
                        break;
                    case "glossdef":
                        Line(Inline(child)).Blank();
                        break;
                    case "prolog":
                    case "related-links":
                    case "glossBody":
                        break;
                    default:
                        // Каталог элементов проекта (с подключённым DTD) знает, что элемент — топик.
                        if (_project.Catalog.Get(child.Name)?.IsTopicType == true)
                        {
                            Topic(child, Math.Min(level + 1, 6), scope, topicDirectory); // вложенный топик
                        }
                        else
                        {
                            Blocks(child, level, string.Empty);
                        }

                        break;
                }
            }
        }

        /// <summary>Дочерние элементы, которые входят в публикацию по условиям сборки.</summary>
        private IEnumerable<DitaNode> Included(DitaNode node) =>
            node.ElementChildren().Where(n => PublishFilter.IsIncluded(n, _options));

        private void Blocks(DitaNode node, int level, string indent)
        {
            switch (node.Name)
            {
                case "p":
                case "cmd":
                case "stepresult":
                case "info":
                case "context":
                case "result":
                case "prereq":
                case "postreq":
                    if (Included(node).Any(IsBlock))
                    {
                        foreach (var child in Included(node))
                        {
                            Blocks(child, level, indent);
                        }
                    }
                    else
                    {
                        Line(indent + Inline(node)).Blank();
                    }

                    break;
                case "section":
                case "example":
                    if (node.FirstElement("title") is { } sectionTitle)
                    {
                        Line(new string('#', Math.Min(level + 1, 6)) + " " + Inline(sectionTitle)).Blank();
                    }

                    foreach (var child in Included(node).Where(c => c.Name != "title"))
                    {
                        Blocks(child, level, indent);
                    }

                    break;
                case "ul":
                case "ol":
                case "steps":
                case "steps-unordered":
                case "substeps":
                    var number = 1;
                    foreach (var item in Included(node))
                    {
                        var marker = node.Name is "ol" or "steps" or "substeps" ? $"{number++}. " : "- ";
                        ListItem(item, level, indent, marker);
                    }

                    Blank();
                    break;
                case "note":
                    var label = node.GetAttribute("type") switch
                    {
                        "tip" => "Совет",
                        "important" => "Важно",
                        "caution" or "warning" or "attention" => "Внимание",
                        "danger" => "Опасно",
                        _ => "Примечание"
                    };
                    Line(indent + "> **" + label + ".** " + Inline(node)).Blank();
                    break;
                case "codeblock":
                case "pre":
                case "screen":
                    Line(indent + "```").Line(node.InnerText.Trim('\n', '\r')).Line(indent + "```").Blank();
                    break;
                case "fig":
                    foreach (var image in node.Descendants().Where(n => n.Name == "image"))
                    {
                        Line(indent + Image(image));
                    }

                    if (node.FirstElement("title") is { } figTitle)
                    {
                        Line(indent + "_Рисунок: " + Inline(figTitle) + "_");
                    }

                    Blank();
                    break;
                case "table":
                case "simpletable":
                case "properties":
                case "choicetable":
                    Table(node, indent);
                    break;
                case "dl":
                case "parml":
                    // Термин и определение: dl (dt/dd) и список параметров parml (pt/pd).
                    foreach (var entry in Included(node))
                    {
                        Line(indent + "**" + Inline(entry.FirstElement("dt") ?? entry.FirstElement("pt") ?? entry) + "**  ");
                        Line(indent + Inline(entry.FirstElement("dd") ?? entry.FirstElement("pd") ?? entry)).Blank();
                    }

                    break;
                case "title":
                    // Название блока без своего уровня заголовка (решение в устранении неполадки и т. п.).
                    Line(indent + "**" + Inline(node) + "**").Blank();
                    break;
                default:
                    // Контейнер блоков — по блокам; текст с фразовыми элементами (stepxmp, pd…) — одной строкой.
                    if (Included(node).Any(IsBlock))
                    {
                        foreach (var child in Included(node))
                        {
                            Blocks(child, level, indent);
                        }
                    }
                    else if (node.InnerText.Trim().Length > 0)
                    {
                        Line(indent + Inline(node)).Blank();
                    }

                    break;
            }
        }

        /// <summary>Блок, а не фраза в строке текста: так считает каталог элементов проекта (с элементами подключённого DTD).</summary>
        private bool IsBlock(DitaNode node) => _project.Catalog.Get(node.Name)?.IsInline != true;

        private void ListItem(DitaNode item, int level, string indent, string marker)
        {
            // Шаг: команда — текстом пункта, остальное — вложенными строками.
            var head = item.Name == "step" ? item.FirstElement("cmd") : null;
            var children = Included(item).ToList();
            if (head is null && !children.Any(IsBlock))
            {
                Line(indent + marker + Inline(item));
                return;
            }

            Line(indent + marker + (head is not null ? Inline(head) : string.Empty));
            var inner = indent + new string(' ', marker.Length);
            // Пояснение, пример, результат шага, таблица выбора, вложенный список — блоками с отступом пункта.
            foreach (var child in children.Where(c => !ReferenceEquals(c, head)))
            {
                Blocks(child, level, inner);
            }
        }

        private void Table(DitaNode table, string indent)
        {
            if (table.FirstElement("title") is { } title)
            {
                Line(indent + "**" + Inline(title) + "**").Blank();
            }

            // Строки таблицы любого вида: CALS row, simpletable strow/sthead, choicetable chrow/chhead…
            var rows = table.Descendants()
                .Where(n => n.Name is "row" or "strow" or "sthead" or "chrow" or "chhead" or "property" or "prophead")
                .Where(n => PublishFilter.IsIncluded(n, _options))
                .Select(r => r.ElementChildren().Select(c => Inline(c).Replace("|", "\\|")).ToList())
                .Where(r => r.Count > 0)
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }

            var columns = rows.Max(r => r.Count);
            foreach (var (row, index) in rows.Select((r, i) => (r, i)))
            {
                Line(indent + "| " + string.Join(" | ", row.Concat(Enumerable.Repeat(string.Empty, columns - row.Count))) + " |");
                if (index == 0)
                {
                    Line(indent + "|" + string.Concat(Enumerable.Repeat(" --- |", columns)));
                }
            }

            Blank();
        }

        /// <summary>Текст с фразовым оформлением: b → **…**, i → _…_, код → `…`, ссылки, ключи, картинки.</summary>
        private string Inline(DitaNode node)
        {
            var sb = new StringBuilder();
            foreach (var child in node.Children)
            {
                if (child.Kind == NodeKind.Text)
                {
                    sb.Append(child.Value);
                    continue;
                }

                if (child.Kind != NodeKind.Element || !PublishFilter.IsIncluded(child, _options) || child.Name is "fn" or "indexterm" or "draft-comment")
                {
                    continue;
                }

                // Пустой элемент с ключом (<keyword keyref="product"/>) — текст ключа.
                var text = child.Children.Count == 0 && child.HasAttribute("keyref")
                    ? _project.TextWithKeys(child, _scope)
                    : Inline(child);
                sb.Append(child.Name switch
                {
                    "b" or "uicontrol" or "wintitle" or "term" => "**" + text + "**",
                    "i" or "cite" or "varname" => "_" + text + "_",
                    "codeph" or "filepath" or "cmdname" or "parmname" or "apiname" or "userinput" or "systemoutput" or "msgph" or "option" =>
                        "`" + text + "`",
                    "menucascade" => string.Join(" → ", child.ElementChildren().Select(c => "**" + Inline(c) + "**")),
                    "xref" or "link" => Link(child, text),
                    "image" => Image(child),
                    _ => text
                });
            }

            // Переносы строк и отступы разметки — одним пробелом: в Markdown перенос внутри абзаца ничего не значит.
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static string Link(DitaNode xref, string text)
        {
            var href = xref.GetAttribute("href") ?? string.Empty;
            var external = xref.GetAttribute("scope") == "external" || href.Contains("://", StringComparison.Ordinal);
            var label = text.Length > 0 ? text : href;
            // Внешняя ссылка — настоящая ссылка Markdown; ссылка на топик — просто текстом (в едином файле адресов топиков нет).
            return external ? $"[{label}]({href})" : label;
        }

        private string Image(DitaNode image)
        {
            var href = image.GetAttribute("href") ?? string.Empty;
            var alt = image.FirstElement("alt")?.InnerText.Trim() ?? image.GetAttribute("alt") ?? string.Empty;
            // Путь картинки — относительно файла Markdown, а не топика.
            var full = Path.GetFullPath(Path.Combine(_topicDirectory, href));
            var relative = Path.GetRelativePath(_outputDirectory, full).Replace('\\', '/');
            return $"![{alt}]({relative})";
        }
    }
}
