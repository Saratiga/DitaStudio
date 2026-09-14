using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.Authoring;

/// <summary>Оформление фразовых элементов DITA в режиме «Автор».</summary>
public static class InlineStyles
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    // Читаются из текущей темы при каждой перестройке — см. AuthorView.TagBrush и соседей.
    private static Brush CodeBackground => ThemeManager.Brush("CodeBackground");
    private static Brush LinkBrush => ThemeManager.Brush("Accent");
    private static Brush TermBrush => ThemeManager.Brush("AttributeNameBrush");
    private static Brush KeywordBrush => ThemeManager.Brush("Success");

    /// <summary>Применяет оформление к прогону текста по имени элемента DITA.</summary>
    public static void Apply(Run run, string elementName)
    {
        switch (elementName)
        {
            case "b":
                run.FontWeight = FontWeights.Bold;
                break;
            case "i":
            case "term":
            case "varname":
            case "var":
                run.FontStyle = FontStyles.Italic;
                if (elementName == "term")
                {
                    run.Foreground = TermBrush;
                }

                break;
            case "u":
                run.TextDecorations = TextDecorations.Underline;
                break;
            case "line-through":
                run.TextDecorations = TextDecorations.Strikethrough;
                break;
            case "sup":
                run.BaselineAlignment = BaselineAlignment.Superscript;
                run.FontSize = 10;
                break;
            case "sub":
                run.BaselineAlignment = BaselineAlignment.Subscript;
                run.FontSize = 10;
                break;
            case "tt":
            case "codeph":
            case "synph":
            case "filepath":
            case "systemoutput":
            case "msgph":
            case "msgnum":
            case "parmname":
            case "apiname":
            case "option":
            case "cmdname":
            case "markupname":
            case "xmlatt":
            case "xmlelement":
                run.FontFamily = Mono;
                run.Background = CodeBackground;
                break;
            case "userinput":
                run.FontFamily = Mono;
                run.Background = CodeBackground;
                run.FontWeight = FontWeights.SemiBold;
                break;
            case "uicontrol":
            case "wintitle":
            case "shortcut":
                run.FontWeight = FontWeights.SemiBold;
                break;
            case "keyword":
                run.Foreground = KeywordBrush;
                break;
            case "xref":
            case "link":
                run.Foreground = LinkBrush;
                run.TextDecorations = TextDecorations.Underline;
                break;
            case "q":
            case "cite":
                run.FontStyle = FontStyles.Italic;
                break;
        }
    }

    /// <summary>Фразовые элементы, предлагаемые на панели быстрого форматирования.</summary>
    public static readonly (string Element, string Title, string Gesture)[] QuickInline =
    {
        ("b", "Полужирный", "Ctrl+B"),
        ("i", "Курсив", "Ctrl+I"),
        ("u", "Подчёркнутый", "Ctrl+U"),
        ("codeph", "Код в строке", "Ctrl+`"),
        ("uicontrol", "Элемент интерфейса", "Ctrl+Shift+U"),
        ("filepath", "Путь к файлу", string.Empty),
        ("userinput", "Ввод пользователя", string.Empty),
        ("term", "Термин", string.Empty),
        ("keyword", "Ключевое слово", string.Empty),
        ("cmdname", "Имя команды", string.Empty),
        ("parmname", "Параметр", string.Empty),
        ("varname", "Переменная", string.Empty)
    };

    public static bool IsInlineElement(string name) => DitaCatalog.Default.IsInline(name);
}
