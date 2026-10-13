using DitaStudio.Core.Project;

namespace DitaStudio.Tests;

// Новая папка в проекте: проверка имени и создание (вложенные, вне проекта, повтор).
internal static partial class CoreChecks
{
    internal static void ProjectFolderTests()
    {
        Section("Новая папка в проекте");

        Check(ProjectFolders.Validate("tasks") is null && ProjectFolders.Validate("Новая папка") is null && ProjectFolders.Validate("a/b c/d") is null && ProjectFolders.Validate(@"a\b") is null,
            "обычные имена и вложенные через слеш допустимы");
        Check(ProjectFolders.Validate("") is not null && ProjectFolders.Validate("   ") is not null && ProjectFolders.Validate(null) is not null, "пустое имя недопустимо");
        Check(ProjectFolders.Validate("a//b") is not null && ProjectFolders.Validate("/a") is not null && ProjectFolders.Validate("a/") is not null, "пустая часть пути недопустима");
        Check(ProjectFolders.Validate("a:b") is not null && ProjectFolders.Validate("a*b") is not null && ProjectFolders.Validate("a?b") is not null && ProjectFolders.Validate("a\"b") is not null &&
              ProjectFolders.Validate("a<b") is not null && ProjectFolders.Validate("a|b") is not null, "символы :*?\"<>| недопустимы");
        Check(ProjectFolders.Validate("..") is not null && ProjectFolders.Validate("a/../b") is not null && ProjectFolders.Validate("name.") is not null && ProjectFolders.Validate(".hidden") is not null,
            "точки: «..», имя с точкой в конце и служебное с точки в начале недопустимы");
        Check(ProjectFolders.Validate("CON") is not null && ProjectFolders.Validate("aux.txt") is not null && ProjectFolders.Validate("com1") is not null && ProjectFolders.Validate("console") is null,
            "зарезервированные имена Windows недопустимы, похожие — допустимы");
        Check(ProjectFolders.Validate(new string('x', 101)) is not null && ProjectFolders.Validate(new string('x', 100)) is null, "длина части — до 100");

        WithProject(new Dictionary<string, string> { ["t.dita"] = "<topic id=\"t\"><title>T</title><body><p>x</p></body></topic>" }, (root, project) =>
        {
            var created = ProjectFolders.Create(project, root, "tasks/new");
            Check(Directory.Exists(created) && created == Path.Combine(root, "tasks", "new"), "создана вложенная папка tasks/new: " + created);

            var inner = ProjectFolders.Create(project, Path.Combine(root, "tasks"), "другая");
            Check(Directory.Exists(inner) && Path.GetDirectoryName(inner) == Path.Combine(root, "tasks"), "папка внутри выбранной папки");

            var again = Assert(() => ProjectFolders.Create(project, root, "tasks"), typeof(IOException));
            Check(again, "повторное имя — ошибка «уже есть», папка не затронута");
            File.WriteAllText(Path.Combine(root, "readme"), "x");
            Check(Assert(() => ProjectFolders.Create(project, root, "readme"), typeof(IOException)), "имя занято файлом — ошибка");
            Check(Assert(() => ProjectFolders.Create(project, root, "a:b"), typeof(ArgumentException)), "недопустимое имя — ArgumentException");
            Check(Assert(() => ProjectFolders.Create(project, Path.GetDirectoryName(root)!, "outside"), typeof(ArgumentException)) && !Directory.Exists(Path.Combine(Path.GetDirectoryName(root)!, "outside")),
                "родитель вне проекта — отказ, папка не создана");
        });
    }

    private static bool Assert(Action action, Type expected)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex.GetType() == expected)
        {
            return true;
        }

        return false;
    }
}
