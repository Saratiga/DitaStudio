using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace DitaStudio.UiAutomation;

/// <summary>Состояние элемента, которое умеет читать <see cref="UiDriver.ReadState"/> —
/// столько полей заполнено, сколько паттернов реально поддерживает элемент.</summary>
public sealed record UiElementState(
    ToggleState? Toggle,
    bool? Selected,
    ExpandCollapseState? ExpandCollapse,
    string? Value);

/// <summary>
/// Драйвер UI Automation (FlaUI) поверх уже запущенного DitaStudio.exe — общая логика для
/// CLI-инструмента <c>tools/UiHarness</c> (ручная проверка) и xUnit-тестов в
/// <c>tests/DitaStudio.UiTests</c> (автоматическая). Один источник истины: правки в поиске
/// элементов/кликах правятся один раз и сразу видны из обоих мест.
/// </summary>
public sealed class UiDriver : IDisposable
{
    static UiDriver()
    {
        // Без этого процесс видит "виртуализированный" (уменьшенный) рабочий стол при
        // масштабировании экрана >100%: BoundingRectangle из UI Automation приходит в физических
        // пикселях (клики по координатам из скриншота промахиваются мимо цели), а сам обход
        // автомейшн-дерева внутри масштабированных WPF-панелей рвётся на полпути — потомки после
        // определённой глубины просто не находятся, хотя визуально элементы на месте. PerMonitorV2
        // уравнивает оба пространства. Должно выполниться ДО первого UIA3Automation — поэтому
        // именно статический конструктор, а не конструктор экземпляра: поле _automation
        // инициализируется раньше тела обычного конструктора, и порядок "автомейшн раньше DPI"
        // воспроизводил именно эту порчу обхода дерева.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    private readonly UIA3Automation _automation = new();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automation.Dispose();
    }

    // --------------------------------------------------------------- процесс/окна

    public static Process Launch(string exePath)
    {
        var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        return process ?? throw new InvalidOperationException($"Не удалось запустить {exePath}.");
    }

    public static bool IsRunning => Process.GetProcessesByName("DitaStudio").Length > 0;

    public static void KillAllRunning()
    {
        foreach (var process in Process.GetProcessesByName("DitaStudio"))
        {
            try
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
            catch
            {
                // процесс уже завершился между проверкой и Kill — не критично
            }
        }
    }

    private static Process FindProcess()
    {
        var candidates = Process.GetProcessesByName("DitaStudio");
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("Процесс DitaStudio.exe не найден — сначала запустите его (Launch).");
        }

