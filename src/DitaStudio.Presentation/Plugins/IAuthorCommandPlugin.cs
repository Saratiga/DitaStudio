using DitaStudio.Presentation.Services;

namespace DitaStudio.Presentation.Plugins;

/// <summary>Контракт плагина — команда режима «Автор», выполняется на текущем открытом
/// документе. См. DitaStudio.Core.Validation.IValidationRulePlugin для общих соглашений
/// загрузки (Private=false на ссылки на хост, чтобы типы совпадали). Контракт общий для
/// WPF- и Avalonia-версий, поэтому плагин получает <see cref="IDocumentView"/>, а не
/// контрол конкретной оболочки.</summary>
public interface IAuthorCommandPlugin
{
    string Name { get; }

    void Execute(IDocumentView pane);
}
