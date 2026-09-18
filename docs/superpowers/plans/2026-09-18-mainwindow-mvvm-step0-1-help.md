# MainWindow → MVVM: Шаг 0 (инфраструктура) + Шаг 1 (Help) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Подключить `CommunityToolkit.Mvvm`, ввести `MainViewModel` с сквозным
состоянием (`StatusText`, `Current`), и мигрировать первую, самую простую
область (`MainWindow.Help.cs`) на `HelpViewModel` — обкатать паттерн на
минимальном риске перед более сложными областями.

**Architecture:** `MainViewModel : ObservableObject` — корень, создаётся в
`MainWindow` и становится `DataContext` окна. `HelpViewModel` — дочерний VM,
получает ссылку на `MainViewModel` через конструктор (без DI-контейнера).
Обработчики `OnXxx` заменяются на `[RelayCommand]`-методы, XAML `Click=`
заменяется на `Command="{Binding ...}"`.

**Tech Stack:** .NET 8, WPF, `CommunityToolkit.Mvvm` (source generators
`[ObservableProperty]`/`[RelayCommand]`).

**Спека:** `docs/superpowers/specs/2026-09-18-mainwindow-mvvm-design.md`

## Global Constraints

- `CommunityToolkit.Mvvm` — NuGet только в `DitaStudio.App.csproj`. `DitaStudio.Core`
  и `DitaStudio.Docx` остаются без новых зависимостей.
- Без `IDialogService`/абстракции диалогов — `Dialogs.Message`/`Dialogs.About`
  и т.п. вызываются из VM напрямую, как статические методы (см. спеку,
  раздел «Явно не делается»).
- Тестирование этой миграции НЕ идёт по классическому red/green юнит-тесту:
  `tests/DitaStudio.Tests` не ссылается на `DitaStudio.App`, и мы не заводим
  для этого отдельный тестовый проект (YAGNI, см. спеку). Вместо
  «написать тест → увидеть красный → сделать зелёным» каждый шаг проверяется:
  (1) сборка, (2) регресс — прогон существующих 238 проверок ядра,
  (3) UI-smoke через `tools/UiHarness` по изменённому экрану. Эта замена
  тест-цикла — намеренное решение спеки, не отступление от процесса.
- Каждый шаг — отдельный коммит.
- Диалоговые классы (`Dialogs`) — в неймспейсе `DitaStudio.App.Views`.

---

### Task 0: Инфраструктура MVVM — пакет, `MainViewModel`, мост `Current`/`StatusText`

**Files:**
- Modify: `src/DitaStudio.App/DitaStudio.App.csproj`
- Create: `src/DitaStudio.App/ViewModels/MainViewModel.cs`
- Modify: `src/DitaStudio.App/MainWindow.xaml.cs`
- Modify: `src/DitaStudio.App/MainWindow.Documents.cs:100-109` (мост `Current`)

**Interfaces:**
- Produces: `MainViewModel` (namespace `DitaStudio.App.ViewModels`) с
  `[ObservableProperty] private string statusText` (публично —
  `StatusText`, тип `string`, значение по умолчанию `"Готово"`) и
  `[ObservableProperty] private DocumentPane? current` (публично — `Current`,
  тип `DocumentPane?` из `DitaStudio.App.Views`, значение по умолчанию `null`).
  `MainWindow` получает `public MainViewModel ViewModel { get; }`.

- [ ] **Step 1: Подключить пакет CommunityToolkit.Mvvm**

Выполнить:
```bash
dotnet add src/DitaStudio.App/DitaStudio.App.csproj package CommunityToolkit.Mvvm
```

Команда сама разрешит актуальную стабильную версию и допишет
`<PackageReference>` в `DitaStudio.App.csproj` рядом с уже существующими
`AvalonEdit`/`Microsoft.Web.WebView2`.

- [ ] **Step 2: Собрать проект, убедиться что пакет подключился**

Выполнить:
```bash
dotnet build DitaStudio.sln -c Debug
```

Ожидается: `Сборка успешно завершена`, 0 ошибок (пакет пока никем не
используется — предупреждений о неиспользуемой зависимости NuGet не даёт).

- [ ] **Step 3: Создать ViewModels/MainViewModel.cs**

Создать папку `src/DitaStudio.App/ViewModels/` и файл в ней:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DitaStudio.App.Views;

namespace DitaStudio.App.ViewModels;

