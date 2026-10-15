using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;
using DitaStudio.Desktop.Views;
using DitaStudio.Presentation.Plugins;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>
/// Пример плагинов из руководства (samples/plugins/DitaStudio.SamplePlugins) — настоящая сборка, загруженная так же, как в редакторе:
/// из подпапки plugins/DitaStudio.SamplePlugins/ через <see cref="PluginLoader"/>. Код из руководства должен оставаться рабочим.
/// </summary>
public sealed class SamplePluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", "SamplePlugins-" + Guid.NewGuid().ToString("N"));
    private readonly string _plugins;

    public SamplePluginTests()
    {
        // Пример собирается вместе с тестами (ссылка в .csproj только задаёт порядок сборки) в своей конфигурации.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var built = Path.Combine(RepositoryRoot(), "samples", "plugins", "DitaStudio.SamplePlugins", "bin", configuration, "net8.0", "DitaStudio.SamplePlugins.dll");
        Assert.True(File.Exists(built), "пример плагинов собран: " + built);
        _plugins = Path.Combine(_root, "plugins");
        var folder = Path.Combine(_plugins, "DitaStudio.SamplePlugins");
        Directory.CreateDirectory(folder);
        File.Copy(built, Path.Combine(folder, "DitaStudio.SamplePlugins.dll"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // сборка плагина занята AssemblyLoadContext до конца прогона — удалится системой
        }
    }

    [Fact]
    public void LoadsAllThreeKinds()
    {
        var rules = PluginLoader.Load<IValidationRulePlugin>(_plugins);
        var formats = PluginLoader.Load<IPublishFormatPlugin>(_plugins);
        var commands = PluginLoader.Load<IAuthorCommandPlugin>(_plugins);
        Assert.Empty(rules.Warnings.Concat(formats.Warnings).Concat(commands.Warnings));
        Assert.Equal(new[] { "Пример: длинные абзацы и команды", "Пример: слова-паразиты" }, rules.Instances.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal("md", Assert.Single(formats.Instances).FileExtension);
        Assert.Equal("Пример: типографские кавычки «»", Assert.Single(commands.Instances).Name);
    }

    [Fact]
    public void ValidationRulesFindIssues()
    {
        var rules = PluginLoader.Load<IValidationRulePlugin>(_plugins).Instances;
        var longText = string.Join(' ', Enumerable.Repeat("слово", 70));
        var doc = DitaDocument.Parse($"""
            <task id="t"><title>Задача</title><taskbody><steps>
              <step><cmd>Это просто: нажмите кнопку.</cmd></step>
              <step><cmd>{string.Join(' ', Enumerable.Repeat("нажмите", 30))}</cmd></step>
            </steps><result><p>{longText}</p><p>Команда <codeph>просто</codeph> и простой текст.</p></result></taskbody></task>
            """);
        var issues = rules.SelectMany(r => r.Check(doc)).ToList();
        Assert.Equal(2, issues.Count(i => i.Severity == IssueSeverity.Info)); // длинные: команда (30 > 25) и абзац (70 > 60)
        var stop = Assert.Single(issues, i => i.Severity == IssueSeverity.Warning);
        Assert.Contains("«просто»", stop.Message);
        Assert.Equal("cmd", stop.Node?.Name); // «просто» в codeph и «простой» — не замечания
    }

    [Fact]
    public void MarkdownExportOfSampleProject()
    {
        var format = Assert.Single(PluginLoader.Load<IPublishFormatPlugin>(_plugins).Instances);
        var project = new DitaProject(Path.Combine(RepositoryRoot(), "samples", "GuideSample"));
        project.Scan();
        var output = Path.Combine(_root, "out", "guide.md");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        format.Publish(project, Path.Combine(project.RootPath, "guide.ditamap"), new PublishOptions(), output);

        var md = File.ReadAllText(output);
        Assert.StartsWith("# Руководство пользователя", md);
        Assert.Contains("## О продукте", md);
        Assert.Contains("1. Запустите `setup.exe`.", md);      // шаги — нумерованный список, filepath — код
        Assert.Contains("| --- |", md);                         // таблица
        Assert.Contains("```", md);                             // блок кода
        Assert.Contains("> **Совет.**", md);                   // примечание type="tip"
        Assert.DoesNotContain("<keyword", md);
        Assert.DoesNotContain("keyref", md);
    }

    [AvaloniaFact]
    public async Task QuotesCommandEditsDocumentWithUndo()
    {
        var command = Assert.Single(PluginLoader.Load<IAuthorCommandPlugin>(_plugins).Instances);
        var project = Path.Combine(_root, "Пример");
        CopyDirectory(Path.Combine(RepositoryRoot(), "samples", "GuideSample"), project);
        var about = Path.Combine(project, "concepts", "about.dita");
        File.WriteAllText(about, File.ReadAllText(about).Replace("<p>Проект состоит", "<p>Кнопка \"Пуск\" и команда <codeph>echo \"x\"</codeph>. Проект состоит"));

        var window = new MainWindow();
        window.Show();
        await window.ViewModel.ProjectPanel.LoadProjectAsync(project);
        var pane = (DocumentView)window.ViewModel.OpenDocument!(about)!;
        Dispatcher.UIThread.RunJobs();

        command.Execute(pane);
        var p = pane.Document.Root.Descendants().First(n => n.Name == "p" && n.InnerText.Contains("Кнопка"));
        Assert.Contains("Кнопка «Пуск» и команда echo \"x\".", p.InnerText); // в codeph кавычки не тронуты

        pane.PerformUndo();
        var restored = pane.Document.Root.Descendants().First(n => n.Name == "p" && n.InnerText.Contains("Кнопка"));
        Assert.Contains("Кнопка \"Пуск\"", restored.InnerText); // одна отмена возвращает документ

        pane.Document.IsDirty = false;
        window.Close();
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(from, to));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to), true);
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DitaStudio.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (DitaStudio.sln).");
    }
}
