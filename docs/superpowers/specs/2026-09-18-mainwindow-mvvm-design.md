# MainWindow → MVVM (CommunityToolkit.Mvvm): дизайн

Дата: 2026-09-18

## Проблема

`MainWindow` — один класс, разложенный на 10 partial-файлов
(`MainWindow.Documents.cs`, `.Insert.cs`, `.Map.cs`, `.Project.cs`,
`.Publish.cs`, `.Search.cs`, `.SidePanels.cs`, `.Validation.cs`, `.Help.cs`,
`.xaml.cs`), суммарно 2517 строк. Все они делят одно общее состояние,
объявленное в `MainWindow.xaml.cs`:

```csharp
private readonly Dictionary<string, DocumentPane> _panes;
private DitaProject? _project;
private MapTree? _mapTree;
private MapItem? _mapDragCandidate;
private Point _mapDragStart;
private Dialogs.ConditionsResult? _conditions;
private string? _lastOutputDirectory;
```

плюс `Current` (активная вкладка) и `UpdateStatus(...)`.

Проверено по коду (не по цифрам графа): бизнес-логика в целом уже корректно
вынесена в Core-сервисы (`RefactorService`, `HtmlPublisher`/`DocxPublisher`,
`DitaProject`) — code-behind в основном оркестрирует диалоги и вызовы.
Реальная проблема — не утечка логики, а то, что состояние и UI-обработчики
не тестируются и не переиспользуются отдельно от `Window`.

## Цель

Перевести `MainWindow` на MVVM (`CommunityToolkit.Mvvm`), решая одним
подходом три связанные боли: тестируемость (VM без WPF), навигацию/понятность
(меньше кода на файл, чёткие границы), задел под будущие фичи (track changes,
мультипроектный workspace), которые проще строить на VM, чем на code-behind.

## Архитектура

- NuGet `CommunityToolkit.Mvvm` — только в `DitaStudio.App.csproj`. `Core`
  остаётся без зависимостей (правило CLAUDE.md не меняется).
- `MainViewModel : ObservableObject` — корень. Композирует дочерние VM,
  держит сквозное состояние: `Current` (производное от выбранной вкладки),
  статус-строку.
- Дочерние VM — один в один с нынешними partial-файлами:
  `ProjectViewModel`, `DocumentsViewModel`, `PublishViewModel`,
  `MapViewModel`, `SearchViewModel`, `ValidationViewModel`,
  `SidePanelsViewModel`. Границы ответственности не меняются — только
  форма (VM вместо partial class + private fields).
- Общие поля переезжают в ту VM, которой концептуально принадлежат:
  `_project`/`_mapTree` → `ProjectViewModel`/`MapViewModel`,
  `_panes` → `DocumentsViewModel`, `_conditions` → `PublishViewModel`,
  `_mapDragCandidate`/`_mapDragStart` → `MapViewModel`.
  Каждое поле — `[ObservableProperty]`.
- Обработчики `OnXxx(object sender, RoutedEventArgs e)` → методы с
  `[RelayCommand]`. XAML: `Click="OnXxx"` → `Command="{Binding XxxCommand}"`.
- Диалоги (`Dialogs.Message/Confirm/RenameFile`, `OpenFolderDialog`,
  `SaveFileDialog`) вызываются из VM напрямую, как статические методы —
  без `IDialogService`-абстракции. Причина: сейчас нет сценария, где нужно
  мокать диалог в тесте; вводить интерфейс ради интерфейса — не YAGNI.
- `RegisterShortcuts()` в `MainWindow.xaml.cs` при миграции соответствующей
  области переключается с лямбд на `KeyBinding(viewModel.XxxCommand, ...)`.
  Отдельным шагом не делается — правится по ходу миграции той области,
  чью команду затрагивает конкретный шорткат.
- Разметка каждой области переезжает в свой `UserControl` в
  `src/DitaStudio.App/Views/` (рядом с уже существующими `DocumentPane`,
  `DiffWindow`, `Dialogs`). `DataContext` дочернего `UserControl`
  проставляется биндингом от родителя: `<views:ProjectView
  DataContext="{Binding ProjectViewModel}"/>`. Без DI-контейнера — прямые
  свойства на `MainViewModel`, минимум кода. См. «Разбивка XAML» ниже.

## Особый случай: Documents / `Current`

Сейчас `Current` — чтение UI-контрола:
`DocumentTabs.SelectedItem is TabItem { Content: DocumentPane pane }`.
При миграции области Documents вводится `TabViewModel` (обёртка над
`DocumentPane` + метаданными вкладки), `DocumentsViewModel.SelectedTab`
становится источником истины, `Current` в `MainViewModel` — производное
свойство от него.

## Особый случай: построение дерева проекта

`MainWindow.Project.BuildProjectTree` руками собирает `TreeViewItem`
(иконка-эмодзи, `ToolTip`, `Tag = ProjectFile`, вложенные `StackPanel`).
Это не биндинг, а императивное построение визуального дерева. При миграции
области Project — заменяется на `HierarchicalDataTemplate` +
`ObservableCollection<ProjectTreeNodeViewModel>`, с сохранением точного
поведения: выделение, двойной клик открывает документ, контекстное меню
по правому клику. Это самая рискованная часть миграции Project — тестируется
через `tools/UiHarness` (открыть/раскрыть дерево, дважды кликнуть по файлу).

## Разбивка XAML на UserControl

`MainWindow.xaml` (287 строк) уже физически размечен по тем же границам,
что и VM — разбивка почти без домыслов:

