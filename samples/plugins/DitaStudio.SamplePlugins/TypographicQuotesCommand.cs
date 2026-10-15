using System.Text;
using DitaStudio.Core.Model;
using DitaStudio.Presentation.Plugins;
using DitaStudio.Presentation.Services;

namespace DitaStudio.SamplePlugins;

/// <summary>
/// Команда режима «Автор»: прямые кавычки "…" во всём открытом документе заменяются «ёлочками». Код, имена файлов и команд не трогаются.
/// Показывает главное для команды: снимок для отмены до правки (<see cref="IDocumentView.PushUndo"/>), правку модели документа и
/// что редактор делает после команды сам (помечает документ изменённым и перестраивает «Автор»).
/// </summary>
public sealed class TypographicQuotesCommand : IAuthorCommandPlugin
{
    private static readonly HashSet<string> CodeElements = new(StringComparer.Ordinal)
    {
        "codeph", "codeblock", "pre", "screen", "filepath", "cmdname", "varname", "parmname", "apiname", "userinput", "systemoutput", "msgph"
    };

    public string Name => "Пример: типографские кавычки «»";

    /// <summary>Сколько кавычек заменено при последнем запуске (для тестов и отладки).</summary>
    public int LastReplaced { get; private set; }

    public void Execute(IDocumentView pane)
    {
        // Снимок ДО правки: «Отменить структурное изменение» (Ctrl+Alt+Z) вернёт документ одним шагом.
        pane.PushUndo(Name);

        LastReplaced = 0;
        var previous = ' '; // символ перед кавычкой — по нему видно, открывающая она или закрывающая
        foreach (var text in pane.Document.Root.Descendants().Where(n => n.Kind == NodeKind.Text).ToList())
        {
            if (text.Ancestors().Any(a => CodeElements.Contains(a.Name)))
            {
                previous = 'x';
                continue;
            }

            var sb = new StringBuilder(text.Value.Length);
            foreach (var c in text.Value)
            {
                if (c == '"')
                {
                    // После пробела, начала текста или открывающей скобки — открывающая, иначе закрывающая.
                    sb.Append(char.IsWhiteSpace(previous) || previous is '(' or '[' or '«' ? '«' : '»');
                    LastReplaced++;
                }
                else
                {
                    sb.Append(c);
                }

                previous = c;
            }

            text.Value = sb.ToString();
        }

        // Ничего больше делать не нужно: после Execute редактор помечает документ изменённым, перестраивает «Автор» и пишет
        // в строке состояния «Команда плагина … выполнена». Сообщить пользователю что-то своё плагин может исключением —
        // его текст редактор покажет в окне (так делают с ошибками, а не с обычным итогом).
    }
}
