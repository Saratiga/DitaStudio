namespace DitaStudio.Docx.Styling;

public enum DocxStyleKind
{
    Paragraph,
    Character
}

/// <summary>Именованный стиль Word, который ставит экспорт, и селекторы HTML-публикации,
/// которые на него переводятся.</summary>
public sealed record DocxStyleDef(
    string Id,
    string Name,
    DocxStyleKind Kind,
    string? BasedOn,
    DocxStyleProps Defaults,
    IReadOnlyList<string> Selectors,
    int? OutlineLevel = null);

/// <summary>
/// Каталог стилей DOCX-экспорта. Оформление по умолчанию повторяет прежний экспорт (до поддержки
/// CSS), селекторы — те, которыми HTML-публикация размечает те же элементы: правило CSS,
/// написанное для сайта, так же меняет и документ Word.
/// </summary>
public static class DocxStyleCatalog
{
    // Особые цели — не стили Word, а параметры таблиц и пометки изменений (rev).
    public const string TableTarget = "@table";
    public const string HeaderCellTarget = "@th";
    public const string BodyCellTarget = "@td";
    public const string RevTarget = "@rev";

    public const string Normal = "Normal";
    public const string BodyText = "BodyText";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";
    public const string TitleAuthor = "TitleAuthor";
    public const string TocHeading = "TOCHeading";
    public const string Shortdesc = "Shortdesc";
    public const string Note = "Note";
    public const string NoteTip = "NoteTip";
    public const string NoteImportant = "NoteImportant";
    public const string NoteWarning = "NoteWarning";
    public const string NoteDanger = "NoteDanger";
    public const string NoteLabel = "NoteLabel";
    public const string CodeBlock = "CodeBlock";
    public const string Quote = "Quote";
    public const string DraftComment = "DraftComment";
    public const string BlockTitle = "BlockTitle";
    public const string GeneratedTitle = "GeneratedTitle";
    public const string ListItem = "ListItem";
    public const string StepCommand = "StepCommand";
    public const string StepInfo = "StepInfo";
    public const string DefinitionTerm = "DefinitionTerm";
    public const string Definition = "Definition";
    public const string FigureCaption = "FigureCaption";
    public const string TableCaption = "TableCaption";
    public const string TableText = "TableText";
    public const string TableHeading = "TableHeading";
    public const string RelatedLinksTitle = "RelatedLinksTitle";
    public const string RelatedLink = "RelatedLink";
    public const string FootnoteText = "FootnoteText";
    public const string PageHeader = "Header";
    public const string PageFooter = "Footer";
    public const string CodeChar = "CodeChar";
    public const string UiControl = "UiControl";
    public const string Term = "Term";
    public const string Hyperlink = "Hyperlink";
    public const string FootnoteReference = "FootnoteReference";

    public static string Heading(int level) => "Heading" + Math.Clamp(level, 1, 6);

    /// <summary>Префикс стилей заголовков «без номера» (не нумеруются, не попадают в оглавление).</summary>
    public const string HeadingPlainPrefix = "HeadingPlain";

    public static string HeadingPlain(int level) => HeadingPlainPrefix + Math.Clamp(level, 1, 6);

    /// <summary>Стиль строки оглавления уровня <paramref name="level"/>.</summary>
    public static string Toc(int level) => "TOC" + Math.Clamp(level, 1, 6);

    public static readonly IReadOnlyList<DocxStyleDef> Styles = Build();

    private static readonly Dictionary<string, DocxStyleDef> ById =
        Styles.ToDictionary(s => s.Id, StringComparer.Ordinal);

    public static DocxStyleDef? Get(string id) => ById.GetValueOrDefault(id);

    /// <summary>Нормализованный селектор → цели (стили и особые цели).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SelectorTargets = BuildSelectorMap();

