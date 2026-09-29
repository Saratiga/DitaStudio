# DITA Studio — контекст проекта

Настольный редактор технической документации на DITA 1.3. Аналог Oxygen XML Author.
.NET 8. Основная версия — кроссплатформенная на Avalonia UI (`src/DitaStudio.Desktop`:
Windows, Linux, macOS), прежняя WPF-версия (`src/DitaStudio.App`, Windows) сохраняется как
легаси и не удаляется. Интерфейс и комментарии — на русском.

## Команды

```powershell
dotnet build DitaStudio.sln -c Debug          # сборка
dotnet test tests\DitaStudio.Tests          # ~860 проверок ядра (xUnit, 50 разделов)
dotnet run --project src/DitaStudio.Desktop    # запуск редактора (Avalonia, любая ОС)
dotnet test tests/DitaStudio.Desktop.Tests     # интерфейс Avalonia (headless, работает и на Linux)
dotnet run --project src\DitaStudio.App       # легаси-версия WPF
dotnet test tests\DitaStudio.UiTests          # UI-тесты WPF (реальный DitaStudio.exe, Windows)
```

Тестовый проект — xUnit, но проверки пишутся «мягким» `Check(условие, описание)` внутри
разделов `CoreChecks` (partial-класс: `CoreChecks.cs` — сам механизм, разделы — по файлам
`*Checks.cs` по областям: `SchemaChecks`, `DocxChecks`, `RefactorChecks`…): раздел доходит до конца
и падает со списком всех непрошедших проверок. Новый раздел — `internal static void XxxTests()`
в `CoreChecks` + строка `[Fact]` в `CoreTests.cs`. Пометки вроде «git недоступен — раздел
пропущен» — через `Note(...)`, не `Console` (попадают в вывод теста, в CI без кракозябр). Тесты идут последовательно
(`DisableTestParallelization`) — у `CoreChecks` статическое состояние.
Новую логику сопровождать проверкой там же; прогон должен оставаться зелёным.

CI — `.github/workflows/`:
- `ci.yml` — каждый пуш: на Windows сборка `tests/DitaStudio.Tests`, тесты ядра с покрытием (coverlet,
  `tests/DitaStudio.Tests/coverage.runsettings`, отчёт ReportGenerator в Summary и артефакте);
  на Linux — `DitaStudio.Desktop.Tests` headless и `CefPreviewTests` под `xvfb-run`.
  WPF (`src/DitaStudio.App`) и его UI-тесты в CI не собираются и не гоняются.
- `release.yml` — тег `vX.Y.Z` (должен совпадать с `<Version>` в `DitaStudio.Desktop.csproj`):
  тесты интерфейса (Linux) и ядра (Windows), `dotnet publish` Avalonia-версии self-contained
  на раннере каждой ОС (win-x64, linux-x64, osx-x64 — нативный Chromium копируется только при
  публикации на своей ОС, кросс-сборка его теряет), черновик релиза со всеми архивами. WPF в
  релизы не входит (готовых сборок нет). Ручной запуск — пробная сборка без релиза
  (архивы — артефакты прогона).
На Linux три проверки `FileSafetyTests` падают ожидаемо (регистр в путях, root игнорирует
«только для чтения»), а svn-тест требует UTF-8-локаль — целевая платформа Windows.

Демо-проект для ручной проверки: **Файл → Открыть папку проекта** → `samples\GuideSample`.

## Avalonia-версия

Кроссплатформенная версия — `src/DitaStudio.Desktop` (Avalonia 11.3, не 12: из-за
CefGlue); логика окна — общие ViewModel'и `src/DitaStudio.Presentation`, их же использует
WPF `src/DitaStudio.App`. План, решения и история переноса — `docs/AVALONIA_MIGRATION.md`
(все этапы завершены). Новые функции делать в Avalonia-версии и общем коде; паритет меню
и подсказок с WPF проверяет `ParityTests` (WPF ⊆ Avalonia). Дизайн переносится один в один: палитра
`Themes/Palette.axaml` = цвета `Themes/Light|Dark.xaml`, стили `Themes/Controls.axaml`
повторяют WPF-шаблоны. В своих шаблонах части **не называть `PART_…`** — к ним цепляются
стили темы Fluent. Проверка — `tests/DitaStudio.Desktop.Tests` (Avalonia.Headless, снимки
окон в `bin/.../screenshots`, в CI — артефакт `desktop-screenshots`). WPF-версия после
переноса **не удаляется** — остаётся как легаси.

