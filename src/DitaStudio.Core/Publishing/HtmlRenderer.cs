using System.Text;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;

namespace DitaStudio.Core.Publishing;

public sealed class RenderOptions
{
    public Labels Labels { get; set; } = Labels.Russian;

    public bool ShowDraftComments { get; set; }

    /// <summary>Ссылка на топик по абсолютному пути и идентификатору — возвращает href в выходном формате.</summary>
    public Func<string, string?, string>? TopicLink { get; set; }

    /// <summary>Преобразование пути к изображению в src выходного документа.</summary>
    public Func<string, string>? ImageSource { get; set; }

    /// <summary>Условная фильтрация: возвращает false, если элемент нужно исключить.</summary>
    public Func<DitaNode, bool>? Filter { get; set; }

    /// <summary>Правила подсветки (action="flag" из .ditaval) — цвет/фон/начертание по атрибуту.</summary>
    public IReadOnlyList<DitavalFlagRule>? FlagRules { get; set; }

    public bool NumberFiguresAndTables { get; set; } = true;

    /// <summary>Цепочка имён областей ключей (keyscope) для топика, который сейчас рендерится —
    /// см. MapItem.KeyScopeChain. Публикатор обновляет её перед каждым RenderTopic.</summary>
    public IReadOnlyList<string>? CurrentKeyScope { get; set; }

    /// <summary>Связанные топики из таблицы соответствий (reltable) для топика, который сейчас
    /// рендерится — см. MapTree.RelatedLinks. Публикатор обновляет перед каждым RenderTopic.</summary>
    public IReadOnlyList<MapTree.RelatedLink>? RelatedTopics { get; set; }

    /// <summary>Однофайловая сборка: все топики живут в одном HTML, поэтому голый id элемента
    /// (например, note id="warn1") может повторяться в разных топиках. Когда включено, id элементов
    /// и вложенных топиков получают тот же префикс "имяФайла--", что и HtmlPublisher.AnchorFor —
    /// иначе xref на file.dita#topicId/elementId целится в несуществующий якорь.</summary>
    public bool SingleFileAnchors { get; set; }
}

/// <summary>
/// Преобразование DITA в HTML. Обрабатывает базовые типы топиков, домены
/// и таблицы CALS; неизвестные элементы выводятся по их отображению из каталога.
/// </summary>
public sealed partial class HtmlRenderer
{
    private readonly DitaProject _project;
    private readonly RenderOptions _options;
    private readonly DitaCatalog _catalog;

    private DitaDocument _document = null!;
    private List<DitaNode> _footnotes = new();
    private readonly List<(IReadOnlyList<string> Path, string? Href)> _indexTerms = new();
    private string? _currentTopicHref;
    private int _figureNumber;
    private int _tableNumber;

    public HtmlRenderer(DitaProject project, RenderOptions? options = null)
    {
        _project = project;
        _catalog = project.Catalog;
        _options = options ?? new RenderOptions();
    }

    public int FigureNumber
    {
        get => _figureNumber;
        set => _figureNumber = value;
    }

    public int TableNumber
    {
        get => _tableNumber;
        set => _tableNumber = value;
    }

    private Labels L => _options.Labels;