    /// <summary>Классы служебных частей HTML-сайта (оглавление, навигация) — правил для них в
    /// документе Word нет, и предупреждать о них бессмысленно.</summary>
    public static readonly IReadOnlySet<string> HtmlOnlyClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "toc", "layout", "pager", "breadcrumbs", "index-terms", "chapter-heading", "toc-inline",
        "unknown-element", "footnotes-list", "head", "current"
    };

    /// <summary>Селекторы, от которых стилю абзаца достаются только наследуемые свойства текста:
    /// поля, рамки и фон body или ячейки в Word превратились бы в оформление каждого абзаца.</summary>
    public static readonly IReadOnlySet<string> InheritOnlySelectors = new HashSet<string>(StringComparer.Ordinal)
    {
        "body", "html", ":root", "main", "article", "table", ".table", ".simpletable",
        "th", "td", "thead td", "thead th", ".entry", ".stentry"
    };

    public static bool IsInheritedProperty(string property) =>
        property.StartsWith("font", StringComparison.Ordinal) || property is
            "color" or "line-height" or "text-align" or "text-indent" or "letter-spacing" or
            "text-transform" or "visibility" or "text-decoration" or "text-decoration-line";

    private static DocxStyleProps P(Action<DocxStyleProps> init)
    {
        var p = new DocxStyleProps();
        init(p);
        return p;
    }

    private static DocxBorder GrayLeft => new("single", 1.5, "999999");

    private static List<DocxStyleDef> Build()
    {
        var list = new List<DocxStyleDef>
        {
            // «Обычный» — база всех стилей: сюда идут только наследуемые свойства текста body.
            // Абзацы <p> — отдельный стиль, чтобы поля и выравнивание p не расползались на
            // заголовки, ячейки и титул (в HTML правило для p их тоже не касается).
            new(Normal, "Normal", DocxStyleKind.Paragraph, null, new DocxStyleProps(),
                new[] { "body", "html", "main", "article", ":root" }),
            new(BodyText, "Body Text", DocxStyleKind.Paragraph, Normal, new DocxStyleProps(),
                new[] { "p", ".p" }),
            new(Title, "Title", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 28; p.Bold = true; p.SpaceAfterPt = 12; }),
                new[] { ".doc-title" }),
            new(Subtitle, "Subtitle", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 16; p.Color = "595959"; p.SpaceAfterPt = 12; }),
                new[] { ".doc-subtitle" }),
            new(TitleAuthor, "Автор на титуле", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 12; p.SpaceBeforePt = 24; }),
                new[] { ".doc-author" }),
            new(TocHeading, "TOC Heading", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 16; p.Bold = true; p.SpaceAfterPt = 12; p.KeepNext = true; }),
                new[] { ".doc-toc-title" })
        };

        var headingSizes = new[] { 18.0, 16, 14, 12, 11, 11 };
        for (var level = 1; level <= 6; level++)
        {
            var size = headingSizes[level - 1];
            list.Add(new DocxStyleDef(Heading(level), "heading " + level, DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.FontSizePt = size; p.KeepNext = true; p.SpaceBeforePt = 12; p.SpaceAfterPt = 6; }),
                new[] { "h" + level },
                OutlineLevel: level - 1));
        }

        for (var level = 1; level <= 6; level++)
        {
            list.Add(new DocxStyleDef(HeadingPlain(level), $"Заголовок {level} без номера", DocxStyleKind.Paragraph, Heading(level),
                new DocxStyleProps(), new[] { $"h{level}.nonumber" }));
        }

        // Строки оглавления: имена «toc N» — встроенные стили Word, их же он берёт при обновлении поля.
        for (var level = 1; level <= 6; level++)
        {
            var indent = (level - 1) * 11.35;
            list.Add(new DocxStyleDef(Toc(level), "toc " + level, DocxStyleKind.Paragraph, Normal,
                P(p => { p.IndentLeftPt = indent; p.SpaceAfterPt = 3; }),
                new[] { ".doc-toc-" + level }));
        }

        list.AddRange(new DocxStyleDef[]
        {
            new(Shortdesc, "Краткое описание", DocxStyleKind.Paragraph, Normal,
                P(p => p.Italic = true), new[] { ".shortdesc", ".abstract" }),
            new(Note, "Примечание", DocxStyleKind.Paragraph, Normal,
                P(p => { p.BorderLeft = GrayLeft; p.IndentLeftPt = 11.35; }),
                new[] { ".note" }),
            new(NoteTip, "Примечание: совет", DocxStyleKind.Paragraph, Note, new DocxStyleProps(),
                new[] { ".note.tip", ".note.fastpath" }),
            new(NoteImportant, "Примечание: важно", DocxStyleKind.Paragraph, Note, new DocxStyleProps(),
                new[] { ".note.important", ".note.remember", ".note.restriction" }),
            new(NoteWarning, "Примечание: предупреждение", DocxStyleKind.Paragraph, Note, new DocxStyleProps(),
                new[] { ".note.caution", ".note.attention", ".note.warning", ".note.notice" }),
            new(NoteDanger, "Примечание: опасность", DocxStyleKind.Paragraph, Note, new DocxStyleProps(),
                new[] { ".note.danger" }),
            new(NoteLabel, "Метка примечания", DocxStyleKind.Character, null,
                P(p => p.Bold = true), new[] { ".note .label", ".note-label", ".note>.label" }),
            new(CodeBlock, "Блок кода", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontFamily = "Consolas"; p.Background = "F2F2F2"; }),
                new[] { "pre", ".codeblock", ".screen", ".msgblock", ".pre", ".lines" }),
            new(Quote, "Quote", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Italic = true; p.IndentLeftPt = 11.35; }), new[] { "blockquote", ".lq" }),
            new(DraftComment, "Черновой комментарий", DocxStyleKind.Paragraph, Normal,
                P(p => p.Italic = true), new[] { ".draft-comment" }),
            new(BlockTitle, "Заголовок блока", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.KeepNext = true; }), new[] { ".block-title" }),
            new(GeneratedTitle, "Служебный заголовок", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.SpaceBeforePt = 8; p.KeepNext = true; }), new[] { ".generated-title" }),
            new(ListItem, "Элемент списка", DocxStyleKind.Paragraph, Normal,
                P(p => { p.SpaceBeforePt = 0; p.SpaceAfterPt = 0; }),
                new[] { "li", ".li", ".sli", ".choice" }),
            new(StepCommand, "Команда шага", DocxStyleKind.Paragraph, ListItem, new DocxStyleProps(),
                new[] { ".cmd", ".step .cmd", ".step>.cmd", ".steps>li" }),
            new(StepInfo, "Пояснение к шагу", DocxStyleKind.Paragraph, Normal, new DocxStyleProps(),
                new[] { ".info", ".stepresult", ".stepxmp", ".steptroubleshooting", ".tutorialinfo",
                        ".step .info", ".step .stepresult", ".step .stepxmp" }),
            new(DefinitionTerm, "Термин списка", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.KeepNext = true; }), new[] { "dt", ".dt", ".pt", ".dlhead" }),
            new(Definition, "Определение", DocxStyleKind.Paragraph, Normal,
                P(p => p.IndentLeftPt = 11.35), new[] { "dd", ".dd", ".pd" }),
            new(FigureCaption, "Подпись рисунка", DocxStyleKind.Paragraph, Normal,
                P(p => p.Italic = true), new[] { ".fig-title", "figcaption", ".figcap" }),
            new(TableCaption, "Подпись таблицы", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.KeepNext = true; }), new[] { ".table-title", "caption" }),
            new(TableText, "Текст таблицы", DocxStyleKind.Paragraph, Normal,
                P(p => { p.SpaceBeforePt = 0; p.SpaceAfterPt = 0; }),
                new[] { "td", ".entry", ".stentry" }),
            new(TableHeading, "Шапка таблицы", DocxStyleKind.Paragraph, TableText, new DocxStyleProps(),
                new[] { "th", "thead td", "thead th" }),
            new(RelatedLinksTitle, "Заголовок «Смотрите также»", DocxStyleKind.Paragraph, Normal,
                P(p => { p.Bold = true; p.KeepNext = true; }),
                new[] { ".related-links h2", ".related-links>h2", ".related-links .title", ".related-links-title" }),
            new(RelatedLink, "Ссылка «Смотрите также»", DocxStyleKind.Paragraph, Normal,
                P(p => p.IndentLeftPt = 11.35), new[] { ".related-links li", ".related-links ul li", ".related-link" }),
            new(FootnoteText, "footnote text", DocxStyleKind.Paragraph, Normal,
                P(p => p.FontSizePt = 9), new[] { ".footnotes", ".fn" }),
            new(PageHeader, "header", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 9; p.Color = "595959"; p.SpaceBeforePt = 0; p.SpaceAfterPt = 0; }),
                new[] { ".doc-header" }),
            new(PageFooter, "footer", DocxStyleKind.Paragraph, Normal,
                P(p => { p.FontSizePt = 9; p.Color = "595959"; p.SpaceBeforePt = 0; p.SpaceAfterPt = 0; }),
                new[] { ".doc-footer" }),
            new(CodeChar, "Код в тексте", DocxStyleKind.Character, null,
                P(p => p.FontFamily = "Consolas"),
                new[] { "code", "kbd", "samp", "tt", ".codeph", ".tt", ".synph", ".filepath", ".userinput",
                        ".systemoutput", ".varname", ".parmname", ".apiname", ".option", ".cmdname", ".msgnum",
                        ".msgph", ".shortcut", ".coderef" }),
            new(UiControl, "Элемент интерфейса", DocxStyleKind.Character, null,
                P(p => p.Bold = true), new[] { ".uicontrol", ".wintitle", ".menucascade>.uicontrol" }),
            new(Term, "Термин", DocxStyleKind.Character, null, new DocxStyleProps(), new[] { ".term", "dfn" }),
            new(Hyperlink, "Hyperlink", DocxStyleKind.Character, null, new DocxStyleProps(),
                new[] { "a", ".xref", ".link", "a:link" }),
            new(FootnoteReference, "footnote reference", DocxStyleKind.Character, null,
                P(p => p.VerticalAlign = "superscript"), new[] { ".fn-ref" })
        });

        return list;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildSelectorMap()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string selector, string target)
        {
            if (!map.TryGetValue(selector, out var targets))
            {
                targets = new List<string>();
                map[selector] = targets;
            }

            if (!targets.Contains(target))
            {
                targets.Add(target);
            }
        }

        foreach (var style in Styles)
        {
            foreach (var selector in style.Selectors)
            {
                Add(selector, style.Id);
            }
        }

        // table: рамки — параметры таблицы, шрифт и прочее — текст ячеек
        Add("table", TableTarget);
        Add("table", TableText);
        Add(".table", TableTarget);
        Add(".table", TableText);
        Add(".simpletable", TableTarget);
        Add(".simpletable", TableText);
        Add("th", HeaderCellTarget);
        Add("thead td", HeaderCellTarget);
        Add("thead th", HeaderCellTarget);
        Add("td", BodyCellTarget);
        Add(".entry", BodyCellTarget);
        Add(".stentry", BodyCellTarget);
        Add(".rev-changed", RevTarget);

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }
}
