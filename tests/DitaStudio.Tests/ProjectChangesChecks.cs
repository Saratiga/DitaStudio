using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Tests;

// Правки по проекту: элементы со status new / changed / deleted во всех файлах, путь к элементу и обратно.
internal static partial class CoreChecks
{
    internal static void ProjectChangesTests()
    {
        Section("Правки по проекту (status)");

        WithProject(new Dictionary<string, string>
        {
            ["a.dita"] = "<topic id=\"a\"><title>A</title><body><p status=\"new\">Новый   абзац</p><p>Обычный</p><note status=\"deleted\"><p>Удалённое</p></note></body></topic>",
            ["b.dita"] = "<topic id=\"b\"><title status=\"changed\">B</title><body><p status=\"unchanged\">Без правки</p></body></topic>",
            ["c.dita"] = "<topic id=\"c\"><title>C</title><body><p>Ничего</p></body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"a.dita\" status=\"new\"/></map>"
        }, (root, project) =>
        {
            var changes = ProjectChanges.Find(project);
            string Describe(ProjectChange c) => Path.GetFileName(c.File.RelativePath) + ":" + c.Element + ":" + c.Status;
            var described = changes.Select(Describe).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Check(described.SequenceEqual(new[] { "a.dita:note:deleted", "a.dita:p:new", "b.dita:title:changed", "m.ditamap:topicref:new" }),
                "найдены new, changed, deleted во всех файлах; unchanged и файлы без правок — нет: " + string.Join(", ", described));
            var paragraph = changes.First(c => c.Element == "p");
            Check(paragraph.Text == "Новый абзац", "текст правки одной строкой: " + paragraph.Text);

            var document = project.GetDocument(Path.Combine(root, "a.dita"));
            var node = ProjectChanges.Resolve(document, paragraph.Path);
            Check(node is { Name: "p" } && node.GetAttribute("status") == "new", "путь ведёт обратно к элементу");
            Check(ProjectChanges.Resolve(document, new[] { 9, 9 }) is null, "несуществующий путь — null");
        });
    }
}