        return candidates[0];
    }

    public Window[] TopLevelWindows()
    {
        var app = Application.Attach(FindProcess());
        return app.GetAllTopLevelWindows(_automation);
    }

    public Window ResolveWindow(string? filter = null)
    {
        var windows = TopLevelWindows();
        if (windows.Length == 0)
        {
            throw new InvalidOperationException("У процесса нет ни одного окна.");
        }

        if (filter is null)
        {
            return windows[^1];
        }

        var match = windows.FirstOrDefault(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // Модальные диалоги (Dialogs.Shell — "О программе" и т.п.) не всегда попадают в
        // GetAllTopLevelWindows: FlaUI иногда отдаёт их не отдельным окном рабочего стола, а
        // узлом ControlType.Window внутри автомейшн-дерева окна-владельца. Ищем там же.
        foreach (var window in windows)
        {
            var nested = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(e => SafeName(e).Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (nested is not null)
            {
                return nested.AsWindow();
            }
        }

        var titles = string.Join(", ", windows.Select(w => $"«{w.Title}»"));
        throw new InvalidOperationException($"Окно с «{filter}» в заголовке не найдено. Открытые окна: {titles}");
    }

    /// <summary>Ждёт появления главного окна процесса (первое окно после запуска может появиться
    /// не сразу — WPF успевает поднять процесс раньше, чем построить визуальное дерево).</summary>
    public Window WaitForWindow(string? filter = null, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return ResolveWindow(filter);
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }

        throw new InvalidOperationException($"Окно не появилось за {timeoutMs} мс.", last);
    }

    // --------------------------------------------------------------- поиск элементов

    public static string SafeName(AutomationElement e)
    {
        try
        {
            return e.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string SafeId(AutomationElement e)
    {
        try
        {
            return e.AutomationId ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Ищет по AutomationId, затем по точному совпадению имени, затем по вхождению
    /// подстроки в имя — тот же порядок приоритетов, что и раньше в CLI-версии.</summary>
    public static AutomationElement FindByNameOrId(AutomationElement root, string identifier)
    {
        var all = root.FindAllDescendants();
        return all.FirstOrDefault(e => string.Equals(SafeId(e), identifier, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => string.Equals(SafeName(e), identifier, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => SafeName(e).Contains(identifier, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Элемент «{identifier}» не найден.");
    }

    /// <summary>Как <see cref="FindByNameOrId(AutomationElement,string)"/>, но null вместо
    /// исключения — удобно для проверок вида "элемента точно нет на экране".</summary>
    public static AutomationElement? TryFindByNameOrId(AutomationElement root, string identifier)
    {
        var all = root.FindAllDescendants();
        return all.FirstOrDefault(e => string.Equals(SafeId(e), identifier, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => string.Equals(SafeName(e), identifier, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => SafeName(e).Contains(identifier, StringComparison.OrdinalIgnoreCase));
    }

    public AutomationElement Find(string identifier, string? windowFilter = null) =>
        FindByNameOrId(ResolveWindow(windowFilter), identifier);

    public AutomationElement? TryFind(string identifier, string? windowFilter = null) =>
        TryFindByNameOrId(ResolveWindow(windowFilter), identifier);

    /// <summary>Ждёт появления элемента (окно может ещё перестраивать дерево после клика/навигации).</summary>
    public AutomationElement Wait(string identifier, int timeoutMs = 5000, string? windowFilter = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var found = TryFind(identifier, windowFilter);
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(200);
        }

        throw new InvalidOperationException($"Элемент «{identifier}» не появился за {timeoutMs} мс.");
    }

    public IReadOnlyList<AutomationElement> FindAllByType(ControlType controlType, string? windowFilter = null) =>
        ResolveWindow(windowFilter).FindAllDescendants(cf => cf.ByControlType(controlType));

    // --------------------------------------------------------------- действия

    public static void Activate(AutomationElement element)
    {
        var invoke = element.Patterns.Invoke.PatternOrDefault;
        if (invoke is not null)
        {
            invoke.Invoke();
            return;
        }

        var toggle = element.Patterns.Toggle.PatternOrDefault;
        if (toggle is not null)
        {
            toggle.Toggle();
            return;
        }

        var expand = element.Patterns.ExpandCollapse.PatternOrDefault;
        if (expand is not null)
        {
            expand.Expand();
            return;
        }

        element.Click();
    }

    public AutomationElement Click(string identifier, string? windowFilter = null)
    {
        var element = Find(identifier, windowFilter);
        Activate(element);
        return element;
    }

    public AutomationElement DoubleClick(string identifier, string? windowFilter = null)
    {
        var element = Find(identifier, windowFilter);
        element.Focus();
        var r = element.BoundingRectangle;
        var x = (int)(r.X + r.Width / 2);
        var y = (int)(r.Y + r.Height / 2);
        Mouse.MoveTo(x, y);
        Mouse.DoubleClick(MouseButton.Left);
        return element;
    }

    public void ClickAt(int x, int y, bool doubleClick = false)
    {
        Mouse.MoveTo(x, y);
        if (doubleClick)
        {
            Mouse.DoubleClick(MouseButton.Left);
        }
        else
        {
            Mouse.Click(MouseButton.Left);
        }
    }

    public void RightClickAt(int x, int y)
    {
        Mouse.MoveTo(x, y);
        Mouse.Click(MouseButton.Right);
    }

    public AutomationElement TypeInto(string identifier, string text, string? windowFilter = null)
    {
        var element = Find(identifier, windowFilter);
        var value = element.Patterns.Value.PatternOrDefault;
        if (value is not null)
        {
            value.SetValue(text);
        }
        else
        {
            element.Focus();
            Keyboard.Type(text);
        }

        return element;
    }

    public static void KeyType(string text) => Keyboard.Type(text);

    public static void KeyPress(FlaUI.Core.WindowsAPI.VirtualKeyShort key)
    {
        Keyboard.Press(key);
        Thread.Sleep(50);
        Keyboard.Release(key);
    }

    /// <summary>Проходит путь меню вида "Файл|Открыть папку проекта…" — каждый пункт ищется и
    /// активируется по очереди (учитывает и главное меню окна, и всплывающие подменю на рабочем
    /// столе). Пауза между пунктами даёт WPF время построить следующее подменю.</summary>
    public void Menu(string path, string? windowFilter = null)
    {
        var window = ResolveWindow(windowFilter);
        foreach (var itemName in path.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var desktop = _automation.GetDesktop();
            var byWindow = window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
            var byDesktop = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
            var candidate = byWindow.Concat(byDesktop)
                .FirstOrDefault(e => SafeName(e).Contains(itemName, StringComparison.OrdinalIgnoreCase));

            if (candidate is null)
            {
                throw new InvalidOperationException($"Пункт меню «{itemName}» не найден.");
            }

            Activate(candidate);
            Thread.Sleep(300);
        }
    }

    /// <summary>Выбирает пункт в ComboBox/ListBox/TreeView: разворачивает контейнер (если у него
    /// есть ExpandCollapse), ищет пункт внутри него, а если не нашёл — по всему окну (всплывающие
    /// попапы ComboBox иногда оказываются вне поддерева самого контейнера).</summary>
    public AutomationElement Select(string containerIdentifier, string itemText, string? windowFilter = null)
    {
        var window = ResolveWindow(windowFilter);
        var container = FindByNameOrId(window, containerIdentifier);

        var expand = container.Patterns.ExpandCollapse.PatternOrDefault;
        if (expand is not null)
        {
            expand.Expand();
            Thread.Sleep(200);
        }

        var item = TryFindByNameOrId(container, itemText) ?? TryFindByNameOrId(window, itemText)
            ?? throw new InvalidOperationException($"Пункт «{itemText}» не найден в «{containerIdentifier}».");

        var selectionItem = item.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItem is not null)
        {
            selectionItem.Select();
        }
        else
        {
            Activate(item);
        }

        return item;
    }

    public void Focus(string? windowFilter = null) => ResolveWindow(windowFilter).Focus();

    public void Close(string? windowFilter = null)
    {
        var window = ResolveWindow(windowFilter);
        var closePattern = window.Patterns.Window.PatternOrDefault;
        if (closePattern is not null)
        {
            closePattern.Close();
        }
        else
        {
            window.Close();
        }
    }

    // --------------------------------------------------------------- чтение

    /// <summary>Text/Value/Name — в этом порядке, смотря что поддерживает элемент.</summary>
    public string? ReadText(string identifier, string? windowFilter = null)
    {
        var element = Find(identifier, windowFilter);

        var textPattern = element.Patterns.Text.PatternOrDefault;
        if (textPattern is not null)
        {
            return textPattern.DocumentRange.GetText(-1);
        }

        var valuePattern = element.Patterns.Value.PatternOrDefault;
        return valuePattern is not null ? valuePattern.Value.ValueOrDefault : SafeName(element);
    }

    public UiElementState ReadState(string identifier, string? windowFilter = null)
    {
        var element = Find(identifier, windowFilter);

        var toggle = element.Patterns.Toggle.PatternOrDefault;
        var selection = element.Patterns.SelectionItem.PatternOrDefault;
        var expand = element.Patterns.ExpandCollapse.PatternOrDefault;
        var value = element.Patterns.Value.PatternOrDefault;

        return new UiElementState(
            toggle?.ToggleState.ValueOrDefault,
            selection?.IsSelected.ValueOrDefault,
            expand?.ExpandCollapseState.ValueOrDefault,
            value?.Value.ValueOrDefault);
    }

    public System.Drawing.Rectangle Bounds(string? windowFilter = null) => ResolveWindow(windowFilter).BoundingRectangle;

    public void Screenshot(string path, string? windowFilter = null)
    {
        var window = ResolveWindow(windowFilter);
        Capture.Element(window).ToFile(path);
    }

    /// <summary>Дерево потомков элемента (или всего окна) — имя/тип/AutomationId на каждый узел,
    /// в порядке обхода, с отступом по глубине. Пригодится и для отладки, и для точечных assert'ов
    /// вида "среди строк дерева есть текст X".</summary>
    public IReadOnlyList<string> Tree(string? windowFilter = null, int maxDepth = 5)
    {
        var window = ResolveWindow(windowFilter);
        var lines = new List<string>();
        Walk(window, 0);
        return lines;

        void Walk(AutomationElement element, int level)
        {
            if (level > maxDepth)
            {
                return;
            }

            try
            {
                var name = string.IsNullOrEmpty(element.Name) ? string.Empty : $" \"{element.Name}\"";
                var id = string.IsNullOrEmpty(element.AutomationId) ? string.Empty : $" id={element.AutomationId}";
                lines.Add($"{new string(' ', level * 2)}{element.ControlType}{name}{id}");
            }
            catch (Exception ex)
            {
                lines.Add($"{new string(' ', level * 2)}<не удалось прочитать узел: {ex.Message}>");
            }

            foreach (var child in element.FindAllChildren())
            {
                Walk(child, level + 1);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
