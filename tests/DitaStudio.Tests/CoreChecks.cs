using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

/// <summary>Итог одного раздела проверок.</summary>
internal sealed record SectionResult(string Title, int Passed, IReadOnlyList<string> Failures, IReadOnlyList<string> Notes);

/// <summary>
/// Проверки ядра и DOCX-экспорта, сгруппированные по разделам. Каждый раздел — тест xUnit
/// в <see cref="CoreTests"/>; запуск — <c>dotnet test tests/DitaStudio.Tests</c>.
/// </summary>
internal static partial class CoreChecks
{
    // Раздел проверок = один тест xUnit (см. CoreTests). Check внутри раздела — «мягкая»
    // проверка: раздел доходит до конца и сообщает все провалы разом, а не только первый,
    // как сделал бы Assert. Состояние статическое, поэтому тесты сборки идут последовательно
    // (CollectionBehavior в CoreTests.cs).
    private static readonly List<string> Failures = new();
    private static readonly List<string> Notes = new();
    private static int _passed;
    private static string _section = string.Empty;

    /// <summary>Выполняет раздел проверок и возвращает его итог.</summary>
    internal static SectionResult Run(Action section)
    {
        Failures.Clear();
        Notes.Clear();
        _passed = 0;
        _section = string.Empty;
        section();
        return new SectionResult(_section, _passed, Failures.ToList(), Notes.ToList());
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            Failures.Add(description);
        }
    }

    /// <summary>Пометка в вывод теста (например, «git недоступен — раздел пропущен»).</summary>
    private static void Note(string text) => Notes.Add(text);

    private static void Section(string title)
    {
        if (_section.Length == 0)
        {
            _section = title;
        }
    }
}
