using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

// Маленький CLI-драйвер поверх FlaUI (UI Automation): подключается к уже запущенному
// DitaStudio.exe и выполняет одну команду за вызов — печатает результат в stdout и выходит.
// Не требует прав computer-use и не зависит от списка "установленных приложений".

// Без этого процесс видит "виртуализированный" (уменьшенный) рабочий стол при масштабировании
// экрана >100%, а BoundingRectangle из UI Automation приходит в физических пикселях — клики по
// координатам из скриншота промахиваются мимо цели. PerMonitorV2 уравнивает оба пространства.
SetProcessDpiAwarenessContext(new IntPtr(-4));

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToList();
var windowFilter = ExtractOption(rest, "--window");
var depthOption = ExtractOption(rest, "--depth");
var depth = depthOption is null ? 5 : int.Parse(depthOption);

using var automation = new UIA3Automation();

try
{
    switch (command)
    {
        case "launch":
            Launch(rest);
            return 0;

        case "windows":
            ListWindows(automation);
            return 0;

        case "tree":
            Tree(automation, windowFilter, depth);
            return 0;

        case "click":
            Click(automation, windowFilter, rest[0]);
            return 0;

        case "type":
            TypeInto(automation, windowFilter, rest[0], rest[1]);
            return 0;

        case "menu":
            Menu(automation, rest[0]);
            return 0;

        case "screenshot":
            Screenshot(automation, windowFilter, rest[0]);
            return 0;

        case "close":
            CloseWindow(automation, windowFilter);
            return 0;

        case "clickat":
            ClickAt(int.Parse(rest[0]), int.Parse(rest[1]), rest.Count > 2 && rest[2] == "double");
            return 0;

        case "rightclickat":
            Mouse.MoveTo(int.Parse(rest[0]), int.Parse(rest[1]));
            Mouse.Click(FlaUI.Core.Input.MouseButton.Right);
            Console.WriteLine($"Правый клик по координатам ({rest[0]},{rest[1]})");
            return 0;

        case "kbtype":
            Keyboard.Type(rest[0]);
            Console.WriteLine($"Набрано с клавиатуры: {rest[0]}");
            return 0;

        case "kbkey":
            var vk = Enum.Parse<FlaUI.Core.WindowsAPI.VirtualKeyShort>(rest[0], ignoreCase: true);
            Keyboard.Press(vk);
            Thread.Sleep(50);
            Keyboard.Release(vk);
            Console.WriteLine($"Нажата клавиша: {rest[0]}");
            return 0;

        case "focus":
            ResolveWindow(automation, windowFilter).Focus();
            Console.WriteLine("Окно активировано.");
            return 0;

        case "bounds":
            Bounds(automation, windowFilter);
            return 0;

        case "find":
            FindByType(automation, windowFilter, rest[0]);
            return 0;

        case "text":
            ReadText(automation, windowFilter, rest[0]);
            return 0;

        case "state":
            ReadState(automation, windowFilter, rest[0]);
            return 0;

        case "select":
            Select(automation, windowFilter, rest[0], rest[1]);
            return 0;

        case "doubleclick":
            DoubleClickElement(automation, windowFilter, rest[0]);
            return 0;

        case "list":
            ListContainer(automation, windowFilter, rest[0], depth);
            return 0;

        case "wait":
            Wait(automation, windowFilter, rest[0], rest.Count > 1 ? int.Parse(rest[1]) : 5000);
            return 0;

        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Ошибка: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
    UiHarness — драйвер UI Automation для DitaStudio.exe

      launch <путь к DitaStudio.exe>
      windows
      tree [--window <подстрока>] [--depth N]
      click <имя или AutomationId> [--window <подстрока>]
      type <имя или AutomationId> <текст> [--window <подстрока>]
      menu "Пункт1|Пункт2|..."
      screenshot <путь.png> [--window <подстрока>]
      close [--window <подстрока>]
      clickat <x> <y> [double]   — запасной путь, когда элемент не находится по имени
      text <имя или AutomationId> [--window <подстрока>]      — прочитать Value/Text/Name
      state <имя или AutomationId> [--window <подстрока>]     — Toggle/Selection/ExpandCollapse
      select <контейнер> <текст пункта> [--window <подстрока>] — выбрать пункт в ComboBox/ListBox/TreeView
      doubleclick <имя или AutomationId> [--window <подстрока>] — двойной клик по элементу (не по координатам)
      list <контейнер|window> [--window <подстрока>] [--depth N] — дерево внутри контейнера (для списков/деревьев)
      wait <имя или AutomationId> [таймаутМс=5000] [--window <подстрока>] — ждать появления элемента
    """);
}

static string? ExtractOption(List<string> args, string name)
{
    var i = args.IndexOf(name);
    if (i < 0 || i + 1 >= args.Count)
    {
        return null;
    }

    var value = args[i + 1];
    args.RemoveRange(i, 2);
    return value;
}

static void Launch(List<string> args)
{
    var exePath = args[0];
    var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    Console.WriteLine($"Запущено, PID={process?.Id}");
}

static Process FindProcess()
{
    var candidates = Process.GetProcessesByName("DitaStudio");
    if (candidates.Length == 0)
    {
        throw new InvalidOperationException("Процесс DitaStudio.exe не найден — сначала запустите его (launch).");
    }

    return candidates[0];
}

static Window[] TopLevelWindows(UIA3Automation automation)
{
    var app = FlaUI.Core.Application.Attach(FindProcess());
    return app.GetAllTopLevelWindows(automation);
}

static void ListWindows(UIA3Automation automation)
{
    foreach (var w in TopLevelWindows(automation))
    {
        Console.WriteLine($"«{w.Title}»  class={w.ClassName}  modal={w.Patterns.Window.PatternOrDefault?.IsModal.ValueOrDefault}");
    }
}

static Window ResolveWindow(UIA3Automation automation, string? filter)
{
    var windows = TopLevelWindows(automation);
    if (windows.Length == 0)
    {
        throw new InvalidOperationException("У процесса нет ни одного окна.");
    }

    if (filter is not null)
    {
        var match = windows.FirstOrDefault(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            var titles = string.Join(", ", windows.Select(w => $"«{w.Title}»"));
            throw new InvalidOperationException($"Окно с «{filter}» в заголовке не найдено. Открытые окна: {titles}");
        }

        return match;
    }

    if (windows.Length > 1)
    {
        var titles = string.Join(", ", windows.Select(w => $"«{w.Title}»"));
        Console.Error.WriteLine($"Открыто несколько окон, беру последнее: {titles}");
    }

    return windows[^1];
}

static void Tree(UIA3Automation automation, string? filter, int maxDepth)
{
    var window = ResolveWindow(automation, filter);
    Console.WriteLine($"Окно: «{window.Title}»");
    PrintNode(window, 0, maxDepth);
}

static void PrintNode(AutomationElement element, int level, int maxDepth)
{
    if (level > maxDepth)
    {
        return;
    }

    try
    {
        var name = string.IsNullOrEmpty(element.Name) ? "" : $" \"{element.Name}\"";
        var id = string.IsNullOrEmpty(element.AutomationId) ? "" : $" id={element.AutomationId}";
        Console.WriteLine($"{new string(' ', level * 2)}{element.ControlType}{name}{id}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{new string(' ', level * 2)}<не удалось прочитать узел: {ex.Message}>");
    }

    foreach (var child in element.FindAllChildren())
    {
        PrintNode(child, level + 1, maxDepth);
    }
}

static string SafeName(AutomationElement e)
{
    try { return e.Name ?? string.Empty; } catch { return string.Empty; }
}

static string SafeId(AutomationElement e)
{
    try { return e.AutomationId ?? string.Empty; } catch { return string.Empty; }
}

static AutomationElement FindByNameOrId(AutomationElement root, string identifier)
{
    var all = root.FindAllDescendants();
    var byId = all.FirstOrDefault(e => string.Equals(SafeId(e), identifier, StringComparison.OrdinalIgnoreCase));
    if (byId is not null)
    {
        return byId;
    }

    var byExactName = all.FirstOrDefault(e => string.Equals(SafeName(e), identifier, StringComparison.OrdinalIgnoreCase));
    if (byExactName is not null)
    {
        return byExactName;
    }

    var byContains = all.FirstOrDefault(e => SafeName(e).Contains(identifier, StringComparison.OrdinalIgnoreCase));
    if (byContains is not null)
    {
        return byContains;
    }

    throw new InvalidOperationException($"Элемент «{identifier}» не найден.");
}

static void Activate(AutomationElement element)
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

static void Click(UIA3Automation automation, string? filter, string identifier)
{
    var window = ResolveWindow(automation, filter);
    var element = FindByNameOrId(window, identifier);
    Activate(element);
    Console.WriteLine($"Активировано: {element.ControlType} \"{element.Name}\"");
}

static void TypeInto(UIA3Automation automation, string? filter, string identifier, string text)
{
    var window = ResolveWindow(automation, filter);
    var element = FindByNameOrId(window, identifier);
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

    Console.WriteLine($"Введено в {element.ControlType} \"{element.Name}\": {text}");
}

static void Menu(UIA3Automation automation, string path)
{
    var window = ResolveWindow(automation, null);
    foreach (var itemName in path.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        var desktop = automation.GetDesktop();
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

    Console.WriteLine($"Меню пройдено: {path}");
}

static void Screenshot(UIA3Automation automation, string? filter, string path)
{
    var window = ResolveWindow(automation, filter);
    var image = Capture.Element(window);
    image.ToFile(path);
    Console.WriteLine($"Сохранено: {path}");
}

static void FindByType(UIA3Automation automation, string? filter, string controlTypeName)
{
    var controlType = Enum.Parse<ControlType>(controlTypeName, ignoreCase: true);
    var window = ResolveWindow(automation, filter);
    var fromWindow = window.FindAllDescendants(cf => cf.ByControlType(controlType));
    Console.WriteLine($"В окне «{window.Title}» через FindAllDescendants: {fromWindow.Length}");
    foreach (var e in fromWindow.Take(30))
    {
        var r = e.BoundingRectangle;
        Console.WriteLine($"  {SafeName(e)}  rect=({r.X},{r.Y},{r.Width}x{r.Height})");
    }

    var fromDesktop = automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(controlType));
    Console.WriteLine($"С рабочего стола: {fromDesktop.Length}");
    foreach (var e in fromDesktop.Take(30))
    {
        var r = e.BoundingRectangle;
        Console.WriteLine($"  {SafeName(e)}  rect=({r.X},{r.Y},{r.Width}x{r.Height})");
    }
}

static void Bounds(UIA3Automation automation, string? filter)
{
    var window = ResolveWindow(automation, filter);
    var r = window.BoundingRectangle;
    Console.WriteLine($"BoundingRectangle: X={r.X} Y={r.Y} Width={r.Width} Height={r.Height}");
}

static void ClickAt(int x, int y, bool doubleClick)
{
    Mouse.MoveTo(x, y);
    if (doubleClick)
    {
        Mouse.DoubleClick(FlaUI.Core.Input.MouseButton.Left);
    }
    else
    {
        Mouse.Click(FlaUI.Core.Input.MouseButton.Left);
    }

    Console.WriteLine($"Клик по координатам ({x},{y}){(doubleClick ? " (двойной)" : "")}");
}

[DllImport("user32.dll")]
static extern bool SetProcessDpiAwarenessContext(IntPtr value);

static void ReadText(UIA3Automation automation, string? filter, string identifier)
{
    var window = ResolveWindow(automation, filter);
    var element = FindByNameOrId(window, identifier);

    var textPattern = element.Patterns.Text.PatternOrDefault;
    if (textPattern is not null)
    {
        Console.WriteLine(textPattern.DocumentRange.GetText(-1));
        return;
    }

    var valuePattern = element.Patterns.Value.PatternOrDefault;
    if (valuePattern is not null)
    {
        Console.WriteLine(valuePattern.Value.ValueOrDefault);
        return;
    }

    Console.WriteLine(SafeName(element));
}

static void ReadState(UIA3Automation automation, string? filter, string identifier)
{
    var window = ResolveWindow(automation, filter);
    var element = FindByNameOrId(window, identifier);
    var reported = false;

    var toggle = element.Patterns.Toggle.PatternOrDefault;
    if (toggle is not null)
    {
        Console.WriteLine($"Toggle: {toggle.ToggleState.ValueOrDefault}");
        reported = true;
    }

    var selection = element.Patterns.SelectionItem.PatternOrDefault;
    if (selection is not null)
    {
        Console.WriteLine($"Selected: {selection.IsSelected.ValueOrDefault}");
        reported = true;
    }

    var expand = element.Patterns.ExpandCollapse.PatternOrDefault;
    if (expand is not null)
    {
        Console.WriteLine($"ExpandCollapse: {expand.ExpandCollapseState.ValueOrDefault}");
        reported = true;
    }

    var value = element.Patterns.Value.PatternOrDefault;
    if (value is not null)
    {
        Console.WriteLine($"Value: {value.Value.ValueOrDefault}");
        reported = true;
    }

    if (!reported)
    {
        Console.WriteLine("У элемента нет распознанного состояния (Toggle/SelectionItem/ExpandCollapse/Value).");
    }
}

static void Select(UIA3Automation automation, string? filter, string containerIdentifier, string itemText)
{
    var window = ResolveWindow(automation, filter);
    var container = FindByNameOrId(window, containerIdentifier);

    var expand = container.Patterns.ExpandCollapse.PatternOrDefault;
    if (expand is not null)
    {
        expand.Expand();
        Thread.Sleep(200);
    }

    var candidates = container.FindAllDescendants();
    var item = candidates.FirstOrDefault(e => string.Equals(SafeName(e), itemText, StringComparison.OrdinalIgnoreCase))
        ?? candidates.FirstOrDefault(e => SafeName(e).Contains(itemText, StringComparison.OrdinalIgnoreCase));

    if (item is null)
    {
        // ComboBox-попапы иногда всплывают вне поддерева контейнера — ищем по всему окну.
        var all = window.FindAllDescendants();
        item = all.FirstOrDefault(e => string.Equals(SafeName(e), itemText, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => SafeName(e).Contains(itemText, StringComparison.OrdinalIgnoreCase));
    }

    if (item is null)
    {
        throw new InvalidOperationException($"Пункт «{itemText}» не найден в «{containerIdentifier}».");
    }

    var selectionItem = item.Patterns.SelectionItem.PatternOrDefault;
    if (selectionItem is not null)
    {
        selectionItem.Select();
    }
    else
    {
        Activate(item);
    }

    Console.WriteLine($"Выбрано: {item.ControlType} \"{item.Name}\"");
}

static void DoubleClickElement(UIA3Automation automation, string? filter, string identifier)
{
    var window = ResolveWindow(automation, filter);
    var element = FindByNameOrId(window, identifier);
    element.Focus();
    var r = element.BoundingRectangle;
    var x = (int)(r.X + r.Width / 2);
    var y = (int)(r.Y + r.Height / 2);
    Mouse.MoveTo(x, y);
    Mouse.DoubleClick(FlaUI.Core.Input.MouseButton.Left);
    Console.WriteLine($"Двойной клик: {element.ControlType} \"{element.Name}\" в ({x},{y})");
}

static void ListContainer(UIA3Automation automation, string? filter, string identifier, int maxDepth)
{
    var window = ResolveWindow(automation, filter);
    var root = string.Equals(identifier, "window", StringComparison.OrdinalIgnoreCase)
        ? window
        : FindByNameOrId(window, identifier);

    Console.WriteLine($"Контейнер: {root.ControlType} \"{root.Name}\"");
    foreach (var child in root.FindAllChildren())
    {
        PrintNode(child, 0, maxDepth);
    }
}

static void Wait(UIA3Automation automation, string? filter, string identifier, int timeoutMs)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var window = ResolveWindow(automation, filter);
            var element = FindByNameOrId(window, identifier);
            Console.WriteLine($"Появилось: {element.ControlType} \"{element.Name}\"");
            return;
        }
        catch
        {
            Thread.Sleep(200);
        }
    }

    throw new InvalidOperationException($"Элемент «{identifier}» не появился за {timeoutMs} мс.");
}

static void CloseWindow(UIA3Automation automation, string? filter)
{
    var window = ResolveWindow(automation, filter);
    var close = window.Patterns.Window.PatternOrDefault;
    if (close is not null)
    {
        close.Close();
    }
    else
    {
        window.Close();
    }

    Console.WriteLine($"Закрыто: «{window.Title}»");
}
