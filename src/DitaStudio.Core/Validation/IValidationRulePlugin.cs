using DitaStudio.Core.Model;

namespace DitaStudio.Core.Validation;

/// <summary>Контракт плагина дополнительных правил валидации — реализующая сборка кладётся в
/// папку plugins/ рядом с приложением и подхватывается PluginLoader (см. Core/Plugins) через
/// AssemblyLoadContext. Автор плагина ссылается на DitaStudio.Core.dll без копирования в
/// локальный вывод (Private=false) — тогда типы совпадают с уже загруженными в хосте.</summary>
public interface IValidationRulePlugin
{
    string Name { get; }

    IEnumerable<ValidationIssue> Check(DitaDocument document);
}