«Автор» в Avalonia (`Desktop/Authoring`) — без RichTextBox: блок = `BlockEditor`
(AvaloniaEdit, одна строка), фразовые элементы — `Presentation/Authoring/InlineContent`
(цепочка элементов на символ; плашка — символ U+FFFC), раскраска и плашки — через
`LineTransformers`/`ElementGenerators`. Текст вперемешку с вложенными блоками (пункт со
вложенным списком, заметка из абзацев) — участки `InlineContent.Segment` между блоками.
Структурные правки перестраивают **только затронутые блоки** (`RefreshChildren`/
`RebuildAround`), полный `Rebuild` — последнее средство: на топике из 500 абзацев он ~1,5 с.
Орфография — WeCantSpell.Hunspell, словари `Desktop/Dictionaries` (ru_RU — BSD, en_US — SCOWL).
Предпросмотр и печать в PDF — встроенный Chromium через CefGlue (`Desktop/Preview`):
`CefHost` запускает его лениво и сообщает причину, если не вышло (тогда внешний браузер);
`UnderlyingBrowser` у контрола защищённый — доступ к хосту через `HostedCefBrowser`.
Настоящий Chromium в тестах — только `DITASTUDIO_CEF_TESTS=1` под `xvfb-run` (`CefPreviewTests`).

## Документация

Пользовательская документация живёт в трёх местах, и их нужно держать согласованными
при добавлении/изменении функций: `README.md` (обзор, сборка, ограничения),
`USAGE.md` (пошаговое руководство по всем пунктам меню) и `samples/DitaStudioGuide`
(то же руководство как DITA-проект, новый топик добавить в `guide.ditamap`). Оба проекта
из `samples/` проверяет тест `SampleProjectsTests`: ноль ошибок и предупреждений валидации,
HTML и DOCX собираются без предупреждений (кроме SVG в DOCX) — после правки прогнать `dotnet test`. Чек-лист тестирования — `docs/TESTPLAN.md`.

## UI-тесты и UiHarness

`tools/DitaStudio.UiAutomation` — общая библиотека (FlaUI/UIA3) поверх уже запущенного
DitaStudio.exe: поиск элементов, клики, меню, чтение текста/состояния. Один источник
истины для двух потребителей:
- `tools/UiHarness` — CLI для ручной проверки (`dotnet run --project tools\UiHarness -- <команда>`,
  список команд — `--help`/без аргументов).
- `tests/DitaStudio.UiTests` — xUnit-обёртка, реальные `Assert` вместо чтения вывода
  консоли; один процесс DitaStudio.exe на весь прогон (`[Collection("DitaStudio App")]`),
  требует собранный `src\DitaStudio.App` заранее.

Известное ограничение (проверено эмпирически, не баг приложения): FlaUI/UIA3 в этой
связке **не видит содержимое внутри вкладок** `TabControl` (LeftTabs/RightTabs/
BottomTabs) — `FindAllDescendants` от корня окна находит заголовки `TabItem`, но не
`ProjectTree`, не кнопки "Обновить"/"Создать…" и вообще ничего внутри активной вкладки,
хотя оно реально отрисовано (проверялось скриншотом). Элементы вне TabControl (меню,
тулбар, статус-бар, диалоги `Dialogs.Shell`) находятся нормально — их и используют
UI-тесты для проверок (например, строка статус-бара "Проект открыт: N файлов..." вместо
прямого обращения к `ProjectTree`). Не тратить время на повторное обнаружение этого при
добавлении новых UI-тестов.

Второе, уже исправленное на этом же материале: `SetProcessDpiAwarenessContext` обязан
выполниться **до** создания первого `UIA3Automation` — иначе (не только клики мимо цели
при масштабе экрана, как было написано изначально, но и) обход автомейшн-дерева внутри
части WPF-панелей рвётся молча. В `UiDriver` это статический конструктор, а не
конструктор экземпляра — поле `_automation` иначе инициализируется раньше.

