namespace DitaStudio.UiTests;

/// <summary>Находит корень репозитория и ключевые пути от него — тесты запускаются из
/// tests/DitaStudio.UiTests/bin/&lt;Config&gt;/net8.0-windows/, репозиторий может лежать где угодно.</summary>
internal static class RepoLocator
{
    public static string RootPath { get; } = FindRoot();

    public static string DitaStudioExePath(string configuration = "Debug") =>
        Path.Combine(RootPath, "src", "DitaStudio.App", "bin", configuration, "net8.0-windows", "DitaStudio.exe");

    public static string GuideSamplePath => Path.Combine(RootPath, "samples", "GuideSample");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Не удалось найти DitaStudio.sln, поднимаясь от {AppContext.BaseDirectory} — репозиторий переместили или тесты запущены не оттуда.");
    }
}
