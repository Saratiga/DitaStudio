namespace DitaStudio.Core.Schema;

public static partial class CatalogSource
{
    /// <summary>Пролог и метаданные топика.</summary>
    private const string MetadataElements = """
@domain prolog

prolog :: - topic/prolog :: meta :: (author*, source?, publisher?, copyright*, critdates?, permissions?, metadata*, resourceid*, data*) :: %univ-atts; :: Пролог: сведения об авторстве и метаданные
author :: - topic/author :: meta :: (%text.cnt;)* :: %univ-atts; href keyref type(contributor|creator) :: Автор
source :: - topic/source :: meta :: (%text.cnt;)* :: %univ-atts; href keyref :: Источник материала
publisher :: - topic/publisher :: meta :: (%text.cnt;)* :: %univ-atts; href keyref :: Издатель
copyright :: - topic/copyright :: meta :: (copyryear*, copyrholder) :: %univ-atts; type(primary|secondary) :: Сведения об авторских правах
copyryear :: - topic/copyryear :: meta :: EMPTY :: %univ-atts; year! :: Год действия авторских прав
copyrholder :: - topic/copyrholder :: meta :: (%text.cnt;)* :: %univ-atts; :: Правообладатель
critdates :: - topic/critdates :: meta :: (created?, revised*) :: %univ-atts; :: Ключевые даты документа
created :: - topic/created :: meta :: EMPTY :: %univ-atts; date! golive expiry :: Дата создания
revised :: - topic/revised :: meta :: EMPTY :: %univ-atts; modified! golive expiry :: Дата изменения
permissions :: - topic/permissions :: meta :: EMPTY :: %univ-atts; view(all|classified|entitled|internal) :: Ограничения доступа
metadata :: - topic/metadata :: meta :: (audience*, category*, keywords*, prodinfo*, othermeta*, data*) :: %univ-atts; mapkeyref :: Метаданные топика
audience :: - topic/audience :: meta :: EMPTY :: %univ-atts; type(administrator|executive|other|programmer|purchaser|user) othertype job(administering|customizing|evaluating|installing|maintaining|migrating|other|planning|programming|troubleshooting|using) otherjob experiencelevel(expert|general|novice) name :: Целевая аудитория
category :: - topic/category :: meta :: (%text.cnt;)* :: %univ-atts; :: Категория содержимого
keywords :: - topic/keywords :: meta :: (indexterm|keyword)* :: %univ-atts; :: Ключевые слова
prodinfo :: - topic/prodinfo :: meta :: (prodname, vrmlist?, brand*, series*, platform*, prognum*, featnum*, component*) :: %univ-atts; :: Сведения о продукте
prodname :: - topic/prodname :: meta :: (%text.cnt;)* :: %univ-atts; :: Название продукта
vrmlist :: - topic/vrmlist :: meta :: (vrm+) :: %univ-atts; :: Список версий продукта
vrm :: - topic/vrm :: meta :: EMPTY :: %univ-atts; version! release modification :: Версия, выпуск, модификация
brand :: - topic/brand :: meta :: (%text.cnt;)* :: %univ-atts; :: Бренд
series :: - topic/series :: meta :: (%text.cnt;)* :: %univ-atts; :: Серия продукта
prognum :: - topic/prognum :: meta :: (%text.cnt;)* :: %univ-atts; :: Номер программы
featnum :: - topic/featnum :: meta :: (%text.cnt;)* :: %univ-atts; :: Номер функции
component :: - topic/component :: meta :: (%text.cnt;)* :: %univ-atts; :: Компонент продукта
othermeta :: - topic/othermeta :: meta :: EMPTY :: %univ-atts; name! content! :: Произвольная пара «имя — значение»
resourceid :: - topic/resourceid :: meta :: EMPTY :: %univ-atts; id appname appid ux-context-string ux-source-priority(topic-and-map|topic-only|map-only|map-takes-priority|topic-takes-priority) ux-windowref :: Идентификатор ресурса для справочной системы

""";
}
