namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Типы топиков DITA 1.3: концепция, задача, справка, устранение неполадок, глоссарий.</summary>
    private const string SpecializationElements = """
@domain concept

concept :: - topic/topic concept/concept :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, conbody?, related-links?, (concept|topic|task|reference|troubleshooting|glossentry|glossgroup)*) :: %univ-atts; :: Концепция — объясняет, что это такое и зачем нужно
conbody :: - topic/body concept/conbody :: container :: (%body.cnt;)* :: %univ-atts; :: Тело концепции
conbodydiv :: - topic/bodydiv concept/conbodydiv :: container :: (%basic.block;)* :: %univ-atts; :: Группа блоков в теле концепции

@domain task

task :: - topic/topic task/task :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, taskbody?, related-links?, (task|topic|concept|reference|troubleshooting|glossentry|glossgroup)*) :: %univ-atts; :: Задача — пошаговая инструкция
taskbody :: - topic/body task/taskbody :: container :: (prereq?, context?, (steps|steps-unordered|steps-informal)?, result?, tasktroubleshooting?, example*, postreq?) :: %univ-atts; :: Тело задачи
prereq :: - topic/section task/prereq :: container :: (%mixed.cnt;)* :: %univ-atts; :: Что нужно сделать до начала
context :: - topic/section task/context :: container :: (%mixed.cnt;)* :: %univ-atts; :: Контекст: зачем и когда выполняется задача
steps :: - topic/ol task/steps :: container :: (stepsection|step)+ :: %univ-atts; :: Пронумерованные шаги
steps-unordered :: - topic/ul task/steps-unordered :: container :: (stepsection|step)+ :: %univ-atts; :: Шаги без определённого порядка
steps-informal :: - topic/section task/steps-informal :: container :: (%mixed.cnt;)* :: %univ-atts; :: Описание действий свободной формой
stepsection :: - topic/li task/stepsection :: block :: (%mixed.cnt;)* :: %univ-atts; :: Подзаголовок внутри последовательности шагов
step :: - topic/li task/step :: container :: (cmd, (info|substeps|tutorialinfo|stepxmp|choicetable|choices|note)*, stepresult?, steptroubleshooting?) :: %univ-atts; importance(optional|required) :: Шаг
cmd :: - topic/ph task/cmd :: block :: (%text.cnt;)* :: %univ-atts; keyref :: Команда шага — что именно нужно сделать
info :: - topic/itemgroup task/info :: block :: (%mixed.cnt;)* :: %univ-atts; :: Пояснение к шагу
substeps :: - topic/ol task/substeps :: container :: (substep+) :: %univ-atts; :: Подшаги
substep :: - topic/li task/substep :: container :: (cmd, (info|tutorialinfo|stepxmp|note)*, stepresult?, steptroubleshooting?) :: %univ-atts; importance(optional|required) :: Подшаг
tutorialinfo :: - topic/itemgroup task/tutorialinfo :: block :: (%mixed.cnt;)* :: %univ-atts; :: Дополнительное пояснение для обучения
stepxmp :: - topic/itemgroup task/stepxmp :: block :: (%mixed.cnt;)* :: %univ-atts; :: Пример к шагу
stepresult :: - topic/itemgroup task/stepresult :: block :: (%mixed.cnt;)* :: %univ-atts; :: Результат шага
steptroubleshooting :: - topic/itemgroup task/steptroubleshooting :: block :: (%mixed.cnt;)* :: %univ-atts; :: Что делать, если шаг не удался
choices :: - topic/ul task/choices :: container :: (choice+) :: %univ-atts; :: Варианты выполнения шага
choice :: - topic/li task/choice :: block :: (%mixed.cnt;)* :: %univ-atts; :: Вариант выполнения
result :: - topic/section task/result :: container :: (%mixed.cnt;)* :: %univ-atts; :: Результат выполнения задачи
tasktroubleshooting :: - topic/section task/tasktroubleshooting :: container :: (%mixed.cnt;)* :: %univ-atts; :: Устранение неполадок по задаче
postreq :: - topic/section task/postreq :: container :: (%mixed.cnt;)* :: %univ-atts; :: Что сделать после выполнения задачи

@domain reference

reference :: - topic/topic reference/reference :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, refbody?, related-links?, (reference|topic|concept|task|troubleshooting|glossentry|glossgroup)*) :: %univ-atts; :: Справка — таблицы, параметры, синтаксис
refbody :: - topic/body reference/refbody :: container :: (section|refsyn|example|table|simpletable|properties|refbodydiv)* :: %univ-atts; :: Тело справочного топика
refbodydiv :: - topic/bodydiv reference/refbodydiv :: container :: (section|refsyn|example|table|simpletable|properties)* :: %univ-atts; :: Группа разделов справки
refsyn :: - topic/section reference/refsyn :: container :: (#PCDATA|title|%basic.ph;|%basic.block;)* :: %univ-atts; spectitle :: Краткий синтаксис

@domain troubleshooting

troubleshooting :: - topic/topic troubleshooting/troubleshooting :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, troublebody?, related-links?, (troubleshooting|topic|concept|task|reference|glossentry|glossgroup)*) :: %univ-atts; :: Устранение неполадки: симптом, причина, решение
troublebody :: - topic/body troubleshooting/troublebody :: container :: (condition?, (troubleSolution+|(cause?, remedy?))) :: %univ-atts; :: Тело топика об устранении неполадки
condition :: - topic/section troubleshooting/condition :: container :: (%mixed.cnt;)* :: %univ-atts; :: Признак проблемы
troubleSolution :: - topic/section troubleshooting/troubleSolution :: container :: (cause?, remedy) :: %univ-atts; :: Пара «причина — решение»
cause :: - topic/section troubleshooting/cause :: container :: (%mixed.cnt;)* :: %univ-atts; :: Причина проблемы
remedy :: - topic/section troubleshooting/remedy :: container :: (title?, responsibleParty?, (steps|steps-unordered|steps-informal)?) :: %univ-atts; :: Решение
responsibleParty :: - topic/ph troubleshooting/responsibleParty :: block :: (%text.cnt;)* :: %univ-atts; :: Кто выполняет решение

@domain glossary

glossentry :: - topic/topic concept/concept glossentry/glossentry :: topic :: (glossterm, glossdef?, prolog?, glossBody?, related-links?, (glossentry|topic|concept|task|reference)*) :: %univ-atts; :: Статья глоссария
glossterm :: - topic/title concept/title glossentry/glossterm :: block :: (%title.cnt;)* :: %univ-atts; :: Термин глоссария
glossdef :: - topic/abstract concept/abstract glossentry/glossdef :: container :: (%mixed.cnt;)* :: %univ-atts; :: Определение термина
glossBody :: - topic/body concept/conbody glossentry/glossBody :: container :: (glossPartOfSpeech|glossStatus|glossProperty|glossSurfaceForm|glossUsage|glossScopeNote|glossSymbol|glossAlt)* :: %univ-atts; :: Дополнительные сведения о термине
glossPartOfSpeech :: - topic/data concept/data glossentry/glossPartOfSpeech :: meta :: EMPTY :: %univ-atts; name value :: Часть речи
glossStatus :: - topic/data concept/data glossentry/glossStatus :: meta :: EMPTY :: %univ-atts; name value :: Статус термина (рекомендован, устарел...)
glossProperty :: - topic/data concept/data glossentry/glossProperty :: meta :: EMPTY :: %univ-atts; name value :: Произвольное свойство термина
glossSurfaceForm :: - topic/p concept/p glossentry/glossSurfaceForm :: block :: (%title.cnt;)* :: %univ-atts; :: Полная форма термина для первого упоминания
glossUsage :: - topic/note concept/note glossentry/glossUsage :: block :: (%mixed.cnt;)* :: %univ-atts; :: Указания по употреблению
glossScopeNote :: - topic/note concept/note glossentry/glossScopeNote :: block :: (%mixed.cnt;)* :: %univ-atts; :: Область применения термина
glossSymbol :: - topic/image concept/image glossentry/glossSymbol :: empty :: (alt?) :: %univ-atts; href keyref alt :: Графическое обозначение термина
glossAlt :: - topic/section concept/section glossentry/glossAlt :: container :: (glossAcronym|glossAbbreviation|glossShortForm|glossSynonym|glossStatus|glossProperty|glossUsage|glossScopeNote|glossSymbol|glossAlternateFor)* :: %univ-atts; :: Альтернативная форма термина
glossAcronym :: - topic/p concept/p glossentry/glossAcronym :: block :: (%title.cnt;)* :: %univ-atts; :: Аббревиатура
glossAbbreviation :: - topic/p concept/p glossentry/glossAbbreviation :: block :: (%title.cnt;)* :: %univ-atts; :: Сокращение
glossShortForm :: - topic/p concept/p glossentry/glossShortForm :: block :: (%title.cnt;)* :: %univ-atts; :: Краткая форма
glossSynonym :: - topic/p concept/p glossentry/glossSynonym :: block :: (%title.cnt;)* :: %univ-atts; :: Синоним
glossAlternateFor :: - topic/xref concept/xref glossentry/glossAlternateFor :: empty :: EMPTY :: %univ-atts; href keyref :: Ссылка на основную форму термина
glossgroup :: - topic/topic concept/concept glossgroup/glossgroup :: topic :: (title, prolog?, (glossentry|glossgroup)*) :: %univ-atts; :: Группа статей глоссария

""";
}
