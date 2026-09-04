using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.Authoring;

/// <summary>Оформление фразовых элементов DITA в режиме «Автор».</summary>
public static class InlineStyles
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7));
    private static readonly Brush LinkBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA9));
    private static readonly Brush TermBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x3E, 0x8C));
    private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));

    static InlineStyles()
    {
        CodeBackground.Freeze();
        LinkBrush.Freeze();
        TermBrush.Freeze();
        KeywordBrush.Freeze();
    }

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
