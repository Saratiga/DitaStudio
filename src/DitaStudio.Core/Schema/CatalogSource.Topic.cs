namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Базовый модуль topic: структура топика, блоки и фразовые элементы.</summary>
    private const string TopicElements = """
@domain topic

# ------------------------------------------------------------- структура топика
topic :: - topic/topic :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, body?, related-links?, (topic|concept|task|reference|troubleshooting|glossentry|glossgroup)*) :: %univ-atts; :: Универсальный топик — основа всех типов
title :: - topic/title :: block :: (%title.cnt;)* :: %univ-atts; :: Заголовок
titlealts :: - topic/titlealts :: meta :: (navtitle?, searchtitle?, subtitle?) :: %univ-atts; :: Альтернативные заголовки
navtitle :: - topic/navtitle :: block :: (%title.cnt;)* :: %univ-atts; :: Заголовок для оглавления и навигации
searchtitle :: - topic/searchtitle :: block :: (%title.cnt;)* :: %univ-atts; :: Заголовок для поисковых систем
subtitle :: - topic/subtitle :: block :: (%title.cnt;)* :: %univ-atts; :: Подзаголовок
shortdesc :: - topic/shortdesc :: block :: (%text.cnt;)* :: %univ-atts; :: Краткое описание топика (одно-два предложения)
abstract :: - topic/abstract :: container :: (#PCDATA|%basic.ph;|shortdesc|%basic.block;)* :: %univ-atts; :: Расширенное вступление, может содержать shortdesc
body :: - topic/body :: container :: (%body.cnt;)* :: %univ-atts; :: Тело универсального топика
bodydiv :: - topic/bodydiv :: container :: (%basic.block;)* :: %univ-atts; :: Логическая группа блоков в теле топика
section :: - topic/section :: container :: (#PCDATA|title|%basic.ph;|%basic.block;|sectiondiv)* :: %univ-atts; :: Раздел с необязательным заголовком
sectiondiv :: - topic/sectiondiv :: container :: (#PCDATA|%basic.ph;|%basic.block;)* :: %univ-atts; :: Группа содержимого внутри раздела
example :: - topic/example :: container :: (#PCDATA|title|%basic.ph;|%basic.block;)* :: %univ-atts; :: Пример использования
div :: - topic/div :: container :: (%basic.block;)* :: %univ-atts; :: Произвольная группа блоков

# ------------------------------------------------------------------------ блоки
p :: - topic/p :: block :: (%mixed.cnt;)* :: %univ-atts; :: Абзац
note :: - topic/note :: block :: (%mixed.cnt;)* :: %univ-atts; type(attention|caution|danger|fastpath|important|note|notice|other|remember|restriction|tip|trouble|warning)=note othertype spectitle :: Примечание, предупреждение, совет
lq :: - topic/lq :: block :: (%mixed.cnt;)* :: %univ-atts; %link-atts; reftitle :: Длинная цитата
pre :: - topic/pre :: pre :: (%text.cnt;)* :: %univ-atts; %display-atts; xml:space(preserve)=preserve spectitle :: Дословный текст с сохранением форматирования
lines :: - topic/lines :: pre :: (%text.cnt;)* :: %univ-atts; %display-atts; xml:space(preserve)=preserve :: Текст с сохранением переводов строк

# -------------------------------------------------------------------- списки
ul :: - topic/ul :: container :: (li)+ :: %univ-atts; compact(yes|no) spectitle :: Маркированный список
ol :: - topic/ol :: container :: (li)+ :: %univ-atts; compact(yes|no) spectitle :: Нумерованный список
li :: - topic/li :: block :: (%mixed.cnt;)* :: %univ-atts; :: Элемент списка
sl :: - topic/sl :: container :: (sli)+ :: %univ-atts; compact(yes|no) spectitle :: Простой список без маркеров
sli :: - topic/sli :: block :: (%text.cnt;)* :: %univ-atts; :: Элемент простого списка
dl :: - topic/dl :: container :: (dlhead?, dlentry+) :: %univ-atts; compact(yes|no) spectitle :: Список определений
dlhead :: - topic/dlhead :: container :: (dthd?, ddhd?) :: %univ-atts; :: Заголовок списка определений
dthd :: - topic/dthd :: block :: (%title.cnt;)* :: %univ-atts; :: Заголовок колонки терминов
ddhd :: - topic/ddhd :: block :: (%title.cnt;)* :: %univ-atts; :: Заголовок колонки определений
dlentry :: - topic/dlentry :: container :: (dt+, dd+) :: %univ-atts; :: Пара «термин — определение»
dt :: - topic/dt :: block :: (%text.cnt;)* :: %univ-atts; keyref :: Термин
dd :: - topic/dd :: block :: (%mixed.cnt;)* :: %univ-atts; :: Определение термина

# ------------------------------------------------------- рисунки и медиа-объекты
fig :: - topic/fig :: container :: (title?, desc?, (figgroup|%basic.block;|xref)*) :: %univ-atts; %display-atts; spectitle :: Рисунок с заголовком
figgroup :: - topic/figgroup :: container :: (title?, (figgroup|xref|fn|image|keyword)*) :: %univ-atts; :: Группа внутри рисунка
desc :: - topic/desc :: block :: (%text.cnt;)* :: %univ-atts; :: Описание объекта, рисунка или ссылки
image :: - topic/image :: empty :: (alt?, longdescref?) :: %univ-atts; href keyref alt height width align placement(break|inline)=inline scale scalefit(yes|no) :: Изображение
alt :: - topic/alt :: block :: (%text.cnt;)* :: %univ-atts; :: Альтернативный текст изображения
longdescref :: - topic/longdescref :: empty :: EMPTY :: %univ-atts; %link-atts; :: Ссылка на подробное описание изображения
object :: - topic/object :: container :: (desc?, longdescref?, param*, (%basic.block;)*) :: %univ-atts; declare classid codebase data type codetype archive standby height width usemap name tabindex :: Встраиваемый объект
param :: - topic/param :: empty :: EMPTY :: %univ-atts; name! value valuetype(data|ref|object) type :: Параметр встраиваемого объекта
video :: - topic/video :: container :: (desc?, fallback?, video-poster?, media-source*, media-track*) :: %univ-atts; href keyref width height loop(true|false) muted(true|false) autoplay(true|false) controls(true|false) tabindex :: Видео
audio :: - topic/audio :: container :: (desc?, fallback?, media-source*, media-track*) :: %univ-atts; href keyref loop(true|false) muted(true|false) autoplay(true|false) controls(true|false) tabindex :: Аудио
media-source :: - topic/media-source :: empty :: EMPTY :: %univ-atts; href keyref value :: Источник медиафайла
media-track :: - topic/media-track :: empty :: EMPTY :: %univ-atts; href keyref kind(captions|chapters|descriptions|metadata|subtitles) srclang value :: Дорожка субтитров или глав
video-poster :: - topic/video-poster :: empty :: EMPTY :: %univ-atts; href keyref value :: Кадр-заставка видео
fallback :: - topic/fallback :: container :: (%mixed.cnt;)* :: %univ-atts; :: Содержимое, показываемое вместо медиа

# --------------------------------------------------------------------- ссылки
related-links :: - topic/related-links :: container :: (link|linklist|linkpool)* :: %univ-atts; %link-atts; role(ancestor|child|cousin|descendant|friend|next|other|parent|previous|sibling) otherrole collection-type(choice|family|sequence|unordered) duplicates(yes|no) mapkeyref :: Блок связанных ссылок
link :: - topic/link :: block :: (linktext?, desc?) :: %univ-atts; %link-atts; role(ancestor|child|cousin|descendant|friend|next|other|parent|previous|sibling) otherrole :: Связанная ссылка
linktext :: - topic/linktext :: block :: (%title.cnt;)* :: %univ-atts; :: Текст ссылки
linklist :: - topic/linklist :: container :: (title?, desc?, (link|linklist|linkpool)*) :: %univ-atts; %link-atts; collection-type(choice|family|sequence|unordered) duplicates(yes|no) mapkeyref :: Упорядоченный список ссылок
linkpool :: - topic/linkpool :: container :: (link|linklist|linkpool)* :: %univ-atts; %link-atts; collection-type(choice|family|sequence|unordered) duplicates(yes|no) mapkeyref :: Группа ссылок с общими свойствами
xref :: - topic/xref :: inline :: (%text.cnt;|desc)* :: %univ-atts; %link-atts; :: Перекрёстная ссылка
fn :: - topic/fn :: inline :: (%mixed.cnt;)* :: %univ-atts; callout :: Сноска

# ------------------------------------------------------------- фразовые элементы
ph :: - topic/ph :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Универсальная фраза
text :: - topic/text :: inline :: (#PCDATA)* :: :: Текстовый фрагмент без семантики
keyword :: - topic/keyword :: inline :: (#PCDATA|text|tm)* :: %univ-atts; keyref :: Ключевое слово
term :: - topic/term :: inline :: (%title.cnt;)* :: %univ-atts; keyref :: Термин
q :: - topic/q :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Короткая цитата
cite :: - topic/cite :: inline :: (%title.cnt;)* :: %univ-atts; keyref :: Название источника
boolean :: - topic/boolean :: empty :: EMPTY :: %univ-atts; state(yes|no)! :: Логическое значение (устарел в пользу state)
state :: - topic/state :: empty :: EMPTY :: %univ-atts; name! value! keyref :: Именованное значение состояния
tm :: - topic/tm :: inline :: (#PCDATA|tm)* :: %univ-atts; trademark tmowner tmtype(reg|service|tm)! tmclass :: Товарный знак
sort-as :: - topic/sort-as :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Строка сортировки

# ---------------------------------------------------------------- индексирование
indexterm :: - topic/indexterm :: inline :: (#PCDATA|%basic.ph;|indexterm|index-see|index-see-also|index-sort-as)* :: %univ-atts; keyref start end :: Термин предметного указателя
index-see :: + topic/index-base indexing-d/index-see :: inline :: (#PCDATA|%basic.ph;|indexterm)* :: %univ-atts; keyref :: Отсылка «см.»
index-see-also :: + topic/index-base indexing-d/index-see-also :: inline :: (#PCDATA|%basic.ph;|indexterm)* :: %univ-atts; keyref :: Отсылка «см. также»
index-sort-as :: + topic/index-base indexing-d/index-sort-as :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Ключ сортировки указателя

# ------------------------------------------------------------- служебные элементы
draft-comment :: - topic/draft-comment :: block :: (%mixed.cnt;)* :: %univ-atts; author time disposition(accepted|completed|deferred|duplicate|issue|open|rejected|reopened) :: Черновой комментарий (не попадает в публикацию)
required-cleanup :: - topic/required-cleanup :: block :: ANY :: %univ-atts; remap :: Незавершённое содержимое, требующее доработки
data :: - topic/data :: block :: (%mixed.cnt;)* :: %univ-atts; name datatype value href keyref format scope :: Произвольные метаданные внутри содержимого
data-about :: - topic/data-about :: block :: (data*) :: %univ-atts; href keyref format scope :: Метаданные о другом элементе
foreign :: - topic/foreign :: block :: ANY :: %univ-atts; :: Содержимое чужого словаря (SVG, MathML и т.п.)
unknown :: - topic/unknown :: block :: ANY :: %univ-atts; :: Неизвестное содержимое
itemgroup :: - topic/itemgroup :: container :: (%mixed.cnt;)* :: %univ-atts; :: Группа содержимого внутри элемента списка

""";
}
