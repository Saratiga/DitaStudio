using DitaStudio.UiAutomation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

// Тонкий CLI-драйвер поверх DitaStudio.UiAutomation.UiDriver (сам FlaUI-код живёт там —
// общий с tests/DitaStudio.UiTests, здесь только разбор аргументов и печать результата).
// Подключается к уже запущенному DitaStudio.exe и выполняет одну команду за вызов.
// Не требует прав computer-use и не зависит от списка "установленных приложений".

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

try
{
    if (command == "launch")
    {
        var process = UiDriver.Launch(rest[0]);
        Console.WriteLine($"Запущено, PID={process.Id}");
        return 0;
    }

    using var driver = new UiDriver();

    switch (command)
    {
        case "windows":
            foreach (var w in driver.TopLevelWindows())
            {
                Console.WriteLine($"«{w.Title}»  class={w.ClassName}  modal={w.Patterns.Window.PatternOrDefault?.IsModal.ValueOrDefault}");
            }

            return 0;

        case "tree":
        {
            var window = driver.ResolveWindow(windowFilter);
            Console.WriteLine($"Окно: «{window.Title}»");
            foreach (var line in driver.Tree(windowFilter, depth))
            {
                Console.WriteLine(line);
            }

            return 0;
        }

        case "click":
        {
            var element = driver.Click(rest[0], windowFilter);
            Console.WriteLine($"Активировано: {element.ControlType} \"{element.Name}\"");
            return 0;
        }

        case "type":
        {
            var element = driver.TypeInto(rest[0], rest[1], windowFilter);
            Console.WriteLine($"Введено в {element.ControlType} \"{element.Name}\": {rest[1]}");
            return 0;
        }

        case "menu":
            driver.Menu(rest[0]);
            Console.WriteLine($"Меню пройдено: {rest[0]}");
            return 0;

        case "screenshot":
            driver.Screenshot(rest[0], windowFilter);
            Console.WriteLine($"Сохранено: {rest[0]}");
            return 0;

        case "close":
        {
            var title = driver.ResolveWindow(windowFilter).Title;
            driver.Close(windowFilter);
            Console.WriteLine($"Закрыто: «{title}»");
            return 0;
        }

        case "clickat":
            driver.ClickAt(int.Parse(rest[0]), int.Parse(rest[1]), rest.Count > 2 && rest[2] == "double");
            Console.WriteLine($"Клик по координатам ({rest[0]},{rest[1]}){(rest.Count > 2 && rest[2] == "double" ? " (двойной)" : string.Empty)}");
            return 0;

        case "rightclickat":
            driver.RightClickAt(int.Parse(rest[0]), int.Parse(rest[1]));
            Console.WriteLine($"Правый клик по координатам ({rest[0]},{rest[1]})");
            return 0;

        case "kbtype":
            UiDriver.KeyType(rest[0]);
            Console.WriteLine($"Набрано с клавиатуры: {rest[0]}");
            return 0;

        case "kbkey":
            UiDriver.KeyPress(Enum.Parse<FlaUI.Core.WindowsAPI.VirtualKeyShort>(rest[0], ignoreCase: true));
            Console.WriteLine($"Нажата клавиша: {rest[0]}");
            return 0;

        case "focus":
            driver.Focus(windowFilter);
            Console.WriteLine("Окно активировано.");
            return 0;

        case "bounds":
        {
            var r = driver.Bounds(windowFilter);
            Console.WriteLine($"BoundingRectangle: X={r.X} Y={r.Y} Width={r.Width} Height={r.Height}");
            return 0;
        }

        case "find":
        {
            var controlType = Enum.Parse<ControlType>(rest[0], ignoreCase: true);
            var window = driver.ResolveWindow(windowFilter);
            var fromWindow = driver.FindAllByType(controlType, windowFilter);
            Console.WriteLine($"В окне «{window.Title}» через FindAllDescendants: {fromWindow.Count}");
            foreach (var e in fromWindow.Take(30))
            {
                var r = e.BoundingRectangle;
                Console.WriteLine($"  {UiDriver.SafeName(e)}  rect=({r.X},{r.Y},{r.Width}x{r.Height})");
            }

            return 0;
        }

        case "text":
            Console.WriteLine(driver.ReadText(rest[0], windowFilter));
            return 0;

        case "state":
        {
            var state = driver.ReadState(rest[0], windowFilter);
            var reported = false;
            if (state.Toggle is not null)
            {
                Console.WriteLine($"Toggle: {state.Toggle}");
                reported = true;
            }

            if (state.Selected is not null)
            {
                Console.WriteLine($"Selected: {state.Selected}");
                reported = true;
            }

            if (state.ExpandCollapse is not null)
            {
                Console.WriteLine($"ExpandCollapse: {state.ExpandCollapse}");
                reported = true;
            }

            if (state.Value is not null)
            {
                Console.WriteLine($"Value: {state.Value}");
                reported = true;
            }

            if (!reported)
            {
                Console.WriteLine("У элемента нет распознанного состояния (Toggle/SelectionItem/ExpandCollapse/Value).");
            }

            return 0;
        }

        case "select":
        {
            var item = driver.Select(rest[0], rest[1], windowFilter);
            Console.WriteLine($"Выбрано: {item.ControlType} \"{item.Name}\"");
            return 0;
        }

        case "doubleclick":
        {
            var element = driver.DoubleClick(rest[0], windowFilter);
            var r = element.BoundingRectangle;
            Console.WriteLine($"Двойной клик: {element.ControlType} \"{element.Name}\" в ({(int)(r.X + r.Width / 2)},{(int)(r.Y + r.Height / 2)})");
            return 0;
        }

        case "list":
        {
            var identifier = rest[0];
            AutomationElement root = string.Equals(identifier, "window", StringComparison.OrdinalIgnoreCase)
                ? driver.ResolveWindow(windowFilter)
                : driver.Find(identifier, windowFilter);
            Console.WriteLine($"Контейнер: {root.ControlType} \"{root.Name}\"");

            void PrintNode(AutomationElement element, int level)
            {
                if (level > depth)
                {
                    return;
                }

                try
                {
                    var name = string.IsNullOrEmpty(element.Name) ? string.Empty : $" \"{element.Name}\"";
                    var id = string.IsNullOrEmpty(element.AutomationId) ? string.Empty : $" id={element.AutomationId}";
                    Console.WriteLine($"{new string(' ', level * 2)}{element.ControlType}{name}{id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{new string(' ', level * 2)}<не удалось прочитать узел: {ex.Message}>");
                }

                foreach (var child in element.FindAllChildren())
                {
                    PrintNode(child, level + 1);
                }
            }

            foreach (var child in root.FindAllChildren())
            {
                PrintNode(child, 0);
            }

            return 0;
        }

        case "wait":
        {
            var element = driver.Wait(rest[0], rest.Count > 1 ? int.Parse(rest[1]) : 5000, windowFilter);
            Console.WriteLine($"Появилось: {element.ControlType} \"{element.Name}\"");
            return 0;
        }

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
