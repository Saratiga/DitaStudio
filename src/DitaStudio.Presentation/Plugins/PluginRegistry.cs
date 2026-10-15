using DitaStudio.Core.Plugins;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;

namespace DitaStudio.Presentation.Plugins;

/// <summary>Загружает плагины из папки plugins/ рядом с приложением один раз при старте (см.
/// App.xaml.cs оболочки) и держит их в статических списках на весь сеанс редактора. Три отдельных прохода
/// PluginLoader.Load — по одному на контракт; если один класс реализует сразу несколько
/// контрактов, он будет создан по разу на каждый (простая, но не самая экономная схема —
/// приемлемо для небольших плагинов).</summary>
public static class PluginRegistry
{
    public static IReadOnlyList<IValidationRulePlugin> ValidationRules { get; private set; } = Array.Empty<IValidationRulePlugin>();

    public static IReadOnlyList<IPublishFormatPlugin> PublishFormats { get; private set; } = Array.Empty<IPublishFormatPlugin>();

    public static IReadOnlyList<IAuthorCommandPlugin> AuthorCommands { get; private set; } = Array.Empty<IAuthorCommandPlugin>();

    public static IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

    /// <summary>Папка, из которой загружены плагины (null — ещё не загружались).</summary>
    public static string? PluginsDirectory { get; private set; }

    /// <summary>Все загруженные плагины: вид (контракт) и экземпляр — для окна «Справка → Плагины…».</summary>
    public static IEnumerable<(string Kind, string Name, string File)> All =>
        ValidationRules.Select(p => ("validation", p.Name, FileOf(p)))
            .Concat(PublishFormats.Select(p => ("publish", p.Name, FileOf(p))))
            .Concat(AuthorCommands.Select(p => ("author", p.Name, FileOf(p))));

    private static string FileOf(object plugin)
    {
        var location = plugin.GetType().Assembly.Location;
        return string.IsNullOrEmpty(location) ? plugin.GetType().Assembly.GetName().Name + ".dll" : Path.GetFileName(location);
    }

    public static void Load(string pluginsDirectory)
    {
        PluginsDirectory = pluginsDirectory;
        var rules = PluginLoader.Load<IValidationRulePlugin>(pluginsDirectory);
        var formats = PluginLoader.Load<IPublishFormatPlugin>(pluginsDirectory);
        var commands = PluginLoader.Load<IAuthorCommandPlugin>(pluginsDirectory);

        ValidationRules = rules.Instances;
        PublishFormats = formats.Instances;
        AuthorCommands = commands.Instances;
        // Одна сломанная сборка даёт по ошибке на каждый проход — показываем её один раз.
        Warnings = rules.Warnings.Concat(formats.Warnings).Concat(commands.Warnings).Distinct().ToList();
    }
}
