namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Домены DITA 1.3: оформление, программирование, ПО, интерфейс, утилиты, разметка и др.</summary>
    private const string DomainElements = """
@domain hi-d

b :: + topic/ph hi-d/b :: inline :: (%text.cnt;)* :: %univ-atts; :: Полужирный
i :: + topic/ph hi-d/i :: inline :: (%text.cnt;)* :: %univ-atts; :: Курсив
u :: + topic/ph hi-d/u :: inline :: (%text.cnt;)* :: %univ-atts; :: Подчёркнутый
sup :: + topic/ph hi-d/sup :: inline :: (%text.cnt;)* :: %univ-atts; :: Верхний индекс
sub :: + topic/ph hi-d/sub :: inline :: (%text.cnt;)* :: %univ-atts; :: Нижний индекс
tt :: + topic/ph hi-d/tt :: inline :: (%text.cnt;)* :: %univ-atts; :: Моноширинный текст
line-through :: + topic/ph hi-d/line-through :: inline :: (%text.cnt;)* :: %univ-atts; :: Зачёркнутый
overline :: + topic/ph hi-d/overline :: inline :: (%text.cnt;)* :: %univ-atts; :: Надчёркнутый

@domain pr-d

codeblock :: + topic/pre pr-d/codeblock :: pre :: (#PCDATA|%basic.ph;|coderef)* :: %univ-atts; %display-atts; xml:space(preserve)=preserve outputclass :: Блок исходного кода
codeph :: + topic/ph pr-d/codeph :: inline :: (%text.cnt;)* :: %univ-atts; :: Фрагмент кода в строке
coderef :: + topic/xref pr-d/coderef :: empty :: EMPTY :: %univ-atts; %link-atts; :: Вставка кода из внешнего файла
apiname :: + topic/keyword pr-d/apiname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя элемента API
option :: + topic/keyword pr-d/option :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Параметр командной строки
parmname :: + topic/keyword pr-d/parmname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя параметра
synph :: + topic/ph pr-d/synph :: inline :: (#PCDATA|kwd|var|oper|delim|sep|synph)* :: %univ-atts; :: Фрагмент синтаксиса в строке
kwd :: + topic/keyword pr-d/kwd :: inline :: (%text.cnt;)* :: %univ-atts; keyref importance(optional|required) :: Обязательное ключевое слово синтаксиса
var :: + topic/ph pr-d/var :: inline :: (%text.cnt;)* :: %univ-atts; importance(optional|required) :: Переменная в синтаксисе
oper :: + topic/ph pr-d/oper :: inline :: (%text.cnt;)* :: %univ-atts; importance(optional|required) :: Оператор в синтаксисе
delim :: + topic/ph pr-d/delim :: inline :: (%text.cnt;)* :: %univ-atts; importance(optional|required) :: Разделитель синтаксиса
sep :: + topic/ph pr-d/sep :: inline :: (%text.cnt;)* :: %univ-atts; importance(optional|required) :: Разделитель между элементами синтаксиса
repsep :: + topic/ph pr-d/repsep :: inline :: (%text.cnt;)* :: %univ-atts; importance(optional|required) :: Разделитель повторяющихся элементов
syntaxdiagram :: + topic/fig pr-d/syntaxdiagram :: container :: (title?, (fragment|groupchoice|groupcomp|groupseq|synblk|synnote|synnoteref)*) :: %univ-atts; %display-atts; :: Синтаксическая диаграмма
synblk :: + topic/figgroup pr-d/synblk :: container :: (title?, (fragment|groupchoice|groupcomp|groupseq|synnote|synnoteref)*) :: %univ-atts; :: Блок синтаксиса
fragment :: + topic/figgroup pr-d/fragment :: container :: (title?, (groupchoice|groupcomp|groupseq|synnote|synnoteref)*) :: %univ-atts; :: Именованный фрагмент синтаксиса
fragref :: + topic/xref pr-d/fragref :: inline :: (%text.cnt;)* :: %univ-atts; %link-atts; importance(optional|required) :: Ссылка на фрагмент синтаксиса
groupchoice :: + topic/figgroup pr-d/groupchoice :: container :: (title?, (delim|fragref|groupchoice|groupcomp|groupseq|kwd|oper|repsep|sep|synnote|synnoteref|var)*) :: %univ-atts; importance(optional|required) :: Группа взаимоисключающих элементов
groupcomp :: + topic/figgroup pr-d/groupcomp :: container :: (title?, (delim|fragref|groupchoice|groupcomp|groupseq|kwd|oper|repsep|sep|synnote|synnoteref|var)*) :: %univ-atts; importance(optional|required) :: Группа элементов без пробелов
groupseq :: + topic/figgroup pr-d/groupseq :: container :: (title?, (delim|fragref|groupchoice|groupcomp|groupseq|kwd|oper|repsep|sep|synnote|synnoteref|var)*) :: %univ-atts; importance(optional|required) :: Последовательность элементов синтаксиса
synnote :: + topic/fn pr-d/synnote :: inline :: (%text.cnt;)* :: %univ-atts; callout :: Сноска к синтаксической диаграмме
synnoteref :: + topic/xref pr-d/synnoteref :: empty :: EMPTY :: %univ-atts; %link-atts; :: Ссылка на сноску синтаксиса
parml :: + topic/dl pr-d/parml :: container :: (plentry+) :: %univ-atts; compact(yes|no) :: Список параметров
plentry :: + topic/dlentry pr-d/plentry :: container :: (pt+, pd+) :: %univ-atts; :: Пара «параметр — описание»
pt :: + topic/dt pr-d/pt :: block :: (%text.cnt;)* :: %univ-atts; keyref :: Имя параметра
pd :: + topic/dd pr-d/pd :: block :: (%mixed.cnt;)* :: %univ-atts; :: Описание параметра

@domain sw-d

cmdname :: + topic/keyword sw-d/cmdname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя команды
filepath :: + topic/ph sw-d/filepath :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Путь к файлу
msgblock :: + topic/pre sw-d/msgblock :: pre :: (%text.cnt;)* :: %univ-atts; %display-atts; xml:space(preserve)=preserve :: Блок системного сообщения
msgnum :: + topic/keyword sw-d/msgnum :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Номер сообщения
msgph :: + topic/ph sw-d/msgph :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Текст системного сообщения
systemoutput :: + topic/ph sw-d/systemoutput :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Вывод системы
userinput :: + topic/ph sw-d/userinput :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Ввод пользователя
varname :: + topic/keyword sw-d/varname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя переменной

@domain ui-d

uicontrol :: + topic/ph ui-d/uicontrol :: inline :: (#PCDATA|%basic.ph;|image)* :: %univ-atts; keyref :: Элемент интерфейса: кнопка, поле, пункт меню
menucascade :: + topic/ph ui-d/menucascade :: inline :: (uicontrol)+ :: %univ-atts; keyref :: Последовательность пунктов меню
shortcut :: + topic/keyword ui-d/shortcut :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Горячая клавиша
wintitle :: + topic/keyword ui-d/wintitle :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Заголовок окна или диалога
screen :: + topic/pre ui-d/screen :: pre :: (%text.cnt;)* :: %univ-atts; %display-atts; xml:space(preserve)=preserve :: Снимок текстового экрана

@domain ut-d

imagemap :: + topic/fig ut-d/imagemap :: container :: (image, area*) :: %univ-atts; %display-atts; :: Изображение с активными областями
area :: + topic/figgroup ut-d/area :: container :: (shape, coords, xref) :: %univ-atts; :: Активная область изображения
shape :: + topic/keyword ut-d/shape :: inline :: (#PCDATA)* :: %univ-atts; keyref :: Форма области: rect, circle, poly, default
coords :: + topic/ph ut-d/coords :: inline :: (#PCDATA)* :: %univ-atts; keyref :: Координаты области

@domain markup-d

markupname :: + topic/keyword markup-d/markupname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя элемента разметки
numcharref :: + topic/keyword markup-d/numcharref xml-d/numcharref :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Числовая ссылка на символ
parameterentity :: + topic/keyword markup-d/parameterentity xml-d/parameterentity :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Параметрическая сущность
textentity :: + topic/keyword markup-d/textentity xml-d/textentity :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Текстовая сущность
xmlatt :: + topic/keyword markup-d/xmlatt xml-d/xmlatt :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя XML-атрибута
xmlelement :: + topic/keyword markup-d/xmlelement xml-d/xmlelement :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя XML-элемента
xmlnsname :: + topic/keyword markup-d/xmlnsname xml-d/xmlnsname :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Имя пространства имён XML
xmlpi :: + topic/keyword markup-d/xmlpi xml-d/xmlpi :: inline :: (%text.cnt;)* :: %univ-atts; keyref :: Инструкция обработки XML

@domain hazard-d

hazardstatement :: + topic/note hazard-d/hazardstatement :: container :: (messagepanel, hazardsymbol*) :: %univ-atts; type(attention|caution|danger|notice|warning)=caution :: Предупреждение об опасности
messagepanel :: + topic/sl hazard-d/messagepanel :: container :: (typeofhazard, consequence*, howtoavoid+) :: %univ-atts; :: Панель сообщения об опасности
typeofhazard :: + topic/sli hazard-d/typeofhazard :: block :: (%text.cnt;)* :: %univ-atts; :: Тип опасности
consequence :: + topic/sli hazard-d/consequence :: block :: (%text.cnt;)* :: %univ-atts; :: Последствия
howtoavoid :: + topic/sli hazard-d/howtoavoid :: block :: (%text.cnt;)* :: %univ-atts; :: Как избежать опасности
hazardsymbol :: + topic/image hazard-d/hazardsymbol :: empty :: (alt?) :: %univ-atts; href keyref alt :: Знак опасности

@domain equation-d

equation-inline :: + topic/ph equation-d/equation-inline :: inline :: (%text.cnt;|mathml)* :: %univ-atts; :: Формула в строке текста
equation-block :: + topic/div equation-d/equation-block :: container :: (%mixed.cnt;|equation-number|mathml)* :: %univ-atts; :: Выключная формула
equation-figure :: + topic/fig equation-d/equation-figure :: container :: (title?, desc?, (equation-number|%basic.block;|mathml)*) :: %univ-atts; %display-atts; :: Нумерованная формула с заголовком
equation-number :: + topic/ph equation-d/equation-number :: inline :: (%text.cnt;)* :: %univ-atts; :: Номер формулы

@domain svg-d

svg-container :: + topic/foreign svg-d/svg-container :: block :: ANY :: %univ-atts; :: Контейнер встроенной SVG-графики
svgref :: + topic/xref svg-d/svgref :: empty :: EMPTY :: %univ-atts; %link-atts; :: Ссылка на внешний SVG-файл

@domain mathml-d

mathml :: + topic/foreign mathml-d/mathml :: block :: ANY :: %univ-atts; :: Контейнер MathML
mathmlref :: + topic/xref mathml-d/mathmlref :: empty :: EMPTY :: %univ-atts; %link-atts; :: Ссылка на внешний MathML-файл

@domain abbrev-d

abbreviated-form :: + topic/term abbrev-d/abbreviated-form :: inline :: EMPTY :: %univ-atts; keyref! :: Сокращённая форма термина из глоссария

@domain release-management-d

change-historylist :: + topic/metadata relmgmt-d/change-historylist :: meta :: (change-item*) :: %univ-atts; :: История изменений топика
change-item :: + topic/data relmgmt-d/change-item :: meta :: (change-person|change-organization|change-revisionid|change-request-reference|change-started|change-completed|change-summary)* :: %univ-atts; :: Запись об изменении
change-person :: + topic/data relmgmt-d/change-person :: meta :: (%text.cnt;)* :: %univ-atts; :: Кто внёс изменение
change-organization :: + topic/data relmgmt-d/change-organization :: meta :: (%text.cnt;)* :: %univ-atts; :: Организация, внёсшая изменение
change-revisionid :: + topic/data relmgmt-d/change-revisionid :: meta :: (%text.cnt;)* :: %univ-atts; :: Идентификатор редакции
change-request-reference :: + topic/data relmgmt-d/change-request-reference :: meta :: (change-request-system?, change-request-id?) :: %univ-atts; :: Ссылка на заявку об изменении
change-request-system :: + topic/data relmgmt-d/change-request-system :: meta :: (%text.cnt;)* :: %univ-atts; :: Система учёта заявок
change-request-id :: + topic/data relmgmt-d/change-request-id :: meta :: (%text.cnt;)* :: %univ-atts; :: Номер заявки
change-started :: + topic/data relmgmt-d/change-started :: meta :: (%text.cnt;)* :: %univ-atts; :: Дата начала изменения
change-completed :: + topic/data relmgmt-d/change-completed :: meta :: (%text.cnt;)* :: %univ-atts; :: Дата завершения изменения
change-summary :: + topic/data relmgmt-d/change-summary :: meta :: (%text.cnt;)* :: %univ-atts; :: Краткое описание изменения

""";
}
