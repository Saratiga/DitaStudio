# Справочник CSS для публикации

Один файл CSS (**«Публикация → Пользовательский CSS…»**) оформляет все форматы: HTML, PDF (их рисует Chromium, правила действуют как в браузере) и DOCX
(правила переводятся в стили и свойства Word). Правила только для Word кладите в `@media docx { … }`, только для печати — в `@media print { … }`.
Правила CSS главнее диалога «Оформление DOCX» (название и глубина оглавления, колонтитулы, размер страницы).

## Как правила находят элементы

- **Имена тегов HTML** (`p`, `h1…h6`, `li`, `td`, `th`, `tr`, `img`, `figure`, `a`, `pre`) и **имена элементов DITA** (`note`, `entry`, `row`, `image`, `fig`, `xref`…) — оба варианта.
- **Классы** — значение `outputclass` и имя элемента: `.note`, `.warning-box`, `table.blue-frame`.
- **Селекторы**: потомок (`ol li`), ребёнок (`ol > li`), `+`/`~`, атрибуты (`[type="warning"]`, `^=`, `$=`, `*=`, `~=`, `|=`), `:first-child`, `:last-child`,
  `:nth-child()`, `:not()`, `:empty`, `::before`/`::after` (только `content`). Состояния (`:hover`…) в документе не применяются.
- **Каскад**: специфичность, затем порядок в файле; `var(--имя)` работает.
- Селектор, который ничего не нашёл, попадает в «Журнал сборки» предупреждением.

## Стили Word и их селекторы (DOCX)

| Что оформляется | Селекторы |
|---|---|
| Текст по умолчанию | `body`, `html`, `main`, `article`, `:root` |
| Абзац | `p`, `.p` |
| Заголовки | `h1…h6` (уровни), `h1.nonumber` — заголовок без номера |
| Краткое описание | `.shortdesc`, `.abstract` |
| Примечания | `.note`; по типам — `.note.tip`, `.note.important`, `.note.warning`, `.note.danger` (и синонимы `fastpath`, `caution`, `attention`…); метка — `.note .label`, `.note-label` |
| Код | `pre`, `.codeblock`, `.screen`, `.msgblock`; в строке — `code`, `kbd`, `samp`, `.codeph`, `.filepath`, `.userinput`, `.varname`, `.cmdname`… |
| Цитата, черновой комментарий | `blockquote`, `.lq`; `.draft-comment` |
| Заголовок блока | `.block-title`, `.generated-title` |
| Списки | `li`, `.sli`, `.choice`; шаги — `.cmd`, `.step .cmd`, `.info`, `.stepresult`; маркеры — `ul`, `ul ul`, `li::marker`, класс (`.dash`) |
| Список определений | `dt`, `dd` |
| Подписи | рисунок — `.fig-title`, `figcaption`; таблица — `.table-title`, `caption` |
| Ячейки | `td`, `.entry`, `.stentry`; шапка — `th`, `thead td`, `thead th` |
| «Смотрите также» | заголовок — `.related-links h2`, `.related-links .title`; ссылки — `.related-links li`, `.related-link` |
| Ссылки, термины, интерфейс | `a`, `.xref`, `.link`; `.term`, `dfn`; `.uicontrol`, `.wintitle`, `.menucascade > .uicontrol` |
| Сноски | текст — `.fn`, `.footnotes`; знак сноски — `.fn-ref` |
| Титул | `.doc-title`, `.doc-subtitle`, `.doc-author` |
| Колонтитулы (стили абзаца) | `.doc-header`, `.doc-footer` |
| Собственные классы | любой `outputclass` — `.mine`, `p.mine`, `.a.b` — становится стилем «база_класс» |

## Оглавление

Разметка (в HTML и для правил DOCX одна и та же):

```html
<div class="toc">                           <!-- в сайте aside.toc, в едином HTML nav.toc -->
  <h2 class="toc-title">Содержание</h2>
  <ul>
    <li class="lvl1"><span class="num">1</span> <a class="text">Глава</a></li>
    <li class="lvl2"><span class="num">1.1</span> <a class="text">Раздел</a></li>
  </ul>
</div>
```

| Правило | Что делает в DOCX |
|---|---|
| `.toc { … }` | общий вид строк оглавления; шрифт и цвет наследует и название |
| `.toc-title { … }` | название: шрифт, размер, регистр (`text-transform: uppercase` → капитель Word), отступы (`margin`), цвет |
| `.toc .lvl1 { … }` … `.lvl6` | вид строк конкретного уровня; отступ уровня — `margin-left` |
| `.toc .lvl1 > .num { min-width; margin-right }` | «таб» между номером и текстом — висячий отступ строки |
| `.toc-title::before / ::after { content: "…" }`, `.toc-title { content: "…" }` | текст названия (вместо «Содержание») или добавки вокруг него |
| `.toc .lvl4 { display: none }` | убирает 4-й и более глубокие уровни из оглавления |
| `.toc-title { display: none }` | убирает название |
| `.toc { display: none }` | убирает оглавление |

