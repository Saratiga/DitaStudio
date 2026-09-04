namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Карты: map, bookmap и subjectScheme.</summary>
    private const string MapElements = """
@domain map

@group map.refs = topicref|topichead|topicgroup|keydef|mapref|topicset|topicsetref|anchor|anchorref|navref|glossref|ditavalref
@group topicref-atts = %link-atts; navtitle query copy-to collection-type(choice|family|sequence|unordered) processing-role(normal|resource-only) toc(yes|no) print(yes|no|printonly) search(yes|no) chunk locktitle(yes|no) linking(none|normal|sourceonly|targetonly) cascade keys keyscope subjectrefs

map :: - map/map :: map :: (title?, topicmeta?, (%map.refs;|reltable|data|data-about)*) :: %univ-atts; title id#ID anchorref outputclass domains DTDVersion chunk keyscope :: Карта: структура публикации
topicref :: - map/topicref :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Ссылка на топик в карте
topichead :: + map/topicref mapgroup-d/topichead :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Узел оглавления без собственного топика
topicgroup :: + map/topicref mapgroup-d/topicgroup :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Группа ссылок с общими свойствами (не попадает в оглавление)
keydef :: + map/topicref mapgroup-d/keydef :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; keys! :: Объявление ключа
mapref :: + map/topicref mapgroup-d/mapref :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Ссылка на вложенную карту
topicset :: + map/topicref mapgroup-d/topicset :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Именованный набор топиков для повторного использования
topicsetref :: + map/topicref mapgroup-d/topicsetref :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Ссылка на набор топиков
anchor :: - map/anchor :: map :: EMPTY :: %univ-atts; id#ID :: Точка подключения содержимого из другой карты
anchorref :: + map/topicref mapgroup-d/anchorref :: map :: (topicmeta?, (%map.refs;|data|data-about)*) :: %univ-atts; %topicref-atts; :: Ссылка на точку подключения
navref :: - map/navref :: map :: EMPTY :: %univ-atts; mapref :: Ссылка на внешнюю карту навигации
glossref :: + map/topicref glossref-d/glossref :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; keys! :: Ссылка на статью глоссария
topicmeta :: - map/topicmeta :: meta :: (navtitle?, linktext?, searchtitle?, shortdesc?, author*, source?, publisher?, copyright*, critdates?, permissions?, audience*, category*, keywords*, prodinfo*, othermeta*, resourceid*, data*) :: %univ-atts; lockmeta(yes|no) :: Метаданные ссылки на топик

reltable :: - map/reltable :: table :: (title?, topicmeta?, relheader?, relrow+) :: %univ-atts; %topicref-atts; title :: Таблица связей между топиками
relheader :: - map/relheader :: table :: (relcolspec+) :: %univ-atts; :: Шапка таблицы связей
relcolspec :: - map/relcolspec :: table :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Описание колонки таблицы связей
relrow :: - map/relrow :: table :: (relcell*) :: %univ-atts; :: Строка таблицы связей
relcell :: - map/relcell :: table :: (%map.refs;)* :: %univ-atts; %topicref-atts; :: Ячейка таблицы связей

ditavalref :: + map/topicref ditavalref-d/ditavalref :: map :: (ditavalmeta?) :: %univ-atts; %topicref-atts; :: Применение условной фильтрации к ветке карты
ditavalmeta :: + map/topicmeta ditavalref-d/ditavalmeta :: meta :: (navtitle?, dvrResourcePrefix?, dvrResourceSuffix?, dvrKeyscopePrefix?, dvrKeyscopeSuffix?) :: %univ-atts; :: Метаданные условной фильтрации
dvrResourcePrefix :: + topic/data ditavalref-d/dvrResourcePrefix :: meta :: (%text.cnt;)* :: %univ-atts; :: Префикс имени файла результата
dvrResourceSuffix :: + topic/data ditavalref-d/dvrResourceSuffix :: meta :: (%text.cnt;)* :: %univ-atts; :: Суффикс имени файла результата
dvrKeyscopePrefix :: + topic/data ditavalref-d/dvrKeyscopePrefix :: meta :: (%text.cnt;)* :: %univ-atts; :: Префикс области ключей
dvrKeyscopeSuffix :: + topic/data ditavalref-d/dvrKeyscopeSuffix :: meta :: (%text.cnt;)* :: %univ-atts; :: Суффикс области ключей

@domain bookmap

bookmap :: - map/map bookmap/bookmap :: map :: (booktitle?, bookmeta?, frontmatter?, (chapter|part|appendix|appendices)*, backmatter?, reltable*) :: %univ-atts; id#ID outputclass domains chunk keyscope :: Карта книги: главы, части, приложения
booktitle :: - topic/title bookmap/booktitle :: block :: (booklibrary?, mainbooktitle, booktitlealt*) :: %univ-atts; :: Заголовок книги
mainbooktitle :: - topic/ph bookmap/mainbooktitle :: block :: (%title.cnt;)* :: %univ-atts; :: Основной заголовок книги
booktitlealt :: - topic/ph bookmap/booktitlealt :: block :: (%title.cnt;)* :: %univ-atts; :: Альтернативный заголовок
booklibrary :: - topic/ph bookmap/booklibrary :: block :: (%title.cnt;)* :: %univ-atts; :: Серия или библиотека изданий
bookmeta :: - map/topicmeta bookmap/bookmeta :: meta :: (author*, source?, publisher?, publisherinformation?, copyright*, critdates?, permissions?, category*, audience*, keywords*, prodinfo*, othermeta*, bookid?, bookchangehistory?, bookrights?, data*) :: %univ-atts; lockmeta(yes|no) :: Метаданные книги
frontmatter :: + map/topicref bookmap/frontmatter :: map :: (%map.refs;|booklists|notices|dedication|preface|bookabstract|draftintro|colophon)* :: %univ-atts; %topicref-atts; :: Передняя часть книги
backmatter :: + map/topicref bookmap/backmatter :: map :: (%map.refs;|booklists|notices|dedication|colophon|amendments)* :: %univ-atts; %topicref-atts; :: Задняя часть книги
part :: + map/topicref bookmap/part :: map :: (topicmeta?, (%map.refs;|chapter)*) :: %univ-atts; %topicref-atts; :: Часть книги
chapter :: + map/topicref bookmap/chapter :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Глава
appendices :: + map/topicref bookmap/appendices :: map :: (topicmeta?, (%map.refs;|appendix)*) :: %univ-atts; %topicref-atts; :: Раздел приложений
appendix :: + map/topicref bookmap/appendix :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Приложение
preface :: + map/topicref bookmap/preface :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Предисловие
notices :: + map/topicref bookmap/notices :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Правовые уведомления
dedication :: + map/topicref bookmap/dedication :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Посвящение
colophon :: + map/topicref bookmap/colophon :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Выходные сведения
amendments :: + map/topicref bookmap/amendments :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Список изменений
bookabstract :: + map/topicref bookmap/bookabstract :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Аннотация книги
draftintro :: + map/topicref bookmap/draftintro :: map :: (topicmeta?, (%map.refs;)*) :: %univ-atts; %topicref-atts; :: Черновое вступление
booklists :: + map/topicref bookmap/booklists :: map :: (topicmeta?, (abbrevlist|bibliolist|booklist|figurelist|glossarylist|indexlist|tablelist|trademarklist|toc|%map.refs;)*) :: %univ-atts; %topicref-atts; :: Служебные списки книги
toc :: + map/topicref bookmap/toc :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Оглавление
figurelist :: + map/topicref bookmap/figurelist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Список рисунков
tablelist :: + map/topicref bookmap/tablelist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Список таблиц
abbrevlist :: + map/topicref bookmap/abbrevlist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Список сокращений
trademarklist :: + map/topicref bookmap/trademarklist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Список товарных знаков
bibliolist :: + map/topicref bookmap/bibliolist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Библиография
glossarylist :: + map/topicref bookmap/glossarylist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Глоссарий
indexlist :: + map/topicref bookmap/indexlist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Предметный указатель
booklist :: + map/topicref bookmap/booklist :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Произвольный список книги
bookid :: - topic/data bookmap/bookid :: meta :: (bookpartno?, edition?, isbn?, booknumber?, volume?, maintainer?) :: %univ-atts; :: Идентификация книги
bookpartno :: - topic/data bookmap/bookpartno :: meta :: (%text.cnt;)* :: %univ-atts; :: Артикул издания
edition :: - topic/data bookmap/edition :: meta :: (%text.cnt;)* :: %univ-atts; :: Издание
isbn :: - topic/data bookmap/isbn :: meta :: (%text.cnt;)* :: %univ-atts; :: ISBN
booknumber :: - topic/data bookmap/booknumber :: meta :: (%text.cnt;)* :: %univ-atts; :: Номер книги
volume :: - topic/data bookmap/volume :: meta :: (%text.cnt;)* :: %univ-atts; :: Том
maintainer :: - topic/data bookmap/maintainer :: meta :: (person|organization)* :: %univ-atts; :: Ответственный за издание
person :: - topic/data bookmap/person :: meta :: (%text.cnt;)* :: %univ-atts; :: Человек
organization :: - topic/data bookmap/organization :: meta :: (%text.cnt;)* :: %univ-atts; :: Организация
bookrights :: - topic/data bookmap/bookrights :: meta :: (copyrfirst?, copyrlast?, bookowner?, summary?) :: %univ-atts; :: Права на книгу
copyrfirst :: - topic/data bookmap/copyrfirst :: meta :: (year?) :: %univ-atts; :: Первый год действия прав
copyrlast :: - topic/data bookmap/copyrlast :: meta :: (year?) :: %univ-atts; :: Последний год действия прав
year :: - topic/data bookmap/year :: meta :: (%text.cnt;)* :: %univ-atts; :: Год
bookowner :: - topic/data bookmap/bookowner :: meta :: (person|organization)* :: %univ-atts; :: Правообладатель книги
summary :: - topic/data bookmap/summary :: meta :: (%text.cnt;)* :: %univ-atts; :: Краткое пояснение
bookchangehistory :: - topic/data bookmap/bookchangehistory :: meta :: (bookevent*, approved*, reviewed*, edited*, tested*, published*) :: %univ-atts; :: История изменений книги
bookevent :: - topic/data bookmap/bookevent :: meta :: (bookeventtype?, revisionid?, started?, completed?, person*, organization*) :: %univ-atts; :: Событие в истории книги
bookeventtype :: - topic/data bookmap/bookeventtype :: meta :: (%text.cnt;)* :: %univ-atts; :: Тип события
revisionid :: - topic/data bookmap/revisionid :: meta :: (%text.cnt;)* :: %univ-atts; :: Идентификатор редакции
started :: - topic/data bookmap/started :: meta :: (%text.cnt;)* :: %univ-atts; :: Начало работы
completed :: - topic/data bookmap/completed :: meta :: (%text.cnt;)* :: %univ-atts; :: Завершение работы
approved :: - topic/data bookmap/approved :: meta :: (revisionid?, started?, completed?, person*, organization*) :: %univ-atts; :: Утверждение
reviewed :: - topic/data bookmap/reviewed :: meta :: (revisionid?, started?, completed?, person*, organization*) :: %univ-atts; :: Рецензирование
edited :: - topic/data bookmap/edited :: meta :: (revisionid?, started?, completed?, person*, organization*) :: %univ-atts; :: Редактирование
tested :: - topic/data bookmap/tested :: meta :: (revisionid?, started?, completed?, person*, organization*) :: %univ-atts; :: Тестирование
published :: - topic/data bookmap/published :: meta :: (revisionid?, started?, completed?, person*, organization*, publishtype?) :: %univ-atts; :: Публикация
publishtype :: - topic/data bookmap/publishtype :: meta :: (%text.cnt;)* :: %univ-atts; :: Тип публикации
publisherinformation :: - topic/data bookmap/publisherinformation :: meta :: (person|organization|printlocation|published)* :: %univ-atts; :: Сведения об издателе
printlocation :: - topic/data bookmap/printlocation :: meta :: (%text.cnt;)* :: %univ-atts; :: Место печати

@domain subjectScheme

subjectScheme :: - map/map subjectScheme/subjectScheme :: map :: (title?, topicmeta?, (%map.refs;|subjectdef|subjectHead|schemeref|enumerationdef|relatedSubjects|hasInstance|hasKind|hasNarrower|hasPart|hasRelated|subjectRelTable|defaultSubject)*) :: %univ-atts; id#ID outputclass domains :: Схема предметных областей для условной сборки
subjectdef :: + map/topicref subjectScheme/subjectdef :: map :: (topicmeta?, (subjectdef|topicref|data)*) :: %univ-atts; %topicref-atts; :: Определение предметной категории
subjectHead :: + map/topicref subjectScheme/subjectHead :: map :: (subjectHeadMeta?, (subjectdef|subjectHead)*) :: %univ-atts; %topicref-atts; :: Заголовок группы категорий
subjectHeadMeta :: + map/topicmeta subjectScheme/subjectHeadMeta :: meta :: (navtitle?, shortdesc?) :: %univ-atts; :: Метаданные заголовка группы
schemeref :: + map/topicref subjectScheme/schemeref :: map :: (topicmeta?) :: %univ-atts; %topicref-atts; :: Ссылка на другую схему
hasInstance :: + map/topicref subjectScheme/hasInstance :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Отношение «экземпляр»
hasKind :: + map/topicref subjectScheme/hasKind :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Отношение «вид»
hasNarrower :: + map/topicref subjectScheme/hasNarrower :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Отношение «уже по смыслу»
hasPart :: + map/topicref subjectScheme/hasPart :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Отношение «часть»
hasRelated :: + map/topicref subjectScheme/hasRelated :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Отношение «связано»
relatedSubjects :: + map/topicref subjectScheme/relatedSubjects :: map :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Связанные категории
enumerationdef :: + map/topicref subjectScheme/enumerationdef :: map :: (attributedef?, elementdef?, defaultSubject?, subjectdef*) :: %univ-atts; :: Перечисление допустимых значений атрибута
attributedef :: + map/topicref subjectScheme/attributedef :: map :: EMPTY :: %univ-atts; name! :: Атрибут, для которого задаётся перечисление
elementdef :: + map/topicref subjectScheme/elementdef :: map :: EMPTY :: %univ-atts; name! :: Элемент, к которому применяется перечисление
defaultSubject :: + map/topicref subjectScheme/defaultSubject :: map :: EMPTY :: %univ-atts; keyref :: Значение по умолчанию
subjectRelTable :: + map/reltable subjectScheme/subjectRelTable :: table :: (title?, topicmeta?, subjectRelHeader?, subjectRel+) :: %univ-atts; %topicref-atts; :: Таблица связей категорий
subjectRelHeader :: + map/relrow subjectScheme/subjectRelHeader :: table :: (subjectRole+) :: %univ-atts; :: Шапка таблицы связей категорий
subjectRel :: + map/relrow subjectScheme/subjectRel :: table :: (subjectRole+) :: %univ-atts; :: Строка таблицы связей категорий
subjectRole :: + map/relcell subjectScheme/subjectRole :: table :: (topicmeta?, (subjectdef|data)*) :: %univ-atts; %topicref-atts; :: Роль категории в связи

""";
}
