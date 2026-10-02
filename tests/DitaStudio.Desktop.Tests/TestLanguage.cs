using System.Runtime.CompilerServices;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Tests;

/// <summary>Тесты проверяют русские тексты и не должны зависеть ни от языка машины, ни от настоящего выбора пользователя.</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Pin()
    {
        // Тесты открывают проекты: список недавних — свой, временный, а не пользовательский.
        DitaStudio.Presentation.RecentProjects.SettingsPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "recent-" + Environment.ProcessId + ".txt");
        LanguageSettings.SettingsPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "language-" + Environment.ProcessId + ".txt");
        Loc.Instance.SetUserLanguage("ru");
    }
}
