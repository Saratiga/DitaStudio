using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Validation;

namespace DitaStudio.Tests;

// Внешний DTD действует только в своём проекте: встроенный каталог не меняется, другой
// проект его элементов не видит, отключение DTD их убирает.
internal static partial class CoreChecks
{
    internal static void ProjectCatalogTests()
    {
        Section("Каталог проекта: внешний DTD изолирован от других проектов");

        var withDtdRoot = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-dtd");
        var plainRoot = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N") + "-plain");
        Directory.CreateDirectory(withDtdRoot);
        Directory.CreateDirectory(plainRoot);

        try
        {
            // Специализация: свой тип топика и свой фразовый элемент, плюс переопределение
            // встроенного note (только текст) — такое переопределение раньше оставалось
            // в общем каталоге и для всех следующих проектов.
            File.WriteAllText(Path.Combine(withDtdRoot, "custom.dtd"), """
<!ELEMENT wtopic (title, widget*)>
<!ATTLIST wtopic id ID #REQUIRED class CDATA "- topic/topic wtopic/wtopic ">
<!ELEMENT widget (#PCDATA)>
<!ATTLIST widget class CDATA "+ topic/ph custom-d/widget ">
<!ELEMENT note (#PCDATA)>
<!ATTLIST note class CDATA "- topic/note ">
""");
            const string widgetTopic = """
<?xml version="1.0" encoding="UTF-8"?>
<wtopic id="w"><title>Виджет</title><widget>x</widget></wtopic>
""";
            File.WriteAllText(Path.Combine(withDtdRoot, "w.dita"), widgetTopic);
            File.WriteAllText(Path.Combine(plainRoot, "w.dita"), widgetTopic);

            var withDtd = new DitaProject(withDtdRoot);
            withDtd.SetExternalDtdPath("custom.dtd");
            withDtd.Scan();
            var plain = new DitaProject(plainRoot);
            plain.Scan();

            var (catalog, dtd) = withDtd.LoadCatalog();
            Check(dtd is not null && dtd.Warnings.Count == 0,
                "LoadCatalog: DTD разобран без предупреждений: " + string.Join("; ", dtd?.Warnings ?? Array.Empty<string>()));
            Check(ReferenceEquals(catalog, withDtd.Catalog), "LoadCatalog возвращает тот же каталог, что Catalog");
            Check(withDtd.Catalog.Get("widget") is not null && withDtd.Catalog.Get("wtopic") is not null,
                "каталог проекта с DTD знает его элементы");
            Check(withDtd.Catalog.Get("p") is not null, "встроенные элементы в каталоге проекта остаются");
            Check(DitaCatalog.Builtin.Get("widget") is null, "встроенный каталог элементов DTD не получил");
            Check(ReferenceEquals(plain.Catalog, DitaCatalog.Builtin), "проект без DTD работает со встроенным каталогом");
            Check(plain.Catalog.Get("widget") is null, "другой проект элементов чужого DTD не видит");

            var builtinNote = DitaCatalog.Builtin.Get("note")!.Model.CollectNames();
            var projectNote = withDtd.Catalog.Get("note")!.Model.CollectNames();
            Check(builtinNote.Contains("p") && !projectNote.Contains("p"),
                "переопределение встроенного note действует только в своём проекте");

            var withDtdErrors = withDtd.ValidateAll().Where(i => i.Severity == IssueSeverity.Error).ToList();
            Check(withDtdErrors.Count == 0,
                "ValidateAll проверяет по каталогу проекта — топик специализации без ошибок: " +
                string.Join("; ", withDtdErrors.Select(i => i.Message)));
            Check(plain.ValidateAll().Any(i => i.Severity == IssueSeverity.Error),
                "тот же топик в проекте без DTD — ошибка (элементы неизвестны)");

            try
            {
                DitaCatalog.Activate(withDtd.Catalog);
                Check(DitaCatalog.Default.Get("widget") is not null, "Activate: Default — каталог активного проекта");
                Check(DitaDocument.Parse(widgetTopic).Kind == DitaDocumentKind.Topic,
                    "код без ссылки на проект (DitaDocument.Kind) видит тип топика из DTD активного проекта");
                DitaCatalog.Activate(plain.Catalog);
                Check(DitaCatalog.Default.Get("widget") is null, "смена активного проекта убирает элементы прежнего DTD");
            }
            finally
            {
                DitaCatalog.Activate(null);
            }

            Check(ReferenceEquals(DitaCatalog.Default, DitaCatalog.Builtin), "Activate(null) возвращает встроенный каталог");

            withDtd.SetExternalDtdPath(null);
            Check(withDtd.Catalog.Get("widget") is null && withDtd.Catalog.Get("note")!.Model.CollectNames().Contains("p"),
                "отключение DTD убирает его элементы и переопределения из каталога проекта");

            var mergeRejected = false;
            try
            {
                DitaCatalog.Builtin.Merge(Array.Empty<ElementDef>());
            }
            catch (InvalidOperationException)
            {
                mergeRejected = true;
            }

            Check(mergeRejected, "Merge во встроенный каталог запрещён — только WithElements");
        }
        finally
        {
            foreach (var dir in new[] { withDtdRoot, plainRoot })
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // временные файлы удалятся системой
                }
            }
        }
    }
}