Модальные диалоги (`Dialogs.Shell`, например «О программе») иногда не попадают в
`Application.GetAllTopLevelWindows` — FlaUI отдаёт их как узел `ControlType.Window`
внутри дерева окна-владельца, а не отдельным окном рабочего стола.
`UiDriver.ResolveWindow` ищет и там, и там; это тоже не нужно передиагностировать заново.

## Устройство

```
src/DitaStudio.Core/     ядро, не знает про интерфейс — переиспользуемо в консоли и CI
  Model/                 DOM (DitaNode, DitaDocument), разбор и запись XML
  IO/                    AtomicFile, FileStamp, RecoveryStore — защита от потери данных
  Schema/                каталог DITA 1.3, контент-модели, конечный автомат допустимости
  Validation/            проверка структуры, атрибутов, редакторского стиля
  Project/               папка проекта, ditamap, ключи, conref/keyref, поиск
  Publishing/            HTML-генератор, подписи, стили, печать в PDF
  Editing/               структурные операции и история отмены
  Templates/             заготовки новых документов
src/DitaStudio.Presentation/ общие ViewModel'и и сервисы обеих оболочек, модель блока «Автора»,
                         орфография, автодополнение, плагины команд «Автора»
src/DitaStudio.Desktop/  основная версия на Avalonia (Authoring, Preview, Views, Services, Themes)
src/DitaStudio.App/      легаси WPF
  Authoring/             режим «Автор» (AuthorView, InlineEditor), редактор XML на AvalonEdit
  Views/                 вкладка документа, диалоги
tests/DitaStudio.Tests/  проверки ядра (xUnit)
tests/DitaStudio.Desktop.Tests/ headless-тесты Avalonia-версии (+ паритет с WPF, + CefPreviewTests)
tests/DitaStudio.UiTests/ автоматизированные UI-тесты WPF (xUnit + FlaUI, см. ниже)
tools/UiHarness/         CLI для ручной UI-проверки (тот же FlaUI-драйвер)
tools/DitaStudio.UiAutomation/ общая FlaUI-библиотека для двух пунктов выше
samples/GuideSample/     учебный DITA-проект
```

Зависимости Avalonia-версии: **Avalonia 11.3**, **AvaloniaEdit** (исходный XML и редактор
блока «Автора»), **CefGlue.Avalonia** (встроенный Chromium: предпросмотр, PDF),
**WeCantSpell.Hunspell** (орфография); WPF — **AvalonEdit** и **Microsoft.Web.WebView2**. Ядро сейчас без NuGet-зависимостей, но это больше не жёсткое правило —
решение снято при добавлении поддержки внешних DTD (см. ниже): если для следующей
задачи понадобится библиотека, добавлять её не запрещено.

## Словарь DITA

Элементы описаны текстом в `Core/Schema/CatalogSource*.cs`, формат строки:

```
имя :: @class :: отображение :: контент-модель :: атрибуты :: описание
```

Директивы `@group имя = ...` (параметрическая сущность как в DTD), `@domain имя`,
`@attrdef имя :: описание`. Контент-модели разбирает `ModelParser`, затем
`ModelAutomaton` (НКА) отвечает на три вопроса: валидна ли последовательность детей,
что можно вставить в позицию N, что здесь ожидалось. На этом же автомате построены
палитра вставки, автодополнение и валидатор — новый элемент достаточно описать одной
строкой, остальное подхватится само.

Внешние DTD (кастомные специализации) тоже загружаются — `Core/Schema/Dtd/`:
`DtdReader` разбирает настоящий DTD-синтаксис (ENTITY/ELEMENT/ATTLIST, включая
параметрические сущности и внешние SYSTEM/PUBLIC-подключения модулей), контент-модель
отдаёт уже существующему `ModelParser` (синтаксис совпадает), `DtdCatalogLoader` строит
`ElementDef`. Каталоги per-project: `DitaCatalog.Builtin` неизменяем (Merge в него
бросает исключение), `DitaProject.Catalog` = `Builtin.WithElements(элементы DTD)`
(`LoadCatalog()` перечитывает DTD), а `DitaCatalog.Default` — указатель на каталог
активного проекта, его переключает `ProjectViewModel` через `DitaCatalog.Activate(...)`
при открытии/обновлении проекта и подключении/отключении DTD. Код с доступом к проекту
(ValidateAll, HtmlRenderer, DocxRenderer) берёт `project.Catalog`, остальной — `Default`.
`DitaProject.ExternalDtdPath`/`ResolveExternalDtd()` — привязка DTD к проекту,
персистентно, как `.ditaval`. DisplayKind для внешних элементов выводится
эвристически из `@class`-конвенции специализации DITA, а не задаётся явно.

