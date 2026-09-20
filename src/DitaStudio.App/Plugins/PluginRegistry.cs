using DitaStudio.Core.Plugins;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;

namespace DitaStudio.App.Plugins;

/// <summary>Загружает плагины из папки plugins/ рядом с приложением один раз при старте (см.
/// App.xaml.cs) и держит их в статических списках на весь сеанс редактора. Три отдельных прохода
/// PluginLoader.Load — по одному на контракт; если один класс реализует сразу несколько
/// контрактов, он будет создан по разу на каждый (простая, но не самая экономная схема —
/// приемлемо для небольших плагинов).</summary>
public static class PluginRegistry
{
    public static IReadOnlyList<IValidationRulePlugin> ValidationRules { get; private set; } = Array.Empty<IValidationRulePlugin>();

    public static IReadOnlyList<IPublishFormatPlugin> PublishFormats { get; private set; } = Array.Empty<IPublishFormatPlugin>();

    public static IReadOnlyList<IAuthorCommandPlugin> AuthorCommands { get; private set; } = Array.Empty<IAuthorCommandPlugin>();

    public static IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

    public static void Load(string pluginsDirectory)
    {
        var rules = PluginLoader.Load<IValidationRulePlugin>(pluginsDirectory);
        var formats = PluginLoader.Load<IPublishFormatPlugin>(pluginsDirectory);
        var commands = PluginLoader.Load<IAuthorCommandPlugin>(pluginsDirectory);

        ValidationRules = rules.Instances;
        PublishFormats = formats.Instances;
        AuthorCommands = commands.Instances;
        Warnings = rules.Warnings.Concat(formats.Warnings).Concat(commands.Warnings).ToList();
    }
}