// Корневая VM окна. Пока содержит только сквозное состояние (статус-строка,
// активная вкладка), которое нужно самой первой мигрируемой области (Help).
// Дочерние VM (Help и далее) добавляются по мере миграции соответствующих
// областей — см. docs/superpowers/specs/2026-09-18-mainwindow-mvvm-design.md.
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Готово";

    // До миграции области Documents (шаг 5 спеки) актуальное значение сюда
    // проставляет OnDocumentTabChanged в MainWindow.Documents.cs — это
    // временный мост, а не источник истины.
    [ObservableProperty]
    private DocumentPane? current;
}
```

- [ ] **Step 4: Завести поле ViewModel и выставить DataContext в MainWindow.xaml.cs**

Открыть `src/DitaStudio.App/MainWindow.xaml.cs`. Добавить `using
DitaStudio.App.ViewModels;` к остальным `using`. Добавить поле-свойство
сразу после существующих private-полей (после `_lastOutputDirectory`):

```csharp
    private string? _lastOutputDirectory;

    public MainViewModel ViewModel { get; } = new();
```

В конструкторе — выставить `DataContext` первой строкой (до
`InitializeComponent()`, чтобы биндинги в XAML сразу видели актуальный
контекст при инициализации визуального дерева):

```csharp
    public MainWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        RegisterShortcuts();
        ThemeManager.ApplyTitleBar(this);
        ThemeMenuItem.IsChecked = ThemeManager.Current == ThemeManager.Theme.Dark;
        RefreshRecentProjectsMenu();
        UpdateStatus("Откройте папку с проектом DITA: Файл → Открыть папку проекта.");
    }
```

Заменить тело `UpdateStatus`, чтобы писать в VM вместо именованного
контрола (сигнатура и все ~30 вызовов по проекту не меняются):

```csharp
    private void UpdateStatus(string text) => ViewModel.StatusText = text;
```

- [ ] **Step 5: Перевести StatusBar в MainWindow.xaml на биндинг**

В `src/DitaStudio.App/MainWindow.xaml` заменить:

```xml
        <StatusBar DockPanel.Dock="Bottom">
            <StatusBarItem>
                <TextBlock x:Name="StatusText" Text="Готово" />
            </StatusBarItem>
```

на:

```xml
        <StatusBar DockPanel.Dock="Bottom">
            <StatusBarItem>
                <TextBlock Text="{Binding StatusText}" />
            </StatusBarItem>
```

(`x:Name="StatusText"` убран — после Step 4 в коде на него больше никто не
ссылается; `ContextText` ниже не трогаем, он не связан с этой миграцией.)

- [ ] **Step 6: Мост Current в OnDocumentTabChanged**

В `src/DitaStudio.App/MainWindow.Documents.cs` (строки 100-109):

```csharp
    private void OnDocumentTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, DocumentTabs))
        {
            return;
        }

        ViewModel.Current = Current;
        OnEditorSelectionChanged();
        BuildOutline();
    }
```

(добавлена одна строка `ViewModel.Current = Current;` — `Current` здесь
по-прежнему старое свойство-геттер из `MainWindow.xaml.cs`, читающее
`DocumentTabs.SelectedItem`; замена его источника истины — задача шага 5
спеки, не этого таска.)

- [ ] **Step 7: Собрать и прогнать регресс ядра**

```bash
dotnet build DitaStudio.sln -c Debug
dotnet run --project tests/DitaStudio.Tests
```

Ожидается: сборка без ошибок; в конце вывода теста —
`Пройдено проверок: 238` и `Все проверки пройдены.` (Core не менялся —
если число проверок другое, значит рабочая копия разошлась с этим планом;
проверить `git log` перед тем как продолжать.)

- [ ] **Step 8: UI-smoke — статус-строка всё ещё работает**

```bash
dotnet build DitaStudio.sln -c Debug
"src/DitaStudio.App/bin/Debug/net8.0-windows/DitaStudio.exe" &
dotnet run --project tools/UiHarness -- windows
dotnet run --project tools/UiHarness -- tree --depth 3
```

В выводе `tree` должна быть строка со статус-текстом
`Откройте папку с проектом DITA: Файл → Открыть папку проекта.` — то есть
биндинг `{Binding StatusText}` реально отрисовался. Закрыть приложение:

```bash
dotnet run --project tools/UiHarness -- close
```

- [ ] **Step 9: Commit**

```bash
git add src/DitaStudio.App/DitaStudio.App.csproj src/DitaStudio.App/ViewModels/MainViewModel.cs src/DitaStudio.App/MainWindow.xaml.cs src/DitaStudio.App/MainWindow.xaml src/DitaStudio.App/MainWindow.Documents.cs
git commit -m "Добавить MainViewModel и подключить CommunityToolkit.Mvvm

Инфраструктурный шаг перед миграцией областей MainWindow на MVVM: StatusBar
переведён на биндинг StatusText, Current временно мостится из
OnDocumentTabChanged до миграции области Documents.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 1: Миграция Help → HelpViewModel

