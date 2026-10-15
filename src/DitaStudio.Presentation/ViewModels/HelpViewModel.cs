using DitaStudio.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Localization;

namespace DitaStudio.Presentation.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private readonly IShellState _shell;
    private readonly UiServices _ui;
    private readonly IDocumentHost _docs;

    public HelpViewModel(ShellContext context, IDocumentHost docs)
    {
        _shell = context.Shell;
        _ui = context.Ui;
        _docs = docs;
    }

    [RelayCommand]
    private async Task ShowElementHelp()
    {
        var node = _docs.Current?.Author.CurrentNode;
        if (node is null)
        {
            _shell.StatusText = Loc.T("Msg_PutTheCursorInAnElement");
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        if (def is null)
        {
            await _ui.Dialogs.MessageAsync(Loc.T("Msg_Help"), Loc.T("Msg_TheElement0IsNotIn", node.Name));
            return;
        }

        var allowed = def.Automaton.AllowedNames;
        var attributes = def.Attributes.Keys.OrderBy(a => a, StringComparer.Ordinal);

        await _ui.Dialogs.MessageAsync($"<{def.Name}>",
            $"{def.Description}\n\n" +
            Loc.T("Msg_Module0", def.Domain) +
            $"@class: {def.ClassAttr}\n\n" +
            Loc.T("Msg_Content0", def.ModelText) +
            Loc.T("Msg_AllowedChildElements01", allowed.Count, string.Join(", ", allowed.Take(40))) +
            (allowed.Count > 40 ? "…" : string.Empty) +
            Loc.T("Msg_Attributes0", string.Join(", ", attributes)));
    }

    [RelayCommand]
    private Task About() => _ui.Dialogs.AboutAsync();

    /// <summary>«Справка → Плагины…»: папка плагинов, загруженные плагины по видам и ошибки загрузки — проверка для автора плагина.</summary>
    [RelayCommand]
    private Task ShowPlugins() => _ui.Dialogs.MessageAsync(Loc.T("Dlg_Plugins"), PluginReport());

    /// <summary>Текст окна «Плагины».</summary>
    public static string PluginReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.T("Msg_PluginsFolder0", Plugins.PluginRegistry.PluginsDirectory ?? "—")).Append("\n\n");
        var all = Plugins.PluginRegistry.All.ToList();
        if (all.Count == 0)
        {
            sb.Append(Loc.T("Msg_NoPluginsLoaded")).Append('\n');
        }

        foreach (var (kind, title) in new[]
                 {
                     ("validation", Loc.T("Msg_PluginsValidationRules")),
                     ("publish", Loc.T("Msg_PluginsPublishFormats")),
                     ("author", Loc.T("Msg_PluginsAuthorCommands"))
                 })
        {
            var group = all.Where(p => p.Kind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            sb.Append(title).Append('\n');
            foreach (var plugin in group)
            {
                sb.Append("  • ").Append(plugin.Name).Append(" — ").Append(plugin.File).Append('\n');
            }

            sb.Append('\n');
        }

        if (Plugins.PluginRegistry.Warnings.Count > 0)
        {
            sb.Append(Loc.T("Msg_PluginLoadErrors")).Append('\n');
            foreach (var warning in Plugins.PluginRegistry.Warnings)
            {
                sb.Append("  • ").Append(warning).Append('\n');
            }
        }

        return sb.ToString().TrimEnd();
    }
}
