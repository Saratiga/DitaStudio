namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Модуль обучения (learning and training) DITA 1.3.</summary>
    private const string LearningElements = """
@domain learning

@group lc.body = %basic.block;|section|example|lcInteraction
@group lc.interactions = lcTrueFalse|lcSingleSelect|lcMultipleSelect|lcSequencing|lcMatching|lcHotspot|lcOpenQuestion

learningBase :: - topic/topic learningBase/learningBase :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningBasebody?, related-links?, (topic|concept|task|reference)*) :: %univ-atts; :: Базовый учебный топик
learningBasebody :: - topic/body learningBase/learningBasebody :: container :: (%lc.body;)* :: %univ-atts; :: Тело базового учебного топика
learningOverview :: - topic/topic learningBase/learningBase learningOverview/learningOverview :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningOverviewbody?, related-links?) :: %univ-atts; :: Обзор учебного модуля
learningOverviewbody :: - topic/body learningBase/learningBasebody learningOverview/learningOverviewbody :: container :: (lcAudience*, lcDuration?, lcIntro?, lcObjectives?, lcPrereqs?, lcResources?, section*, example*) :: %univ-atts; :: Тело обзора
learningContent :: - topic/topic learningBase/learningBase learningContent/learningContent :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningContentbody?, related-links?) :: %univ-atts; :: Учебное содержимое
learningContentbody :: - topic/body learningBase/learningBasebody learningContent/learningContentbody :: container :: (lcIntro?, lcObjectives?, (%lc.body;|conbody|refbody|taskbody)*, lcSummary?) :: %univ-atts; :: Тело учебного содержимого
learningSummary :: - topic/topic learningBase/learningBase learningSummary/learningSummary :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningSummarybody?, related-links?) :: %univ-atts; :: Итоги учебного модуля
learningSummarybody :: - topic/body learningBase/learningBasebody learningSummary/learningSummarybody :: container :: (lcObjectives?, lcSummary?, lcReview?, lcNextSteps?, section*, example*) :: %univ-atts; :: Тело итогов
learningAssessment :: - topic/topic learningBase/learningBase learningAssessment/learningAssessment :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningAssessmentbody?, related-links?) :: %univ-atts; :: Проверка знаний
learningAssessmentbody :: - topic/body learningBase/learningBasebody learningAssessment/learningAssessmentbody :: container :: (lcIntro?, lcObjectives?, (%lc.body;)*, lcSummary?) :: %univ-atts; :: Тело проверки знаний
learningPlan :: - topic/topic learningBase/learningBase learningPlan/learningPlan :: topic :: (title, titlealts?, (shortdesc|abstract)?, prolog?, learningPlanbody?, related-links?) :: %univ-atts; :: План учебного курса
learningPlanbody :: - topic/body learningBase/learningBasebody learningPlan/learningPlanbody :: container :: (lcProject?, lcNeedsAnalysis?, lcGapAnalysis?, lcIntervention?, lcTechnical?, section*) :: %univ-atts; :: Тело плана курса

lcIntro :: - topic/section learningBase/lcIntro :: container :: (%mixed.cnt;)* :: %univ-atts; :: Вступление
lcObjectives :: - topic/section learningBase/lcObjectives :: container :: (title?, lcObjectivesStem?, (lcObjectiveGroup|lcObjective|%basic.block;)*) :: %univ-atts; :: Учебные цели
lcObjectivesStem :: - topic/ph learningBase/lcObjectivesStem :: block :: (%text.cnt;)* :: %univ-atts; :: Вводная фраза к списку целей
lcObjectiveGroup :: - topic/div learningBase/lcObjectiveGroup :: container :: (lcObjective|%basic.block;)* :: %univ-atts; :: Группа учебных целей
lcObjective :: - topic/p learningBase/lcObjective :: block :: (%text.cnt;)* :: %univ-atts; :: Учебная цель
lcAudience :: - topic/div learningBase/lcAudience :: container :: (%mixed.cnt;)* :: %univ-atts; :: Целевая аудитория курса
lcDuration :: - topic/div learningBase/lcDuration :: container :: (%mixed.cnt;)* :: %univ-atts; :: Продолжительность
lcPrereqs :: - topic/section learningBase/lcPrereqs :: container :: (%mixed.cnt;)* :: %univ-atts; :: Требования к слушателю
lcResources :: - topic/section learningBase/lcResources :: container :: (%mixed.cnt;)* :: %univ-atts; :: Материалы и ресурсы
lcSummary :: - topic/section learningBase/lcSummary :: container :: (%mixed.cnt;)* :: %univ-atts; :: Итоги
lcReview :: - topic/section learningBase/lcReview :: container :: (%mixed.cnt;)* :: %univ-atts; :: Повторение материала
lcNextSteps :: - topic/section learningBase/lcNextSteps :: container :: (%mixed.cnt;)* :: %univ-atts; :: Что делать дальше
lcProject :: - topic/section learningPlan/lcProject :: container :: (%mixed.cnt;)* :: %univ-atts; :: Сведения о проекте обучения
lcNeedsAnalysis :: - topic/section learningPlan/lcNeedsAnalysis :: container :: (%mixed.cnt;)* :: %univ-atts; :: Анализ потребностей
lcGapAnalysis :: - topic/section learningPlan/lcGapAnalysis :: container :: (%mixed.cnt;)* :: %univ-atts; :: Анализ разрыва в знаниях
lcIntervention :: - topic/section learningPlan/lcIntervention :: container :: (%mixed.cnt;)* :: %univ-atts; :: Меры обучения
lcTechnical :: - topic/section learningPlan/lcTechnical :: container :: (%mixed.cnt;)* :: %univ-atts; :: Технические требования

lcInteraction :: - topic/div learningInteractionBase/lcInteraction :: container :: (%lc.interactions;) :: %univ-atts; :: Интерактивное задание
lcQuestion :: - topic/p learningInteractionBase/lcQuestion :: block :: (%mixed.cnt;)* :: %univ-atts; :: Формулировка вопроса
lcTrueFalse :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcTrueFalse :: container :: (title?, lcQuestion, lcAnswerOptionGroup?, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Вопрос «верно/неверно»
lcSingleSelect :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcSingleSelect :: container :: (title?, lcQuestion, lcAnswerOptionGroup?, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Вопрос с одним верным ответом
lcMultipleSelect :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcMultipleSelect :: container :: (title?, lcQuestion, lcAnswerOptionGroup?, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Вопрос с несколькими верными ответами
lcOpenQuestion :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcOpenQuestion :: container :: (title?, lcQuestion, lcOpenAnswer?, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Открытый вопрос
lcSequencing :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcSequencing :: container :: (title?, lcQuestion, lcSequenceOptionGroup?, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Задание на упорядочивание
lcMatching :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcMatching :: container :: (title?, lcQuestion, lcMatchingPair*, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Задание на сопоставление
lcHotspot :: - topic/div learningInteractionBase/lcInteractionBase learning-d/lcHotspot :: container :: (title?, lcQuestion, image?, lcArea*, lcFeedbackIncorrect?, lcFeedbackCorrect?) :: %univ-atts; :: Задание с активными областями
lcAnswerOptionGroup :: - topic/ul learningInteractionBase/lcAnswerOptionGroup :: container :: (lcAnswerOption+) :: %univ-atts; :: Варианты ответа
lcAnswerOption :: - topic/li learningInteractionBase/lcAnswerOption :: container :: (lcAnswerContent, lcCorrectResponse?, lcFeedback?) :: %univ-atts; :: Вариант ответа
lcAnswerContent :: - topic/p learningInteractionBase/lcAnswerContent :: block :: (%text.cnt;)* :: %univ-atts; :: Текст варианта ответа
lcCorrectResponse :: - topic/data learningInteractionBase/lcCorrectResponse :: meta :: EMPTY :: %univ-atts; :: Отметка правильного варианта
lcOpenAnswer :: - topic/div learningInteractionBase/lcOpenAnswer :: container :: (%mixed.cnt;)* :: %univ-atts; :: Образец ответа на открытый вопрос
lcSequenceOptionGroup :: - topic/ol learningInteractionBase/lcSequenceOptionGroup :: container :: (lcSequenceOption+) :: %univ-atts; :: Элементы для упорядочивания
lcSequenceOption :: - topic/li learningInteractionBase/lcSequenceOption :: container :: (lcSequence?, lcAnswerContent, lcFeedback?) :: %univ-atts; :: Элемент последовательности
lcSequence :: - topic/data learningInteractionBase/lcSequence :: meta :: (%text.cnt;)* :: %univ-atts; :: Правильный порядковый номер
lcMatchingPair :: - topic/div learningInteractionBase/lcMatchingPair :: container :: (lcItem, lcMatchingItem) :: %univ-atts; :: Пара для сопоставления
lcItem :: - topic/p learningInteractionBase/lcItem :: block :: (%text.cnt;)* :: %univ-atts; :: Левый элемент пары
lcMatchingItem :: - topic/p learningInteractionBase/lcMatchingItem :: block :: (%text.cnt;)* :: %univ-atts; :: Правый элемент пары
lcArea :: - topic/figgroup learningInteractionBase/lcArea :: container :: (shape, coords, lcAreaCoords?, lcCorrectResponse?) :: %univ-atts; :: Активная область изображения
lcAreaCoords :: - topic/ph learningInteractionBase/lcAreaCoords :: inline :: (#PCDATA)* :: %univ-atts; :: Координаты активной области
lcFeedback :: - topic/div learningInteractionBase/lcFeedback :: container :: (%mixed.cnt;)* :: %univ-atts; :: Обратная связь по варианту
lcFeedbackCorrect :: - topic/div learningInteractionBase/lcFeedbackCorrect :: container :: (%mixed.cnt;)* :: %univ-atts; :: Обратная связь при верном ответе
lcFeedbackIncorrect :: - topic/div learningInteractionBase/lcFeedbackIncorrect :: container :: (%mixed.cnt;)* :: %univ-atts; :: Обратная связь при неверном ответе

""";
}
