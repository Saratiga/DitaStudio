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

// Внешние DTD: разбор ATTLIST, загрузка каталога (включая property-based).
internal static partial class CoreChecks
{
    internal static void DtdAttributeListParserTests()
    {
        Section("DtdAttributeListParser: типы атрибутов (по отчёту покрытия)");

        var attrs = DtdAttributeListParser.Parse(
            "req CDATA #REQUIRED " +
            "fixedval CDATA #FIXED \"const\" " +
            "single IDREF #IMPLIED " +
            "multi IDREFS #IMPLIED " +
            "tok NMTOKEN #IMPLIED " +
            "toks NMTOKENS #IMPLIED " +
            "kind NOTATION (gif|jpeg) #IMPLIED " +
            "status (obsolete|deprecated) \"deprecated\" " +
            "plain CDATA 'single-quoted default'"
        ).ToDictionary(a => a.Name);

        Check(attrs.Count == 9, $"разобраны все атрибуты одного ATTLIST: {attrs.Count}");

        Check(attrs["req"] is { Type: AttrType.CData, Required: true, DefaultValue: null }, "#REQUIRED помечает атрибут обязательным без значения по умолчанию");
        Check(attrs["fixedval"] is { Required: false, DefaultValue: "const" }, "#FIXED \"значение\" даёт зафиксированное значение по умолчанию");
        Check(attrs["single"].Type == AttrType.IdRef, "IDREF распознан");
        Check(attrs["multi"].Type == AttrType.IdRef, "IDREFS распознан как тот же AttrType.IdRef");
        Check(attrs["tok"].Type == AttrType.NmToken, "NMTOKEN распознан");
        Check(attrs["toks"].Type == AttrType.NmToken, "NMTOKENS распознан как тот же AttrType.NmToken");
        Check(attrs["kind"].Type == AttrType.CData, "NOTATION (a|b) сведён к CDATA — сама нотация не нужна каталогу");
        Check(attrs["status"] is { Type: AttrType.Enumeration } s && s.Values.SequenceEqual(new[] { "obsolete", "deprecated" }) && s.DefaultValue == "deprecated",
            "перечисление и его значение по умолчанию (двойные кавычки) разобраны");
        Check(attrs["plain"].DefaultValue == "single-quoted default", "значение по умолчанию в одинарных кавычках тоже разбирается");

        var empty = DtdAttributeListParser.Parse(string.Empty);
        Check(empty.Count == 0, "пустой текст ATTLIST — пустой список атрибутов, не падает");

        var danglingName = DtdAttributeListParser.Parse("onlyname");
        Check(danglingName.Count == 0, "имя атрибута без типа/умолчания (оборванный текст) не добавляется как половинчатая запись");
    }