## Грабли, на которые уже наступали

- **`System.IO` не входит в implicit usings WPF-проектов.** В `DitaStudio.App.csproj`
  стоит `<Using Include="System.IO" />` — не удалять, иначе разом отвалятся
  `Path`/`File`/`Directory`.
- **`GridViewColumn` принимает `DisplayMemberBinding`, а не `DisplayMemberPath`.**
  Ошибка вылезает только на этапе компиляции XAML, после того как C# уже собрался.
- **`@class` в каталоге читается через `Trim()`**, поэтому проверки вида
  `Contains("/map ")` с хвостовым пробелом не работают. Сравнивать без пробелов.
- **`keyref` подставляет текст только в пустой элемент** — так по спецификации DITA.
  `<uicontrol keyref="k">свой текст</uicontrol>` оставляет собственный текст.
- **Внешние процессы (git, svn, браузер для PDF): stdout и stderr вычитывать одновременно**
  (`ReadToEndAsync` на оба) и ждать с таймаутом — невычитанный stderr переполняет буфер канала
  и вешает обе стороны. Кодировку вывода задавать явно (UTF-8). Путь для svn — с завершающим
  `@`, иначе `logo@2x.dita` читается как peg-ревизия.
- **`MaxCharactersFromEntities` не ставить в 0** (это «без предела») — предел
  `DitaDocument.MaxEntityCharacters` защищает от «billion laughs» при открытии файла.
- **PowerShell 5.1 читает UTF-8 без BOM как CP1251**, длинное тире превращается в
  «умную» кавычку и ломает разбор скрипта. `.ps1` в репозитории — только ASCII.

## Режим «Автор» в легаси WPF: как он устроен

(Avalonia-версия — см. «Avalonia-версия» выше.) Блочные элементы рисуются отдельными `InlineEditor` (наследник `RichTextBox` на один
абзац). Фразовые элементы хранятся как оформленные прогоны с `Tag = цепочка узлов
DITA`, при записи цепочки восстанавливают исходную вложенность. Структурные операции
(Enter, Backspace, Tab) идут через `EditCommands` и проверяются по контент-модели —
редактор не должен позволять получить невалидный DITA.

Отмена — снимками XML (`UndoStack`), только для структурных правок; набор текста
отменяется средствами самого `RichTextBox`.

Вкладка «Предпросмотр» (`DocumentPane`) умеет показывать три формата: HTML (как
есть), PDF (настоящая печать через `WebView2PdfExporter` во временный файл — тот
же встроенный просмотрщик WebView2 показывает реальную пагинацию, не имитацию) и
DOCX-приближённо (тот же HTML со скином `Assets.WordPreviewCss`, подобранным по
`DocxPublisher.AddStyles` — не пиксель-в-пиксель, но структурно похоже: чёрные
жирные заголовки вместо цветных, note серой полосой без заливки).

## Сохранность данных

- Все записи проектных файлов (`DitaDocument.Save`, `.ditaval`, `.ditastudio-*`) идут через
  `Core/IO/AtomicFile` — временный файл `.~dita-*.tmp` в той же папке + `File.Replace`.
  Новую запись файлов пользователя делать так же, не через `File.WriteAllText`:
  текст — `WriteAllText`, поток (`XDocument.Save`) — `Write`, библиотека, которая сама
  открывает файл по пути (OpenXML/DOCX), — `WriteVia` (получает путь временного файла).
  Так же пишутся экспорт XLIFF, DOCX, шаблон CSS, настройки темы и недавних проектов.
- `DitaDocument.DiskStamp` (время записи + размер) запоминается при чтении и сохранении;
  `HasChangedOnDisk()` отличает чужую запись от своей. `App/ExternalChangeWatcher` —
  `FileSystemWatcher` на папку проекта + проверка при активации окна.
- `App/AutoRecovery` раз в 30 с пишет несохранённые вкладки в `RecoveryStore`
  (`%LOCALAPPDATA%\DitaStudio\Recovery\<проект>-<хэш>`), удаляет копию при сохранении
  или явном отказе, предлагает восстановление в `ProjectViewModel.LoadProject`.
