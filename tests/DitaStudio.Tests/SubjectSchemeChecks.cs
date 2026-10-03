using DitaStudio.Core.Project;
using DitaStudio.Core.Validation;

namespace DitaStudio.Tests;

// Значения условных атрибутов проверяются по схеме категорий проекта (subjectScheme).
internal static partial class CoreChecks
{
    internal static void SubjectSchemeTests()
    {
        Section("Проверка значений по схеме категорий");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string Topic(string id, string attrs) =>
                $"<?xml version=\"1.0\"?><topic id=\"{id}\"><title>T</title><shortdesc>S</shortdesc><body><p {attrs}>Текст</p></body></topic>";

            File.WriteAllText(Path.Combine(root, "ok.dita"), Topic("ok", "product=\"alpha\" audience=\"admin user\""));
            File.WriteAllText(Path.Combine(root, "bad.dita"), Topic("bad", "product=\"gamma\" audience=\"admin guest\""));
            File.WriteAllText(Path.Combine(root, "group.dita"), Topic("group", "props=\"platform(linux windows) platform(bsd)\""));
            File.WriteAllText(Path.Combine(root, "none.dita"), Topic("none", string.Empty));
            File.WriteAllText(Path.Combine(root, "guide.ditamap"),
                "<?xml version=\"1.0\"?><map><title>M</title><topicref href=\"ok.dita\"/><topicref href=\"bad.dita\"/><topicref href=\"group.dita\"/><topicref href=\"none.dita\"/></map>");

            var project = new DitaProject(root);
            project.Scan();

            // --- без схемы категорий проверки нет
            Check(!project.ValidateAll().Any(i => i.Message.Contains("схеме категорий") || i.Message.Contains("subject scheme")),
                "нет схемы категорий — значения не проверяются");

            // --- схема: product — alpha, beta (прямо), audience — вложенные admin, user по keyref, platform — linux, windows с ограничением по <p>
            File.WriteAllText(Path.Combine(root, "scheme.ditamap"), """
                <?xml version="1.0"?>
                <subjectScheme>
                  <title>Схема</title>
                  <subjectdef keys="roles">
                    <subjectdef keys="admin"/>
                    <subjectdef keys="user"/>
                  </subjectdef>
                  <subjectdef keys="systems">
                    <subjectdef keys="linux"/>
                    <subjectdef keys="windows"/>
                  </subjectdef>
                  <enumerationdef>
                    <attributedef name="product"/>
                    <subjectdef keys="alpha"/>
                    <subjectdef keys="beta"/>
                  </enumerationdef>
                  <enumerationdef>
                    <attributedef name="audience"/>
                    <subjectdef keyref="roles"/>
                  </enumerationdef>
                  <enumerationdef>
                    <attributedef name="platform"/>
                    <elementdef name="p"/>
                    <subjectdef keyref="systems"/>
                  </enumerationdef>
                </subjectScheme>
                """);
            project.Scan();

            var issues = project.ValidateAll().Where(i => i.Severity == IssueSeverity.Warning &&
                                                          (i.Message.Contains("схеме категорий") || i.Message.Contains("subject scheme"))).ToList();
            string Where(ValidationIssue i) => Path.GetFileNameWithoutExtension(i.FilePath) + ":" + i.Message;
            Check(!issues.Any(i => i.FilePath!.EndsWith("ok.dita")), "значения из схемы (прямые и через keyref) — без замечаний");
            Check(issues.Any(i => i.FilePath!.EndsWith("bad.dita") && i.Message.Contains("«gamma»") && i.Message.Contains("@product")),
                "product=gamma не объявлен в схеме: " + string.Join(" | ", issues.Select(Where)));
            Check(issues.Any(i => i.FilePath!.EndsWith("bad.dita") && i.Message.Contains("«guest»") && i.Message.Contains("@audience")),
                "audience=guest не объявлен (допустимы admin и user из roles)");
            Check(issues.Count(i => i.FilePath!.EndsWith("bad.dita")) == 2, "admin у audience не даёт лишнего замечания");
            Check(issues.Any(i => i.FilePath!.EndsWith("group.dita") && i.Message.Contains("«bsd»")), "значение групповой записи props platform(bsd) не объявлено");
            Check(!issues.Any(i => i.FilePath!.EndsWith("group.dita") && (i.Message.Contains("«linux»") || i.Message.Contains("«windows»"))),
                "linux и windows в групповой записи допустимы");
            Check(!issues.Any(i => i.FilePath!.EndsWith("none.dita")), "элемент без условных атрибутов замечаний не даёт");
            Check(issues.All(i => i.Line >= 0 && i.Location.Length > 0), "замечание указывает на элемент");

            // --- ограничение elementdef: platform на других элементах не проверяется
            File.WriteAllText(Path.Combine(root, "elem.dita"),
                "<?xml version=\"1.0\"?><topic id=\"elem\"><title platform=\"bsd\">T</title><shortdesc>S</shortdesc><body><p>x</p></body></topic>");
            File.WriteAllText(Path.Combine(root, "guide.ditamap"),
                "<?xml version=\"1.0\"?><map><title>M</title><topicref href=\"ok.dita\"/><topicref href=\"elem.dita\"/></map>");
            project.Scan();
            var elemIssues = project.ValidateAll().Where(i => i.FilePath!.EndsWith("elem.dita") && (i.Message.Contains("схеме категорий") || i.Message.Contains("subject scheme"))).ToList();
            Check(elemIssues.Count == 0, "elementdef ограничивает проверку: platform у <title> не проверяется");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // временная папка удалится системой
            }
        }
    }
}
