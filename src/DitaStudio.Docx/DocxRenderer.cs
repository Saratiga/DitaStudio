using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

public sealed class DocxRenderOptions
{
    public Labels Labels { get; set; } = Labels.Russian;

    public bool ShowDraftComments { get; set; }

    /// <summary>Условная фильтрация: возвращает false, если элемент нужно исключить.</summary>
    public Func<DitaNode, bool>? Filter { get; set; }

    public bool NumberFiguresAndTables { get; set; } = true;

    public CaptionSeparator CaptionSeparator { get; set; } = CaptionSeparator.Period;

    /// <summary>Список заголовков Word (нумерация включена) — в него же входят нумерованные абзацы; null — нумерации нет.</summary>
    public int? HeadingNumId { get; set; }

    /// <summary>Оформление: стили Word из пользовательского CSS (по умолчанию — встроенное).</summary>
    public DocxStyleSheet Styles { get; set; } = DocxStyleSheet.Default;

    /// <summary>Возвращает имя закладки для топика (путь, id элемента) — на неё ссылаются
    /// перекрёстные ссылки. Null, если целевой топик не входит в публикацию.</summary>
    public Func<string, string?, string?>? TopicBookmark { get; set; }

    public List<string> Warnings { get; } = new();

    /// <summary>Цепочка имён областей ключей (keyscope) для топика, который сейчас рендерится —
    /// см. MapItem.KeyScopeChain. Публикатор обновляет её перед каждым RenderTopic.</summary>
    public IReadOnlyList<string>? CurrentKeyScope { get; set; }

    /// <summary>Связанные топики из таблицы соответствий (reltable) для топика, который сейчас
    /// рендерится — см. MapTree.RelatedLinks. Публикатор обновляет перед каждым RenderTopic.</summary>
    public IReadOnlyList<MapTree.RelatedLink>? RelatedTopics { get; set; }
}

/// <summary>
/// Преобразование DITA в родной OOXML (.docx) — параллельно <see cref="HtmlRenderer"/>, но вместо
/// строк собирает Paragraph/Table для одного документа Word. Сноски — настоящие сноски Word
/// (FootnotesPart), поэтому Word сам кладёт их на ту страницу, где стоит ссылка. Списки — через
/// общий NumberingDefinitionsPart с отдельным numId на каждый независимый список (чтобы нумерация
/// начиналась заново). Покрыт основной словарь DITA, используемый в проекте; редкие
/// специализации (hazardstatement, видео/аудио, coderef, MathML/SVG-контейнер, learning-контент)
/// показываются обобщённым абзацем с текстом — как и в HtmlRenderer для неизвестных элементов.
/// </summary>
public sealed partial class DocxRenderer
{
    private readonly DitaProject _project;
    private readonly MainDocumentPart _mainPart;
    private readonly DocxRenderOptions _options;
    private readonly DitaCatalog _catalog;
    private readonly NumberingDefinitionsPart _numberingPart;

    private DitaDocument _document = null!;
    private int _figureNumber;
    private int _tableNumber;
    private int _nextNumId = 1;
    private int _nextFootnoteId = 1;
    private int _nextBookmarkId = 1;
    private int _nextImageId = 1;
    internal const int BulletAbstractNumId = 1000;
    internal const int DecimalAbstractNumId = 1001;
    private int _nextCustomAbstractId = 2000;
    private readonly Dictionary<DocxListMarker, int> _customBulletAbstracts = new();

    public DocxRenderer(DitaProject project, MainDocumentPart mainPart, NumberingDefinitionsPart numberingPart,
        DocxRenderOptions? options = null)
    {
        _project = project;
        _catalog = project.Catalog;
        _mainPart = mainPart;
        _numberingPart = numberingPart;
        _options = options ?? new DocxRenderOptions();
    }

    private Labels L => _options.Labels;

    private bool Include(DitaNode node) => _options.Filter?.Invoke(node) ?? true;
}
