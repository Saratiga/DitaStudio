using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Плагины: загрузка сборок и правила проверки.
internal static partial class CoreChecks
{
    internal static void PluginLoaderTests()
    {
        Section("Плагины: загрузка сборки через AssemblyLoadContext");

        // tests/TestPlugin — не настоящий плагин, а компилируемая solution'ом заглушка (Private=false
        // на ссылке на DitaStudio.Core, как и полагается настоящему плагину) — единственный способ
        // проверить, что typeof(T).IsAssignableFrom(type) реально работает через границу отдельно
        // загруженной сборки, а не просто на бумаге.
        // Проект тестов ссылается на TestPlugin (ReferenceOutputAssembly=false) — плагин всегда
        // собран в той же конфигурации, что и тесты (bin/<Конфигурация>/net8.0).
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var testPluginDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestPlugin", "bin", configuration, "net8.0");
        var testPluginDll = Path.Combine(testPluginDir, "TestPlugin.dll");

        if (!File.Exists(testPluginDll))
        {
            Check(false, $"TestPlugin.dll собран вместе с тестами: не найден по {testPluginDll}");
            return;
        }

        var rules = PluginLoader.Load<IValidationRulePlugin>(testPluginDir);
        Check(rules.Warnings.Count == 0, "загрузка настоящей .dll без предупреждений: " + string.Join("; ", rules.Warnings));
        Check(rules.Instances.Count == 1, $"найдена ровно одна конкретная реализация IValidationRulePlugin (абстрактный класс пропущен): {rules.Instances.Count}");

        var rule = rules.Instances.FirstOrDefault();
        Check(rule?.Name == "TestPlugin.NoteFlaggingRule", $"Name загруженного плагина: '{rule?.Name}'");

        var doc = DitaDocument.Parse("""
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic">
  <title>Тема</title>
  <conbody>
    <note>Предупреждение.</note>
    <p>Обычный абзац.</p>
  </conbody>
</concept>
""");
        var issues = rule?.Check(doc).ToList() ?? new List<ValidationIssue>();
        Check(issues.Count == 1 && issues[0].Message.Contains("из тестового плагина"),
            "плагин из отдельно загруженной сборки реально выполняется и находит note в документе хоста");

        var formats = PluginLoader.Load<IPublishFormatPlugin>(testPluginDir);
        Check(formats.Instances.Count == 1 && formats.Instances[0].Name == "TestPlugin.NoopPublishFormat",
            "PluginLoader.Load<T> фильтрует ровно по запрошенному контракту, даже когда в той же сборке есть реализации другого");

        var missing = PluginLoader.Load<IValidationRulePlugin>(Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N")));
        Check(missing.Instances.Count == 0 && missing.Warnings.Count == 0, "несуществующая папка плагинов — пустой результат без ошибок");
    }

    /// <summary>Правило, которое всегда падает — проверяет, что DitaProject.ValidateAll не роняет
    /// всю проверку из-за одного сломанного плагина.</summary>
    private sealed class ThrowingRule : IValidationRulePlugin
    {
        public string Name => "ThrowingRule";

        public IEnumerable<ValidationIssue> Check(DitaDocument document) => throw new InvalidOperationException("нарочно сломан");
    }

    /// <summary>Локальная реализация контракта (не связана с tests/TestPlugin — та существует для
    /// проверки настоящей динамической загрузки, а тут просто нужен любой IValidationRulePlugin,
    /// чтобы проверить, что DitaProject.ValidateAll его вызывает и вливает результат).</summary>
    private sealed class NoteFlaggingTestRule : IValidationRulePlugin
    {
        public string Name => "NoteFlaggingTestRule";

        public IEnumerable<ValidationIssue> Check(DitaDocument document) =>
            document.Root.DescendantsAndSelf()
                .Where(n => n.Kind == NodeKind.Element && n.Name == "note")
                .Select(n => new ValidationIssue(IssueSeverity.Info, "[из тестового плагина] найден note", n, document.FilePath));
    }

    internal static void DitaProjectValidateAllWithPluginsTests()
    {
        Section("DitaProject.ValidateAll с плагинами");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "topic1.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic1">
  <title>Тема 1</title>
  <conbody>
    <note>Предупреждение.</note>
  </conbody>
</concept>
""");
            File.WriteAllText(Path.Combine(root, "topic2.dita"), """
<?xml version="1.0" encoding="UTF-8"?>
<concept id="topic2">
  <title> </title>
  <conbody>
    <p>Без note.</p>
  </conbody>
</concept>
""");
            var project = new DitaProject(root);
            project.Scan();

            var withoutPlugins = project.ValidateAll();
            Check(!withoutPlugins.Any(i => i.Message.Contains("из тестового плагина")),
                "без плагинов правило из плагина не срабатывает");
            Check(withoutPlugins.Any(i => i.Message.Contains("Пустой заголовок")),
                "обычная проверка стиля находит пустой заголовок во втором файле (опорная точка для следующей проверки)");

            var noteRule = new NoteFlaggingTestRule();
            var withPlugin = project.ValidateAll(new IValidationRulePlugin[] { noteRule });
            Check(withPlugin.Count(i => i.Message.Contains("из тестового плагина")) == 1,
                "плагин находит note ровно в одном файле из двух");

            var withThrowingPlugin = project.ValidateAll(new IValidationRulePlugin[] { new ThrowingRule() });
            Check(withThrowingPlugin.Count(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("ThrowingRule") && i.Message.Contains("нарочно сломан")) == 2,
                "плагин падает на обоих файлах — по предупреждению на каждый, ни один не пропущен");
            Check(withThrowingPlugin.Any(i => i.Message.Contains("Пустой заголовок")),
                "обычная проверка стиля второго файла всё равно выполнилась, несмотря на падение плагина на первом");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }
}
