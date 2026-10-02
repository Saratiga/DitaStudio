using System.Runtime.CompilerServices;
using DitaStudio.Core.Localization;

namespace DitaStudio.Desktop.Tests;

/// <summary>Тесты проверяют русские тексты и не должны зависеть ни от языка машины, ни от настоящего выбора пользователя.</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Pin()
    {
        LanguageSettings.SettingsPath = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "language-" + Environment.ProcessId + ".txt");
        Loc.Instance.SetUserLanguage("ru");
    }
}
