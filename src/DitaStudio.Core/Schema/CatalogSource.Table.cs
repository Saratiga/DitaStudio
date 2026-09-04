namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Таблицы: CALS-таблица, простая таблица, свойства и таблица вариантов.</summary>
    private const string TableElements = """
@domain table

table :: - topic/table :: table :: (title?, desc?, tgroup+) :: %univ-atts; %display-atts; pgwide(0|1) colsep rowsep rowheader(firstcol|norowheader) orient(land|port) spectitle :: Таблица CALS
tgroup :: - topic/tgroup :: table :: (colspec*, thead?, tbody) :: %univ-atts; cols! colsep rowsep align(center|char|justify|left|right) :: Группа строк и колонок таблицы
colspec :: - topic/colspec :: empty :: EMPTY :: colnum colname colwidth colsep rowsep align(center|char|justify|left|right) char charoff :: Описание колонки
thead :: - topic/thead :: table :: (row+) :: %univ-atts; valign(bottom|middle|top) :: Шапка таблицы
tbody :: - topic/tbody :: table :: (row+) :: %univ-atts; valign(bottom|middle|top) :: Тело таблицы
row :: - topic/row :: table :: (entry+) :: %univ-atts; rowsep valign(bottom|middle|top) :: Строка таблицы
entry :: - topic/entry :: table :: (%mixed.cnt;)* :: %univ-atts; colname namest nameend morerows colsep rowsep rotate(0|1) align(center|char|justify|left|right) valign(bottom|middle|top) char charoff scope(col|row|rowgroup|colgroup) headers :: Ячейка таблицы

simpletable :: - topic/simpletable :: table :: (sthead?, strow+) :: %univ-atts; relcolwidth keycol refcols spectitle :: Простая таблица
sthead :: - topic/sthead :: table :: (stentry+) :: %univ-atts; :: Шапка простой таблицы
strow :: - topic/strow :: table :: (stentry*) :: %univ-atts; :: Строка простой таблицы
stentry :: - topic/stentry :: table :: (%mixed.cnt;)* :: %univ-atts; specentry :: Ячейка простой таблицы

properties :: - topic/simpletable reference/properties :: table :: (prophead?, property+) :: %univ-atts; relcolwidth keycol refcols spectitle :: Таблица свойств (справочный топик)
prophead :: - topic/sthead reference/prophead :: table :: (proptypehd?, propvaluehd?, propdeschd?) :: %univ-atts; :: Шапка таблицы свойств
proptypehd :: - topic/stentry reference/proptypehd :: table :: (%title.cnt;)* :: %univ-atts; specentry :: Заголовок колонки «Тип»
propvaluehd :: - topic/stentry reference/propvaluehd :: table :: (%title.cnt;)* :: %univ-atts; specentry :: Заголовок колонки «Значение»
propdeschd :: - topic/stentry reference/propdeschd :: table :: (%title.cnt;)* :: %univ-atts; specentry :: Заголовок колонки «Описание»
property :: - topic/strow reference/property :: table :: (proptype?, propvalue?, propdesc?) :: %univ-atts; :: Строка таблицы свойств
proptype :: - topic/stentry reference/proptype :: table :: (%text.cnt;)* :: %univ-atts; specentry :: Тип свойства
propvalue :: - topic/stentry reference/propvalue :: table :: (%text.cnt;)* :: %univ-atts; specentry :: Значение свойства
propdesc :: - topic/stentry reference/propdesc :: table :: (%mixed.cnt;)* :: %univ-atts; specentry :: Описание свойства

choicetable :: - topic/simpletable task/choicetable :: table :: (chhead?, chrow+) :: %univ-atts; relcolwidth keycol refcols spectitle :: Таблица вариантов выбора в шаге задачи
chhead :: - topic/sthead task/chhead :: table :: (choptionhd, chdeschd) :: %univ-atts; :: Шапка таблицы вариантов
choptionhd :: - topic/stentry task/choptionhd :: table :: (%title.cnt;)* :: %univ-atts; specentry :: Заголовок колонки «Вариант»
chdeschd :: - topic/stentry task/chdeschd :: table :: (%title.cnt;)* :: %univ-atts; specentry :: Заголовок колонки «Описание»
chrow :: - topic/strow task/chrow :: table :: (choption, chdesc) :: %univ-atts; :: Строка таблицы вариантов
choption :: - topic/stentry task/choption :: table :: (%text.cnt;)* :: %univ-atts; specentry :: Вариант выбора
chdesc :: - topic/stentry task/chdesc :: table :: (%mixed.cnt;)* :: %univ-atts; specentry :: Описание варианта

""";
}