- `DocumentsViewModel.ConfirmClose` возвращает false, если сохранить не удалось, —
  окно или смена проекта тогда отменяются.

## Оформление DOCX

- Экспорт ставит **именованные стили Word**, а не прямое форматирование. Каталог стилей и
  селекторы HTML, которые на них переводятся, — `Docx/Styling/DocxStyleCatalog.cs`;
  оформление по умолчанию в нём повторяет прежний вид экспорта.
- `DocxStyleSheet.FromCss(css)` разбирает пользовательский CSS проекта (`CssParser`,
  `CssApplier`): каскад по специфичности, `@media print|docx`, `@page`, переменные
  `var(--x)`. Неизвестный класс → правило для `outputclass` и имени элемента →
  производный стиль «база_класс» (`DocxRenderer.Styling.cs`).
- Маркеры списков — не стили, а abstractNum: `ul`/`ul ul`/`li::marker` → `DocxStyleSheet.BulletMarker(ilvl)`,
  классы (`.dash`) → `MarkerForClasses` → свой abstractNum в `DocxRenderer.CustomBulletAbstract`
  (вставляется перед первым `w:num` — так требует схема).
- `p` → стиль BodyText, а не Normal: Normal — база всех стилей, в него идут только
  наследуемые свойства `body`.
- Вёрстка, которую CSS не выражает, — `Core/Publishing/DocxLayout` (`.ditastudio-docx`,
  диалог `Dialogs.DocxLayoutSettings`).
- Порядок дочерних элементов rPr/pPr/tcPr/settings в OOXML строгий — `DocxPropsWriter`
  выдаёт их уже упорядоченными. Каждый DOCX в тестах проходит `OpenXmlValidator`
  (`CheckValidDocx`) — новые экспорты проверять так же.
- Символы маркеров из области частного использования Unicode (U+F0B7) писать только
  escape-последовательностью: при чтении и перезаписи файла они незаметно теряются.

## Плагины

`Core/Plugins/PluginLoader.Load<T>(directory)` — обобщённый загрузчик, каждая .dll
из папки грузится в свой `AssemblyLoadContext`; сам контракт (интерфейс) резолвится
через возврат `null` из `Load`, откатываясь на уже загруженную в хосте сборку —
иначе `typeof(T).IsAssignableFrom(type)` не сработает через границу сборки. Автор
плагина ссылается на `DitaStudio.Core.dll`/`DitaStudio.Presentation.dll` с `Private=false`
(не копировать в свой вывод) — см. `tests/TestPlugin` как пример.

Три контракта: `IValidationRulePlugin` (Core/Validation, доп. правила стиля —
`DitaProject.ValidateAll(plugins)`), `IPublishFormatPlugin` (Core/Publishing, свой
формат вывода), `IAuthorCommandPlugin` (Presentation/Plugins, команда режима «Автор» —
получает `IDocumentView`, поэтому один плагин работает в обеих оболочках).
Загружаются один раз при старте (`App.axaml.cs`/`App.xaml.cs` → `PluginRegistry.Load`) из
`plugins/` рядом с exe. UI — один и тот же пункт меню на любое число плагинов
конкретного вида, выбор через `Dialogs.PickOne`. Падение плагина (при загрузке или
выполнении) не роняет приложение — превращается в предупреждение/сообщение.

## Что осталось незакрытым

- Живая правка текста внутри самой плашки в режиме «Автор» (например, alt-текст
  картинки) — только через исходный код, не прямо в плашке.
- PDF собирается печатью HTML (встроенный Chromium, Edge/Chrome, в WPF — WebView2); своего движка нет.
- Легаси WPF: WebView2 требует Evergreen Runtime, запасного пути нет.
- macOS-сборка только x64 (CefGlue не выпускает Chromium под arm64), не подписана.
- Грамматика не проверяется — только орфография (Hunspell в Avalonia, `SpellCheck` в WPF).
- XLIFF-экспорт — по одному документу, без пакетного экспорта всей карты/проекта.
- Git/SVN — только просмотр diff, коммит/пуш из редактора нет.
- Совместное редактирование и серверная часть отсутствуют — однопользовательский десктоп.
