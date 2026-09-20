using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Validation;

namespace TestPlugin;

/// <summary>Отмечает каждый &lt;note&gt; сведением — специально узнаваемый текст для теста
/// PluginLoaderTests (DitaStudio.Tests), проверяющего настоящую динамическую загрузку.</summary>
public sealed class NoteFlaggingRule : IValidationRulePlugin
{
    public string Name => "TestPlugin.NoteFlaggingRule";

    public IEnumerable<ValidationIssue> Check(DitaDocument document)
    {
        foreach (var node in document.Root.DescendantsAndSelf())
        {
            if (node.Kind == NodeKind.Element && node.Name == "note")
            {
                yield return new ValidationIssue(IssueSeverity.Info, "[из тестового плагина] найден note", node, document.FilePath);
            }
        }
    }
}

/// <summary>Не делает ничего полезного — существует, чтобы проверить, что PluginLoader.Load&lt;T&gt;
/// фильтрует ровно по запрошенному контракту, даже когда в сборке есть реализации нескольких.</summary>
public sealed class NoopPublishFormat : IPublishFormatPlugin
{
    public string Name => "TestPlugin.NoopPublishFormat";

    public string FileExtension => "test";

    public void Publish(DitaProject project, string mapPath, PublishOptions options, string outputPath)
    {
    }
}

/// <summary>Абстрактный класс, реализующий контракт — не должен попасть в результат
/// (у него нет публичного конструктора без параметров в применимом смысле: его нельзя
/// инстанцировать вовсе). Проверяет, что PluginLoader пропускает абстрактные типы.</summary>
public abstract class AbstractRule : IValidationRulePlugin
{
    public abstract string Name { get; }

    public abstract IEnumerable<ValidationIssue> Check(DitaDocument document);
}