**Files:**
- Create: `src/DitaStudio.App/ViewModels/HelpViewModel.cs`
- Modify: `src/DitaStudio.App/ViewModels/MainViewModel.cs`
- Modify: `src/DitaStudio.App/MainWindow.Help.cs`
- Modify: `src/DitaStudio.App/MainWindow.xaml`
- Modify: `src/DitaStudio.App/MainWindow.xaml.cs` (`RegisterShortcuts`)

**Interfaces:**
- Consumes: `MainViewModel.Current` (тип `DocumentPane?`),
  `MainViewModel.StatusText` (тип `string`, сеттер) — из Task 0.
- Produces: `HelpViewModel` с `[RelayCommand] ShowElementHelp()` (публично —
  `ICommand ShowElementHelpCommand`) и `[RelayCommand] About()` (публично —
  `ICommand AboutCommand`). `MainViewModel.Help` типа `HelpViewModel`.

- [ ] **Step 1: Создать ViewModels/HelpViewModel.cs**

```csharp
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DitaStudio.App.Views;
using DitaStudio.Core.Schema;

namespace DitaStudio.App.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HelpViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void ShowElementHelp()
    {
        var node = _main.Current?.Author.CurrentNode;
        if (node is null)
        {
            _main.StatusText = "Поставьте курсор в элемент.";
            return;
        }

        var def = DitaCatalog.Default.Get(node.Name);
        if (def is null)
        {
            Dialogs.Message("Справка", $"Элемент <{node.Name}> отсутствует в словаре DITA 1.3.");
            return;
        }

        var allowed = def.Automaton.AllowedNames;
        var attributes = def.Attributes.Keys.OrderBy(a => a, StringComparer.Ordinal);

        Dialogs.Message($"<{def.Name}>",
            $"{def.Description}\n\n" +
            $"Модуль: {def.Domain}\n" +
            $"@class: {def.ClassAttr}\n\n" +
            $"Содержимое: {def.ModelText}\n\n" +
            $"Допустимые дочерние элементы ({allowed.Count}): {string.Join(", ", allowed.Take(40))}" +
            (allowed.Count > 40 ? "…" : string.Empty) +
            $"\n\nАтрибуты: {string.Join(", ", attributes)}");
    }

    [RelayCommand]
    private void About() => Dialogs.About();
}
```

(Логика и текст сообщений — дословно из старого `MainWindow.Help.cs`,
поведение не меняется.)

- [ ] **Step 2: Подключить HelpViewModel в MainViewModel**

В `src/DitaStudio.App/ViewModels/MainViewModel.cs` добавить свойство и
инициализацию в конструкторе:

```csharp
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Готово";

    [ObservableProperty]
    private DocumentPane? current;

    public HelpViewModel Help { get; }

    public MainViewModel()
    {
        Help = new HelpViewModel(this);
    }
}
```

- [ ] **Step 3: Убрать мигрированные обработчики из MainWindow.Help.cs**

`src/DitaStudio.App/MainWindow.Help.cs` — удалить `OnElementHelp` и
`ShowElementHelp` целиком. Оставить файл с `OnAbout`... нет, `OnAbout`
тоже мигрирован — удалить и его. `OnToggleTheme` **оставить как есть**:
он зависит от `BuildProjectTree`/`BuildMapTree`/`BuildOutline`/
`BuildAttributePanel`/`BuildPalette`/`_panes` — методов и поля,
принадлежащих ещё не мигрированным областям (Project/Map/SidePanels/
Documents). Перенести `OnToggleTheme` в `HelpViewModel` — отдельной
задачей после того, как все перечисленные области мигрируют (после шага 9
спеки, «Map»), не раньше.

Итоговое содержимое файла:

```csharp
using System.Windows;

namespace DitaStudio.App;

// Переключение темы. OnElementHelp/OnAbout мигрированы в HelpViewModel —
// см. ViewModels/HelpViewModel.cs. OnToggleTheme остаётся здесь, пока не
// мигрируют Project/Map/SidePanels/Documents (от них зависят Build*-вызовы
// ниже) — тогда его тоже можно будет перенести.
public partial class MainWindow
{
    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        ThemeManager.ApplyTitleBar(this);

        // Раскрашенные в коде части (дерево проекта, карта, структура, атрибуты, открытые
        // документы в режиме «Автор») не следят за DynamicResource сами по себе — перестраиваем
        // их, чтобы цвета подхватились сразу.
        BuildProjectTree();
        BuildMapTree();
        BuildOutline();
        BuildAttributePanel();
        BuildPalette();

        foreach (var pane in _panes.Values)
        {
            pane.Author.Rebuild();
        }
    }
}
```

