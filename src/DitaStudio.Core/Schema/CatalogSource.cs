namespace DitaStudio.Core.Schema;

/// <summary>
/// Текстовое описание словаря DITA 1.3. Формат строки:
/// <c>имя :: @class :: отображение :: контент-модель :: атрибуты :: описание</c>.
/// Директивы: <c>@group имя = ...</c> (параметрическая сущность),
/// <c>@domain имя</c> (модуль для группировки в палитре).
/// </summary>
public static partial class CatalogSource
{
    public static string All =>
        Groups + "\n" +
        TopicElements + "\n" +
        TableElements + "\n" +
        MetadataElements + "\n" +
        SpecializationElements + "\n" +
        DomainElements + "\n" +
        MapElements + "\n" +
        LearningElements;

    private const string Groups = """
# ------------------------------------------------------------------ атрибуты
@group univ-atts = id#ID conref conrefend conaction(mark|pushafter|pushbefore|pushreplace) conkeyref props base platform product audience otherprops deliveryTarget importance(obsolete|deprecated|optional|default|low|normal|high|recommended|required|urgent) rev status(new|changed|deleted|unchanged) translate(yes|no) xml:lang dir(lro|ltr|rlo|rtl) outputclass class tcauthor tcdate
@group link-atts = href keyref scope(local|peer|external) type format
@group display-atts = scale(50|60|70|80|90|100|110|120|140|160|180|200) frame(all|bottom|none|sides|top|topbot) expanse(column|page|spread|textline)

@attrdef id :: Уникальный идентификатор элемента внутри топика
@attrdef href :: Ссылка на топик, элемент или внешний ресурс
@attrdef keyref :: Ссылка через ключ, объявленный в карте (keydef)
@attrdef conref :: Повторное использование содержимого по прямой ссылке
@attrdef conkeyref :: Повторное использование содержимого через ключ
@attrdef props :: Пользовательский признак для условной сборки
@attrdef platform :: Условие: платформа
@attrdef product :: Условие: продукт
@attrdef audience :: Условие: аудитория
@attrdef otherprops :: Условие: прочие признаки
@attrdef rev :: Метка редакции для подсветки изменений
@attrdef tcauthor :: Track changes: кто внёс правку (используется вместе со status="new"/"deleted")
@attrdef tcdate :: Track changes: когда внесена правка (ISO-дата)
@attrdef translate :: Переводить ли содержимое элемента
@attrdef outputclass :: Класс для оформления в выходном формате
@attrdef scope :: Расположение цели ссылки
@attrdef format :: Формат цели ссылки (dita, html, pdf...)
@attrdef type :: Тип целевого топика

# ------------------------------------------------------------------ фразовые
@group ph.hi = b|i|u|sup|sub|tt|line-through|overline
@group ph.pr = apiname|codeph|option|parmname|synph
@group ph.sw = cmdname|filepath|msgnum|msgph|systemoutput|userinput|varname
@group ph.ui = menucascade|shortcut|uicontrol|wintitle
@group ph.markup = markupname|numcharref|parameterentity|textentity|xmlatt|xmlelement|xmlnsname|xmlpi
@group ph.base = ph|term|text|keyword|q|boolean|state|cite|tm|xref|image|data|data-about|foreign|unknown|draft-comment|required-cleanup|fn|indexterm|sort-as|abbreviated-form|equation-inline|svg-container|mathml
@group basic.ph = %ph.base;|%ph.hi;|%ph.pr;|%ph.sw;|%ph.ui;|%ph.markup;
@group title.cnt = #PCDATA|%basic.ph;
@group text.cnt = #PCDATA|%basic.ph;

# --------------------------------------------------------------------- блоки
@group block.core = p|lq|note|dl|parml|ul|ol|sl|pre|lines|fig|image|object|table|simpletable|div|draft-comment|required-cleanup|data|data-about|foreign|unknown
@group block.domain = codeblock|msgblock|screen|imagemap|hazardstatement|equation-block|equation-figure|syntaxdiagram|svg-container|mathml|video|audio
@group basic.block = %block.core;|%block.domain;
@group mixed.cnt = #PCDATA|%basic.ph;|%basic.block;
@group body.cnt = %basic.block;|section|example

""";
}