    internal static void DtdCatalogLoaderTests()
    {
        Section("Загрузка внешнего DTD");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // Проверяем сразу три механизма настоящего DTD: параметрическую сущность как текстовую
            // подстановку внутри ATTLIST (%common-atts;), внешний файл через ENTITY SYSTEM, и голую
            // %entity; верхнего уровня, раскрывающуюся в целые ELEMENT/ATTLIST — именно так реальные
            // модульные DTD DITA подключают домены специализации.
            File.WriteAllText(Path.Combine(root, "widget.mod"), """
<!ENTITY % widget-def '<!ELEMENT widget (#PCDATA)><!ATTLIST widget %common-atts; class CDATA "+ topic/ph custom-d/widget ">'>
%widget-def;
""");
            File.WriteAllText(Path.Combine(root, "custom.dtd"), """
<!ENTITY % common-atts "id ID #IMPLIED">
<!ENTITY % widget-mod SYSTEM "widget.mod">
%widget-mod;
<!ELEMENT root (widget+)>
<!ATTLIST root class CDATA "- topic/topic ">
""");

            var result = DtdCatalogLoader.Load(Path.Combine(root, "custom.dtd"));

            Check(result.Warnings.Count == 0, "загрузка без предупреждений: " + string.Join("; ", result.Warnings));
            Check(result.Elements.Count == 2, $"найдено два элемента (root, widget): {result.Elements.Count}");

            var widget = result.Elements.FirstOrDefault(e => e.Name == "widget");
            Check(widget is not null, "widget из внешнего файла (ENTITY SYSTEM) найден");
            Check(widget!.ClassAttr == "+ topic/ph custom-d/widget ", $"@class у widget прочитан из ATTLIST: '{widget.ClassAttr}'");
            Check(widget.Display == DisplayKind.Inline, $"@class 'topic/ph' даёт DisplayKind.Inline: {widget.Display}");
            Check(widget.Attributes.ContainsKey("id"), "атрибут id из %common-atts; попал в widget (раскрытие параметрической сущности в ATTLIST)");
            Check(widget.Model.AllowsText(), "контент-модель widget (#PCDATA) разобрана существующим ModelParser");

            var rootDef = result.Elements.FirstOrDefault(e => e.Name == "root");
            Check(rootDef is not null, "root найден");
            Check(rootDef!.ClassAttr == "- topic/topic ", $"@class у root: '{rootDef.ClassAttr}'");
            Check(rootDef.Display == DisplayKind.Topic, $"@class 'topic/topic' даёт DisplayKind.Topic: {rootDef.Display}");
            Check(rootDef.Model.CollectNames().Contains("widget"), "контент-модель root (widget+) ссылается на widget");

            // Merge — на отдельном экземпляре каталога, не на общем Default, чтобы не влиять на
            // остальные проверки; встроенные элементы при этом не теряются.
            var mergedCatalog = DitaCatalog.Load(CatalogSource.All);
            mergedCatalog.Merge(result.Elements);
            Check(mergedCatalog.Get("widget") is not null, "после Merge внешний элемент доступен через каталог");
            Check(mergedCatalog.Get("p") is not null, "после Merge встроенные элементы каталога никуда не делись");

            var missingWarnings = DtdCatalogLoader.Load(Path.Combine(root, "no-such-file.dtd"));
            Check(missingWarnings.Elements.Count == 0 && missingWarnings.Warnings.Count == 1,
                "загрузка несуществующего файла не падает, а даёт предупреждение и пустой результат");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    internal static void DtdReaderMiscTests()
    {
        Section("DtdReader: PUBLIC-сущности, циклы, повреждённый синтаксис (по отчёту покрытия)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // --- PUBLIC-сущность: путь после публичного идентификатора должен подключиться так же,
            // как и SYSTEM (успешный случай раньше не проверялся вовсе — только SYSTEM).
            File.WriteAllText(Path.Combine(root, "public-module.mod"), "<!ELEMENT pubEl (#PCDATA)>");
            File.WriteAllText(Path.Combine(root, "public.dtd"), """
<!ENTITY % pubmod PUBLIC "-//Example//ELEMENTS Public Module//EN" "public-module.mod">
%pubmod;
<!ELEMENT root (pubEl)>
""");
            var publicResult = DtdCatalogLoader.Load(Path.Combine(root, "public.dtd"));
            Check(publicResult.Warnings.Count == 0, "PUBLIC-сущность с системным путём подключается без предупреждений: " + string.Join("; ", publicResult.Warnings));
            Check(publicResult.Elements.Any(e => e.Name == "pubEl"), "элемент из PUBLIC-подключённого модуля найден");

            // --- PUBLIC без системного пути вовсе (только публичный идентификатор, распространённо
            // для настоящих каталогов OASIS) — не падает, просто ничего не подключает.
            File.WriteAllText(Path.Combine(root, "public-no-system.dtd"), """
<!ENTITY % nopath PUBLIC "-//Example//ELEMENTS No System//EN">
<!ELEMENT root2 (#PCDATA)>
""");
            var noSystemResult = DtdCatalogLoader.Load(Path.Combine(root, "public-no-system.dtd"));
            Check(noSystemResult.Elements.Any(e => e.Name == "root2"), "чтение продолжается после PUBLIC без системного пути (сам файл не пострадал)");

            // --- Циклическое SYSTEM-подключение: a.dtd подключает b.dtd, b.dtd подключает обратно a.dtd.
            File.WriteAllText(Path.Combine(root, "cycle-a.dtd"), """
<!ENTITY % incB SYSTEM "cycle-b.dtd">
%incB;
<!ELEMENT fromA (#PCDATA)>
""");
            File.WriteAllText(Path.Combine(root, "cycle-b.dtd"), """
<!ENTITY % incA SYSTEM "cycle-a.dtd">
%incA;
<!ELEMENT fromB (#PCDATA)>
""");
            var cycleResult = DtdCatalogLoader.Load(Path.Combine(root, "cycle-a.dtd"));
            Check(cycleResult.Elements.Select(e => e.Name).OrderBy(n => n).SequenceEqual(new[] { "fromA", "fromB" }),
                $"циклическое SYSTEM-подключение не зацикливается, оба элемента найдены: [{string.Join(",", cycleResult.Elements.Select(e => e.Name))}]");

            // --- SYSTEM-путь с символом, недопустимым для Path.GetFullPath (embedded NUL — .NET
            // Core почти не проверяет спецсимволы вроде '|' в путях, в отличие от старого .NET
            // Framework, поэтому только NUL надёжно вызывает исключение) — ReadFile ловит его и
            // превращает в предупреждение, не падает.
            File.WriteAllText(Path.Combine(root, "bad-path.dtd"),
                "<!ENTITY % badinc SYSTEM \"bad\0name.mod\">\n%badinc;\n<!ELEMENT ok (#PCDATA)>\n");
            var badPathResult = DtdCatalogLoader.Load(Path.Combine(root, "bad-path.dtd"));
            Check(badPathResult.Warnings.Any(w => w.Contains("Некорректный путь")), "SYSTEM-путь с недопустимым символом даёт предупреждение, а не падает");
            Check(badPathResult.Elements.Any(e => e.Name == "ok"), "остальной файл после сломанного SYSTEM-подключения читается нормально");

            // --- Обрывки синтаксиса: пустые ELEMENT/ATTLIST, недопустимая форма имени, обычная
            // (не параметрическая) сущность, стрей-символ между декларациями, незакрытая кавычка.
            File.WriteAllText(Path.Combine(root, "malformed.dtd"), """
<!ENTITY nbsp "&#160;">
<!ELEMENT>
<!ATTLIST>
<!ELEMENT (a|b) (#PCDATA)>
x
<!ELEMENT good (#PCDATA)>
<!ATTLIST good id ID #IMPLIED>
""");
            var malformedResult = DtdCatalogLoader.Load(Path.Combine(root, "malformed.dtd"));
            Check(malformedResult.Elements.Count == 1 && malformedResult.Elements[0].Name == "good",
                $"обрывки синтаксиса (пустой ELEMENT/ATTLIST, групповое имя, обычная сущность, стрей-символ) пропущены, валидный ELEMENT дальше по файлу разобран: [{string.Join(",", malformedResult.Elements.Select(e => e.Name))}]");
            Check(malformedResult.Elements[0].Attributes.ContainsKey("id"), "ATTLIST после обрывков всё ещё корректно привязался к своему элементу");

            // --- Незакрытая кавычка в значении SYSTEM тянет декларацию до конца файла
            // (FindDeclarationEnd) и ExtractQuoted корректно не находит вторую кавычку — не падает.
            File.WriteAllText(Path.Combine(root, "unterminated.dtd"), """
<!ENTITY % bad SYSTEM "never-closed
""");
            var unterminatedResult = DtdCatalogLoader.Load(Path.Combine(root, "unterminated.dtd"));
            Check(unterminatedResult.Elements.Count == 0 && unterminatedResult.Warnings.Count == 0,
                "незакрытая кавычка в SYSTEM — декларация проглочена целиком до EOF, ничего не подключается, но и не падает");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    private static readonly (string? ClassAttr, DisplayKind Expected)[] DtdClassVariants =
    {
        ("+ topic/ph custom-d/inline-el ", DisplayKind.Inline),
        ("- topic/topic ", DisplayKind.Topic),
        ("+ topic/table custom-d/table-el ", DisplayKind.Table),
        ("+ topic/pre custom-d/pre-el ", DisplayKind.Preformatted),
        (null, DisplayKind.Block) // без @class — решает форма модели, см. ниже
    };

    /// <summary>Прогоняет тот же генератор контент-моделей (ModelSpec), что и
    /// ModelAutomatonPropertyTests, но теперь через весь путь ЧЕРЕЗ реальный DTD-текст:
    /// DtdReader (сущности, ELEMENT/ATTLIST) → DtdCatalogLoader (ElementDef, DisplayKind по
    /// @class) → ElementDef.Automaton — если где-то на этом пути модель или @class потеряются
    /// или исказятся, сгенерированная валидная последовательность перестанет проходить, а
    /// ожидаемый DisplayKind разойдётся с фактическим.</summary>
    internal static void DtdCatalogLoaderPropertyTests()
    {
        Section("Property-based: DtdReader + DtdCatalogLoader (случайные DTD-фрагменты)");

        var rnd = new Random(20260921);
        var alphabet = new[] { "x1", "x2", "x3", "x4", "x5" };
        const int iterations = 60;
        var failures = 0;

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "PropDtd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    var elementCount = 2 + rnd.Next(3); // 2..4
                    var sb = new StringBuilder();
                    // Параметрическая сущность на общий атрибут — как в реальных модульных DTD
                    // DITA (%univ-atts; и т.п.), уже проверялась вручную в DtdCatalogLoaderTests.
                    sb.Append("<!ENTITY % common-atts \"id ID #IMPLIED\">\n");

                    var expectations = new List<(string Name, string ClassAttr, DisplayKind Expected, ModelSpec Model)>();

                    for (var e = 0; e < elementCount; e++)
                    {
                        var name = $"el{i}_{e}";
                        var spec = GenerateModelNode(rnd, alphabet, depth: 2);
                        var modelText = SerializeModelNode(spec);
                        sb.Append("<!ELEMENT ").Append(name).Append(' ').Append(modelText).Append(">\n");

                        var variant = DtdClassVariants[rnd.Next(DtdClassVariants.Length)];
                        var expectedDisplay = variant.ClassAttr is null
                            ? (ModelParser.Parse(modelText).AllowsText() ? DisplayKind.Block : DisplayKind.Container)
                            : variant.Expected;

                        sb.Append("<!ATTLIST ").Append(name).Append(" %common-atts;");
                        if (variant.ClassAttr is not null)
                        {
                            sb.Append(" class CDATA \"").Append(variant.ClassAttr).Append('"');
                        }

                        sb.Append(">\n");

                        expectations.Add((name, variant.ClassAttr ?? string.Empty, expectedDisplay, spec));
                    }

                    var dtdPath = Path.Combine(root, $"case{i}.dtd");
                    File.WriteAllText(dtdPath, sb.ToString());

                    var result = DtdCatalogLoader.Load(dtdPath);
                    if (result.Warnings.Count != 0)
                    {
                        failures++;
                        Failures.Add($"DtdCatalogLoader property: неожиданные предупреждения на случае {i}: {string.Join("; ", result.Warnings)}");
                        continue;
                    }

                    if (result.Elements.Count != expectations.Count)
                    {
                        failures++;
                        Failures.Add($"DtdCatalogLoader property: ожидалось {expectations.Count} элементов, получено {result.Elements.Count} (случай {i})");
                        continue;
                    }

                    foreach (var (name, classAttr, expectedDisplay, spec) in expectations)
                    {
                        var def = result.Elements.FirstOrDefault(el => el.Name == name);
                        if (def is null)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: элемент {name} не найден (случай {i})");
                            continue;
                        }

                        if (def.ClassAttr != classAttr)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: @class у {name} — ожидалось '{classAttr}', получено '{def.ClassAttr}' (случай {i})");
                        }

                        if (def.Display != expectedDisplay)
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: DisplayKind у {name} — ожидалось {expectedDisplay}, получено {def.Display} (случай {i})");
                        }

                        if (!def.Attributes.ContainsKey("id"))
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: атрибут id из %common-atts; не попал в {name} (случай {i})");
                        }

                        var validSequence = GenerateValidSequence(spec, rnd);
                        if (!def.Automaton.Validate(validSequence, out _, out _))
                        {
                            failures++;
                            Failures.Add($"DtdCatalogLoader property: контент-модель {name} не приняла собственную сгенерированную последовательность [{string.Join(",", validSequence)}] (случай {i})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Failures.Add($"DtdCatalogLoader property: исключение на случае {i}: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }

        Check(failures == 0,
            $"{iterations} случайных DTD-фрагментов (сущности + ELEMENT/ATTLIST + @class) разобраны и провалидированы без расхождений ({iterations - failures}/{iterations})");
    }
}