(`using DitaStudio.App.Views;` и `using DitaStudio.Core.Schema;` из старого
файла убраны — они были нужны только удалённому коду; `Dialogs`/
`DitaCatalog` здесь больше не используются.)

- [ ] **Step 4: Обновить XAML — меню «Справка»**

В `src/DitaStudio.App/MainWindow.xaml`, секция `<MenuItem Header="_Справка">`
— заменить:

```xml
            <MenuItem Header="_Справка">
                <MenuItem Header="Об элементе под курсором" Click="OnElementHelp" InputGestureText="F1" />
                <MenuItem x:Name="ThemeMenuItem" Header="Тёмная тема" IsCheckable="True" Click="OnToggleTheme" />
                <Separator />
                <MenuItem Header="О программе" Click="OnAbout" />
            </MenuItem>
```

на:

```xml
            <MenuItem Header="_Справка">
                <MenuItem Header="Об элементе под курсором" Command="{Binding Help.ShowElementHelpCommand}" InputGestureText="F1" />
                <MenuItem x:Name="ThemeMenuItem" Header="Тёмная тема" IsCheckable="True" Click="OnToggleTheme" />
                <Separator />
                <MenuItem Header="О программе" Command="{Binding Help.AboutCommand}" />
            </MenuItem>
```

(`ThemeMenuItem`/`OnToggleTheme` не трогаем — см. Step 3.)

- [ ] **Step 5: Обновить горячую клавишу F1 в RegisterShortcuts**

В `src/DitaStudio.App/MainWindow.xaml.cs`, метод `RegisterShortcuts()` —
заменить строку:

```csharp
        Bind(Key.F1, ModifierKeys.None, ShowElementHelp);
```

на:

```csharp
        InputBindings.Add(new KeyBinding(ViewModel.Help.ShowElementHelpCommand, Key.F1, ModifierKeys.None));
```

(Прямой `KeyBinding` на `ICommand` вместо обёртки через `Bind`/
`RoutedCommand` — `ShowElementHelp` как метод `MainWindow` больше не
существует после Step 3, старая строка перестала бы компилироваться.)

- [ ] **Step 6: Собрать и прогнать регресс ядра**

```bash
dotnet build DitaStudio.sln -c Debug
dotnet run --project tests/DitaStudio.Tests
```

Ожидается: 0 ошибок сборки; `Пройдено проверок: 238`, `Все проверки
пройдены.`

- [ ] **Step 7: UI-smoke — справка по элементу и «О программе»**

```bash
"src/DitaStudio.App/bin/Debug/net8.0-windows/DitaStudio.exe" &
dotnet run --project tools/UiHarness -- windows
```

Открыть демо-проект и топик (через меню, т.к. отдельной CLI-команды открытия
папки нет):

```bash
dotnet run --project tools/UiHarness -- menu "Файл|Открыть папку проекта…"
```

(диалог выбора папки — системный `OpenFolderDialog`, FlaUI им не управляет;
дальше путь вводится вручную либо это шаг проверяется руками — отметить
в отчёте о прогоне, если делается не интерактивно). Если проект уже открыт
в запущенном инстансе с предыдущего сеанса — пропустить и сразу:

```bash
dotnet run --project tools/UiHarness -- kbkey F1
dotnet run --project tools/UiHarness -- tree --depth 4
```

Ожидается: в дереве окна виден диалог `Dialogs.Message` (либо с текстом
про элемент, либо, если курсор не в элементе, статус-строка внизу
показывает «Поставьте курсор в элемент.» — проверить `tree`, что строка
статуса обновилась через биндинг, как в Task 0 Step 8). Закрыть диалог,
если открылся:

```bash
dotnet run --project tools/UiHarness -- kbkey Escape
dotnet run --project tools/UiHarness -- menu "Справка|О программе"
dotnet run --project tools/UiHarness -- tree --depth 3
```

Ожидается: диалог «О программе» появился в дереве окна. Закрыть его и
приложение:

```bash
dotnet run --project tools/UiHarness -- kbkey Escape
dotnet run --project tools/UiHarness -- close
```

- [ ] **Step 8: Commit**

```bash
git add src/DitaStudio.App/ViewModels/HelpViewModel.cs src/DitaStudio.App/ViewModels/MainViewModel.cs src/DitaStudio.App/MainWindow.Help.cs src/DitaStudio.App/MainWindow.xaml src/DitaStudio.App/MainWindow.xaml.cs
git commit -m "Мигрировать Help на HelpViewModel (MVVM, шаг 1)

OnElementHelp/ShowElementHelp/OnAbout перенесены в HelpViewModel как
RelayCommand. OnToggleTheme оставлен в code-behind — зависит от Build*
методов ещё не мигрированных областей (Project/Map/SidePanels/Documents).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
