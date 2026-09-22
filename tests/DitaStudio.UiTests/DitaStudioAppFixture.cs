using DitaStudio.UiAutomation;
using FlaUI.Core.AutomationElements;

namespace DitaStudio.UiTests;

/// <summary>
/// Запускает DitaStudio.exe один раз на весь набор тестов в коллекции "DitaStudio App" (см.
/// <see cref="AppCollection"/>) — реальный WPF-процесс тяжело поднимать заново на каждый тест.
/// Перед запуском подставляет samples/GuideSample первым в недавние проекты, чтобы тесты могли
/// открыть его через настоящее меню "Файл → Недавние проекты", как это сделал бы человек — без
/// диалога выбора папки, который UI Automation штатно не умеет вести (это системный диалог Windows).
/// </summary>
public sealed class DitaStudioAppFixture : IDisposable
{
    private readonly string? _recentProjectsBackup;
    private readonly string _recentProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "recent-projects.txt");

    public UiDriver Driver { get; }

    public Window MainWindow { get; }

    /// <summary>Путь к учебному проекту, подставленному в недавние — открывается через
    /// <c>OpenGuideSampleViaRecentProjects</c> в тестах, которым нужен открытый проект.</summary>
    public string GuideSamplePath => RepoLocator.GuideSamplePath;

    public DitaStudioAppFixture()
    {
        var exePath = ResolveExePath();
        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException(
                $"DitaStudio.exe не найден: {exePath}\nСоберите решение перед UI-тестами: dotnet build DitaStudio.sln -c Debug");
        }

        if (!Directory.Exists(RepoLocator.GuideSamplePath))
        {
            throw new InvalidOperationException($"Демо-проект не найден: {RepoLocator.GuideSamplePath}");
        }

        UiDriver.KillAllRunning();

        Directory.CreateDirectory(Path.GetDirectoryName(_recentProjectsPath)!);
        _recentProjectsBackup = File.Exists(_recentProjectsPath) ? File.ReadAllText(_recentProjectsPath) : null;
        File.WriteAllText(_recentProjectsPath, RepoLocator.GuideSamplePath + Environment.NewLine);

        UiDriver.Launch(exePath);
        Driver = new UiDriver();
        MainWindow = Driver.WaitForWindow(timeoutMs: 20000);
    }

    /// <summary>Открывает samples/GuideSample через "Файл → Недавние проекты → &lt;путь&gt;" — тот
    /// же путь, что реальный пользователь; ждёт строку статус-бара "Проект открыт: N файлов...".
    ///
    /// ВАЖНО: не проверяет через ProjectTree/TreeItem — обнаружено, что FlaUI/UIA3 в этом
    /// приложении не видит содержимое ВНУТРИ вкладок TabControl (LeftTabs/RightTabs/BottomTabs),
    /// хотя оно реально отрисовано на экране (проверено скриншотом): FindAllDescendants из корня
    /// окна находит заголовки TabItem, но не их содержимое — ни ProjectTree, ни кнопки "Обновить"/
    /// "Создать…" внутри вкладки "Проект" тем же обходом не находятся. Элементы вне TabControl
    /// (меню, тулбар, статус-бар, диалоги) находятся нормально. Похоже на особенность обхода
    /// автомейшн-дерева WPF TabControl конкретно в FlaUI 4.0/UIA3, а не баг этого приложения —
    /// не тратьте время на попытки достучаться до содержимого вкладок тем же Find/Wait/Click,
    /// пока это не разобрано отдельно.</summary>
    public void OpenGuideSampleViaRecentProjects()
    {
        Driver.Menu($"Файл|Недавние проекты|{Path.GetFileName(GuideSamplePath)}");
        Driver.Wait("Проект открыт", timeoutMs: 10000);
    }

    public void Dispose()
    {
        try
        {
            Driver.Close();
        }
        catch
        {
            // окно уже могло закрыться в ходе теста — не критично для очистки
        }

        Driver.Dispose();
        UiDriver.KillAllRunning();

        try
        {
            if (_recentProjectsBackup is null)
            {
                File.Delete(_recentProjectsPath);
            }
            else
            {
                File.WriteAllText(_recentProjectsPath, _recentProjectsBackup);
            }
        }
        catch
        {
            // восстановление списка недавних проектов пользователя не критично для теста
        }
    }

    private static string ResolveExePath()
    {
        var debug = RepoLocator.DitaStudioExePath("Debug");
        return File.Exists(debug) ? debug : RepoLocator.DitaStudioExePath("Release");
    }
}