    /// <summary>Отрисовывает один топик. headingLevel = 1 для отдельной страницы.</summary>
    public string RenderTopic(DitaDocument document, DitaNode topic, int headingLevel = 1)
    {
        _document = document;
        _footnotes = new List<DitaNode>();

        var sb = new StringBuilder();
        var def = _catalog.Get(topic.Name);
        var cls = def?.ClassAttr.Contains("concept/concept") == true ? "concept"
            : def?.ClassAttr.Contains("task/task") == true ? "task"
            : def?.ClassAttr.Contains("reference/reference") == true ? "reference"
            : def?.ClassAttr.Contains("troubleshooting/") == true ? "troubleshooting"
            : def?.ClassAttr.Contains("glossentry/") == true ? "glossentry"
            : "topic";

        var id = topic.GetAttribute("id");
        _currentTopicHref = _options.TopicLink?.Invoke(document.FilePath ?? string.Empty, id);
        sb.Append("<article class=\"").Append(cls).Append('"');
        if (!string.IsNullOrEmpty(id))
        {
            sb.Append(" id=\"").Append(Escape(PrefixedId(id!))).Append('"');
        }

        sb.Append(">\n");

        foreach (var child in topic.ElementChildren())
        {
            if (!Include(child))
            {
                continue;
            }

            switch (child.Name)
            {
                case "title":
                case "glossterm":
                    sb.Append('<').Append(H(headingLevel)).Append(OptionalClassAttr(child))
                      .Append('>')
                      .Append(RenderInlineChildren(child))
                      .Append("</").Append(H(headingLevel)).Append(">\n");
                    break;

                case "shortdesc":
                    sb.Append("<p class=\"shortdesc\">").Append(RenderInlineChildren(child)).Append("</p>\n");
                    break;

                case "abstract":
                case "glossdef":
                    sb.Append("<div class=\"abstract\">").Append(RenderChildren(child, headingLevel)).Append("</div>\n");
                    break;

                case "prolog":
                case "titlealts":
                    break;

                case "body":
                case "conbody":
                case "refbody":
                case "taskbody":
                case "troublebody":
                case "glossBody":
                case "learningBasebody":
                case "learningOverviewbody":
                case "learningContentbody":
                case "learningSummarybody":
                case "learningAssessmentbody":
                case "learningPlanbody":
                    sb.Append(RenderChildren(child, headingLevel));
                    break;

                case "related-links":
                    sb.Append(RenderRelatedLinks(child));
                    break;

                default:
                    if (_catalog.Get(child.Name)?.IsTopicType == true)
                    {
                        sb.Append(RenderNestedTopic(document, child, headingLevel + 1));
                    }
                    else
                    {
                        sb.Append(RenderNode(child, headingLevel));
                    }

                    break;
            }
        }

        sb.Append(RenderFootnotes());
        sb.Append(RenderReltableLinks());
        sb.Append("</article>\n");
        return sb.ToString();
    }

    /// <summary>Автоматический блок «Смотрите также» из таблицы соответствий (reltable) —
    /// отдельно от авторского related-links, который топик мог указать в разметке сам.</summary>
    private string RenderReltableLinks()
    {
        if (_options.RelatedTopics is null || _options.RelatedTopics.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("<nav class=\"related-links reltable-links\">\n<h2>")
            .Append(Escape(L.RelatedLinks)).Append("</h2>\n<ul>\n");

        foreach (var link in _options.RelatedTopics)
        {
            var reference = new DitaReference(link.Path, link.TopicId, null, link.Path);
            var title = TitleOf(reference) ?? System.IO.Path.GetFileNameWithoutExtension(link.Path);
            var target = _options.TopicLink?.Invoke(link.Path, link.TopicId) ?? "#";
            sb.Append("<li><a href=\"").Append(Escape(target)).Append("\">")
              .Append(Escape(title)).Append("</a></li>\n");
        }

        sb.Append("</ul>\n</nav>\n");
        return sb.ToString();
    }

    private string RenderNestedTopic(DitaDocument document, DitaNode topic, int level)
    {
        var savedFootnotes = _footnotes;
        var savedHref = _currentTopicHref;
        var savedRelated = _options.RelatedTopics;
        _options.RelatedTopics = null; // связи reltable относятся к topicref карты, а не к вложенным топикам файла
        var html = RenderTopic(document, topic, Math.Min(level, 6));
        _footnotes = savedFootnotes;
        _currentTopicHref = savedHref;
        _options.RelatedTopics = savedRelated;
        return html;
    }

    private static string H(int level) => "h" + Math.Clamp(level, 1, 6);

    private bool Include(DitaNode node) => _options.Filter?.Invoke(node) ?? true;
}
