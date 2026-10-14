using Avalonia.Controls;

namespace DitaStudio.Desktop.Authoring;

/// <summary>
/// Контекстные меню «Автора» (текст блока, ячейки, выделенные блоки) открываются вручную, а не через свойство ContextMenu элемента, поэтому
/// следить за ними приходится самим: открыто не больше одного меню, а щелчок в «Авторе», прокрутка и уход из окна закрывают его.
/// </summary>
public static class AuthorMenus
{
    private static ContextMenu? _open;

    /// <summary>Открытое сейчас меню «Автора» или null.</summary>
    public static ContextMenu? Current => _open is { IsOpen: true } ? _open : null;

    /// <summary>Закрывает прежнее меню и открывает <paramref name="menu"/> у <paramref name="target"/>.</summary>
    public static void Open(ContextMenu menu, Control target)
    {
        Close();
        _open = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, menu))
            {
                _open = null;
            }
        };
        menu.Open(target);
    }

    /// <summary>Закрывает открытое меню, если оно есть.</summary>
    public static void Close()
    {
        var menu = _open;
        _open = null;
        if (menu is { IsOpen: true })
        {
            menu.Close();
        }
    }
}