| Область XAML | Новый UserControl | Чья VM |
|---|---|---|
| Вкладки «Проект» + «Ключи» (левая колонка) | `ProjectView.xaml` | `ProjectViewModel` |
| Вкладка «Карта» (левая колонка) | `MapView.xaml` | `MapViewModel` |
| Вкладка «Проверка» (нижние вкладки) | `ValidationView.xaml` | `ValidationViewModel` |
| Вкладка «Поиск» (нижние вкладки) | `SearchView.xaml` | `SearchViewModel` |
| Вкладка «Журнал сборки» (нижние вкладки) | `BuildLogView.xaml` | `PublishViewModel` |
| Вкладки «Атрибуты»/«Вставка»/«Структура» (правая колонка) | `SidePanelsView.xaml` | `SidePanelsViewModel` (все три вкладки — одна VM, как сейчас один файл) |
| `DocumentTabs` (центр) | остаётся в `MainWindow.xaml`, но `TabControl.ItemsSource="{Binding DocumentsViewModel.Tabs}"` вместо ручного `Items.Add` | `DocumentsViewModel` |

Меню и `ToolBarTray` — сквозные (например, File-меню зовёт команды и
Project, и Documents), остаются в `MainWindow.xaml`, команды биндятся
через точку: `Command="{Binding ProjectViewModel.OpenProjectCommand}"`.
`StatusBar` — тривиален, биндится на `MainViewModel.StatusText`.

У Help нет своего экрана (только пункты меню + диалог «О программе») —
для него `UserControl` не нужен.

## Порядок миграции

От простого/изолированного к сложному/связанному — риск растёт
постепенно:

1. **Help** (62 стр.) — обкатка паттерна: пакет, `DataContext`, первый
   `[RelayCommand]`, первый биндинг. Без `UserControl` (см. выше).
2. **Search** (102) — `SearchView.xaml` + `ObservableCollection` для
   результатов поиска.
3. **Validation** (112) — `ValidationView.xaml`, список проблем с двойным
   кликом (та же форма, что Search).
4. **SidePanels** (344) — `SidePanelsView.xaml`, завязано на `Current` —
   первая проверка связи между VM.
5. **Documents** (295) — `DocumentTabs` переводится на
   `ItemsSource="{Binding Tabs}"`; вводится `TabViewModel` и производный
   `Current` (см. выше).
6. **Project** (259) — `ProjectView.xaml`, дерево (см. выше),
   `LoadProject`.
7. **Publish** (295) — `BuildLogView.xaml`; зависит от `_project`
   (из шага 6) и `_conditions`.
8. **Insert** (493) — самый длинный файл, но по сути десятки однотипных
   `OnInsertXxx` → `[RelayCommand]`. Широко, не глубоко — ниже риск,
   чем размер файла намекает. Своего экрана нет — команды только для
   меню/тулбара, `UserControl` не нужен.
9. **Map** (474) — `MapView.xaml`, последним: drag-drop
   (`_mapDragCandidate`/`_mapDragStart`), reltable-редактор — больше всего
   интерактивного состояния, паттерн к этому моменту обкатан на восьми
   областях.

Каждый шаг — отдельная ветка/коммит: перенос полей+команд в VM → перенос
разметки этой области в новый `AreaView.xaml` (`UserControl`), если он
есть по таблице выше → правка `MainWindow.xaml` (вставить `<views:AreaView
DataContext="{Binding AreaViewModel}"/>` на старое место, командные
биндинги в меню/тулбаре — на точечный путь) → удаление старого
code-behind → сборка + прогон тестов + UI smoke → коммит.

## Тестирование

- **Регресс ядра:** `dotnet run --project tests/DitaStudio.Tests` (238
  проверок) после каждого шага. Core не трогается этой миграцией, но
  прогон дешёвый и ловит случайные ошибки в ссылках между проектами.
- **Юнит-тесты VM:** не вводится отдельный тестовый набор сразу. Ценность
  миграции — принципиальная тестируемость VM без `Window`, а не обязательство
  покрыть их тестами в рамках этой работы. Тест добавляется точечно, когда
  в конкретной VM появляется нетривиальная ветвящаяся логика (по образцу
  `DitaProject.MergeExcludeConditions`).
- **UI smoke на каждый шаг:** `tools/UiHarness` (FlaUI-драйвер, команды
  `tree`/`click`/`type` по уже запущенному `DitaStudio.exe`) — минимальный
  сценарий под мигрированную область, не общий тест-план.
- **Обработка ошибок:** без изменений — `try/catch` → `Dialogs.Message`,
  вызов из `[RelayCommand]`-метода вместо `OnXxx`.
- **Откат:** каждый шаг — отдельный коммит; регресс от smoke откатывает
  один коммит, не всю миграцию.

## Критерий готовности

`MainWindow.xaml.cs` содержит только `DataContext = new MainViewModel()` +
`RegisterShortcuts()` (уже на командах VM). Обработчиков `OnXxx` не
осталось. 9 partial-файлов удалены или пусты.

## Явно не делается (YAGNI)

- `IDialogService`/абстракция диалогов — нет сценария, требующего мокать
  диалог.
- Отдельный тестовый проект/фреймворк для VM — используется существующий
  консольный `tests/DitaStudio.Tests`, если и когда понадобится тест VM.
- Замена `tools/UiHarness` на другой UI-test-фреймворк — уже есть рабочий
  инструмент, новый не нужен.
