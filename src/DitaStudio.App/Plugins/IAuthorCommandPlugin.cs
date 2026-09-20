using DitaStudio.App.Views;

namespace DitaStudio.App.Plugins;

/// <summary>Контракт плагина — команда режима «Автор», выполняется на текущем открытом
/// документе. См. DitaStudio.Core.Validation.IValidationRulePlugin для общих соглашений
/// загрузки (Private=false на ссылки на хост, чтобы типы совпадали).</summary>
public interface IAuthorCommandPlugin
{
    string Name { get; }

    void Execute(DocumentPane pane);
}