Строки оглавления Word перестраивает при обновлении поля (F9); оформление берётся из стилей «toc 1…6» и «TOC Heading», которые заполняет CSS.

## Таблицы

| Правило | Действие |
|---|---|
| `table`, `td`, `th` | вид всех таблиц документа: рамки, заливка шапки/тела, поля ячеек, ширина |
| `table.имя`, `.имя` (класс — `outputclass` таблицы) | своя таблица: `border` / `border-top|left|bottom|right` — **внешние рамки** (толщина, цвет, стиль, `none`), `background-color` — заливка, `width` — ширина |
| `.имя th`, `.имя td`, `.имя entry`, `thead entry`, `td:first-child`, `tr:nth-child(2n) td` | своя ячейка: рамки сторон (`border-bottom`…), заливка (`background-color`), поля (`padding`) |
| `td`, `th` (общие) | текст ячеек: шрифт, цвет, начертание, выравнивание |

Рамки, заливка и поля из правил для ячеек и таблиц (`border*`, `background-color`, `padding`, `margin`) — свойства **ячейки** и **таблицы**
(`tcBorders`, `shd`, поля ячейки `tcMar`), а не абзацев внутри: в Word не появляются лишние линии над и под текстом ячейки. В стиль
абзаца ячейки из этих правил идёт только оформление текста (шрифт, цвет, начертание, выравнивание).

С шаблоном DOCX таблица может получить **стиль таблицы шаблона** (по классу или общий — см. USAGE, «Шаблон DOCX»): тогда рамки и заливку
по умолчанию даёт стиль, а из CSS пишется только явно заданное для этой таблицы.

Рамки `frame`/`rowsep`/`colsep` в DITA сохраняются: они задают, где линии есть, а CSS — какого они вида.
Пример (внешние голубые рамки 2 px, шапка с заливкой):

```css
table.blue-frame { border: 2px solid #1E90FF; }
.blue-frame th { background-color: #DCEBFA; border-bottom: 2px solid #1E90FF; }
.blue-frame td:first-child { background-color: #F2F7FD; }
```

## Картинки

`image`, `img`, `.fig image`, `image.имя`, `.имя`:

- `width`, `height` — размер (`px`, `pt`, `mm`, `cm`, `in`, `%` от ширины текста); одна из сторон — вторая по пропорции; CSS главнее атрибутов `width`/`height` DITA;
- `max-width`, `max-height` — ограничение;
- `border` — линия вокруг картинки (толщина, цвет): контур самого рисунка в Word, абзац с картинкой рамки не получает.

SVG растеризуется в PNG при экспорте из редактора (подробнее — README). Подпись — `.fig-title`; номер рисунка — «Оформление DOCX…».

## Колонтитулы и страница

```css
@page {
  size: A4; margin: 25mm 20mm;
  @top-left   { content: "Руководство" url("logo.png"); }
  @top-center { content: string(title); font-size: 10pt; text-transform: uppercase; height: 12mm; border-bottom: 0.5pt solid #999; }
  @bottom-right { content: "Стр. " counter(page) " из " counter(pages); font-size: 9pt; color: #555; }
}
```

Поля: `@top-left|center|right`, `@bottom-left|center|right`. Содержимое `content`: строки, `counter(page)`, `counter(pages)`, `string(title)`, `url(картинка)`.
Свойства поля: `font-family`, `font-size`, `font-weight`, `font-style`, `font` (сокращение), `color`, `text-transform: uppercase`, `text-decoration: underline`,
`letter-spacing`, `height` / `min-height` (наименьшая высота области колонтитула; у картинки `height` — её высота), `padding-top` / `padding-bottom` (интервалы над текстом и под ним),
`margin-top` (верхнее поле) / `margin-bottom` (нижнее) — расстояние от края листа, `border-bottom` (верхнее поле) / `border-top` (нижнее) — линия.
Поля в `@media print` и `@media docx` тоже разбираются. Верх из CSS перекрывает верх из диалога, низ — низ.

## Текст

Для текста и абзацев поддержаны: `font-family`, `font-size`, `font-weight`, `font-style`, `color`, `background-color`, `text-align`, `text-indent`, `line-height`,
`margin`/`padding` (интервалы и отступы), `border` (рамки абзаца), `text-transform`, `text-decoration`, `letter-spacing`, `vertical-align` (верхний/нижний индекс),
`page-break-before|after|inside` (и `break-*`), `display: none` (скрытый текст). Свойства, которые Word выразить не может (flex, позиционирование, тени, скругления,
анимации, фоновые картинки), в журнал сборки не мешают и в DOCX не переносятся.

## Чего CSS пока не умеет

- Вёрстка веб-страницы (flex, grid, `position`), тени, скругления — в Word нет.
- Номера страниц в оглавлении и указателе подставляет Word при обновлении полей, а не CSS.
- Свой макет титульной страницы (расположение блоков) — через «Оформление DOCX…», а вид строк — `.doc-title`, `.doc-subtitle`, `.doc-author`.
- Вид отдельных строк предметного указателя: сейчас один стиль.
