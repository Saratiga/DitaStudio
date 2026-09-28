using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DitaStudio.Core.Diff;
using DitaStudio.Core.Editing;
using DitaStudio.Core.Localization;
using DitaStudio.Core.Model;
using DitaStudio.Core.Plugins;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Core.Schema;
using DitaStudio.Core.Schema.Dtd;
using DitaStudio.Core.Templates;
using DitaStudio.Core.Validation;
using DitaStudio.Docx;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Tests;

// Разбор и запись XML, проверка структуры и стиля.
internal static partial class CoreChecks
{
    internal static void RoundTripTests()
    {
        Section("Разбор и запись XML");

        const string xml = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE task PUBLIC "-//OASIS//DTD DITA Task//EN" "task.dtd">
<task id="install" xml:lang="ru-RU">
  <title>Установка</title>
  <shortdesc>Как установить <keyword>Продукт</keyword> на сервер.</shortdesc>
  <taskbody>
    <steps>
      <step><cmd>Откройте <uicontrol>Параметры</uicontrol> и нажмите <b>Далее</b>.</cmd></step>
    </steps>
  </taskbody>
</task>
""";

        var document = DitaDocument.Parse(xml);
        Check(document.Root.Name == "task", "корневой элемент разобран");
        Check(document.Id == "install", "атрибут @id прочитан");
        Check(document.Title == "Установка", "заголовок получен");
        Check(document.DoctypePublicId == "-//OASIS//DTD DITA Task//EN", "DOCTYPE сохранён");

        var cmd = document.Root.FindDescendant("cmd")!;
        Check(cmd.InnerText.Contains("Откройте") && cmd.InnerText.Contains("Далее"),
            "смешанное содержимое сохранило текст");
        Check(cmd.ElementChildren().Count() == 2, "внутри cmd два фразовых элемента");

        var serialized = document.ToXmlString();
        var reparsed = DitaDocument.Parse(serialized);
        Check(reparsed.Root.FindDescendant("cmd")!.InnerText == cmd.InnerText,
            "текст не изменился после повторного разбора");
        Check(serialized.Contains("<uicontrol>Параметры</uicontrol>"),
            "фразовые элементы записаны в одну строку");
        Check(serialized.Contains("<!DOCTYPE task PUBLIC"), "DOCTYPE записан обратно");

        var escaped = DitaDocument.Parse("<topic id=\"t\"><title>a &amp; b &lt; c</title></topic>");
        Check(escaped.Root.FirstElement("title")!.InnerText == "a & b < c", "сущности раскрыты при чтении");
        Check(escaped.ToXmlString().Contains("a &amp; b &lt; c"), "спецсимволы экранированы при записи");
    }

    internal static void XmlSerializerMiscTests()
    {
        Section("XmlSerializer: Comment/PI, неизвестный элемент, экранирование атрибутов (по отчёту покрытия)");

        Check(XmlSerializer.ToXml(DitaNode.Comment("комментарий")) == "<!--комментарий-->", "комментарий сериализуется как <!--...-->");

        var piWithValue = DitaNode.Pi("xml-stylesheet", "href=\"x.css\"");
        Check(XmlSerializer.ToXml(piWithValue) == "<?xml-stylesheet href=\"x.css\"?>", "PI со значением: '<?имя значение?>'");

        var piNoValue = DitaNode.Pi("foo", string.Empty);
        Check(XmlSerializer.ToXml(piNoValue) == "<?foo?>", "PI без значения — без лишнего пробела: '<?имя?>'");

        var unknownWithText = DitaNode.Element("совершенно-неизвестный-элемент");
        unknownWithText.Add(DitaNode.Text("текст"));
        Check(!XmlSerializer.ToXml(unknownWithText).Contains('\n'),
            "неизвестный элемент каталогу с непустым текстовым ребёнком печатается в одну строку (эвристика IsInlineContainer)");

        var unknownBlockOnly = DitaNode.Element("другой-неизвестный-элемент");
        var innerUnknown = DitaNode.Element("child");
        unknownBlockOnly.Add(innerUnknown);
        Check(XmlSerializer.ToXml(unknownBlockOnly).Contains('\n'),
            "неизвестный элемент только с дочерними элементами (без текста) печатается блочно, с переносами");

        var withNewlineAttr = DitaNode.Element("p");
        withNewlineAttr.SetAttribute("outputclass", "line1\nline2\ttabbed");
        Check(XmlSerializer.ToXml(withNewlineAttr).Contains("line1&#10;line2&#9;tabbed"),
            "перевод строки и таб в значении атрибута экранируются числовыми ссылками");
    }

    // ------------------------------------------------------------- проверка

    internal static void ValidationTests()
    {
        Section("Валидация");
        var validator = new DitaValidator { CheckStyleRules = false };

        var good = DitaDocument.Parse(
            "<concept id=\"c1\"><title>Заголовок</title><conbody><p>Текст</p></conbody></concept>");
        Check(validator.Validate(good).Count == 0, "корректный concept проходит проверку");

        var wrongOrder = DitaDocument.Parse(
            "<concept id=\"c1\"><conbody><p>Текст</p></conbody><title>Заголовок</title></concept>");
        Check(validator.Validate(wrongOrder).Any(i => i.Severity == IssueSeverity.Error),
            "нарушенный порядок элементов найден");

        var unknown = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><paragraph>Текст</paragraph></conbody></concept>");
        Check(validator.Validate(unknown).Any(i => i.Message.Contains("paragraph")),
            "неизвестный элемент найден");

        var badEnum = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><note type=\"неизвестно\">Текст</note></conbody></concept>");
        Check(validator.Validate(badEnum).Any(i => i.Message.Contains("@type")),
            "недопустимое значение перечисления найдено");

        var duplicateIds = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p id=\"x\">A</p><p id=\"x\">B</p></conbody></concept>");
        Check(duplicateIds is not null && validator.Validate(duplicateIds).Any(i => i.Message.Contains("уже используется")),
            "повторяющийся @id найден");

        var textInContainer = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody>Просто текст</conbody></concept>");
        Check(validator.Validate(textInContainer).Any(i => i.Message.Contains("не может содержать текст")),
            "текст в блочном контейнере найден");

        var conrefSkipped = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p conref=\"other.dita#t/p1\"/></conbody></concept>");
        Check(validator.Validate(conrefSkipped).Count == 0, "элемент с conref не проверяется по модели");
    }

    internal static void DitaValidatorMiscTests()
    {
        Section("DitaValidator: атрибуты/EMPTY/незакрытая модель/стилевые правила (по отчёту покрытия)");

        var structural = new DitaValidator { CheckStyleRules = false };

        var reused = DitaDocument.Parse(
            "<topic id=\"t\"><title>T</title><body><p conref=\"a.dita#a/p1\"/><p conkeyref=\"k/p1\"/><p/></body></topic>");
        Check(new DitaValidator().Validate(reused).Count(i => i.Message == "Пустой элемент <p>.") == 1,
            "пустой <p> — предупреждение, а <p conref/conkeyref> без содержимого — нет (текст подставится из ссылки)");

        var unusualRoot = DitaDocument.Parse("<p>Просто абзац как корень.</p>");
        Check(structural.Validate(unusualRoot).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("обычно не используется как корень")),
            "известный, но не topic/map элемент в корне — предупреждение, не ошибка");

        var undeclaredAttr = DitaDocument.Parse("<concept id=\"c\" совершенно-незнакомый-атрибут=\"x\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(structural.Validate(undeclaredAttr).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("не объявлен")),
            "необъявленный атрибут элемента — предупреждение");

        var xmlnsIgnored = DitaDocument.Parse("<concept id=\"c\" xmlns:ditaarch=\"http://dita.oasis-open.org/architecture/2005/\" ditaarch:DITAArchVersion=\"1.3\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(!structural.Validate(xmlnsIgnored).Any(i => i.Message.Contains("не объявлен")),
            "xmlns:*/ditaarch:* атрибуты не считаются необъявленными (пропускаются явно)");

        var missingRequiredAttr = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст <abbreviated-form/> текст</p></conbody></concept>");
        Check(structural.Validate(missingRequiredAttr).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("обязательный атрибут @keyref")),
            "отсутствующий обязательный атрибут (keyref! у abbreviated-form) — ошибка");

        var emptyWithChildren = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст <abbreviated-form keyref=\"k\"><b>x</b></abbreviated-form></p></conbody></concept>");
        Check(structural.Validate(emptyWithChildren).Any(i => i.Message.Contains("должен быть пустым")),
            "содержимое у элемента с моделью EMPTY — ошибка");

        var incompleteContent = DitaDocument.Parse("<task id=\"t\"><title>T</title><taskbody><steps><step></step></steps></taskbody></task>");
        Check(structural.Validate(incompleteContent).Any(i => i.Message.Contains("неполное") && i.Message.Contains("cmd")),
            "пустой <step/> (нужен обязательный cmd) — 'содержимое неполное', а не 'недопустим в этой позиции'");

        // --- стилевые правила (CheckStyleRules по умолчанию true — во всех остальных тестах их
        // намеренно выключают, поэтому здесь единственное прямое покрытие ValidateStyle/*).
        var styled = new DitaValidator();

        var noId = DitaDocument.Parse("<concept><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(styled.Validate(noId).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("нет атрибута @id")),
            "топик без @id — предупреждение стиля");

        var glossTitleFallback = DitaDocument.Parse("<glossentry id=\"g\"><glossterm></glossterm><glossdef>Определение.</glossdef></glossentry>");
        Check(styled.Validate(glossTitleFallback).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("Пустой заголовок")),
            "glossentry без текста в glossterm — пустой заголовок (тот же путь, что title)");

        var withAbstractNoShortdesc = DitaDocument.Parse("<concept id=\"c\"><title>T</title><abstract><p>Реферат.</p></abstract><conbody><p>Текст</p></conbody></concept>");
        Check(!styled.Validate(withAbstractNoShortdesc).Any(i => i.Message.Contains("shortdesc")),
            "abstract присутствует — предупреждение про отсутствие shortdesc не выдаётся, даже если самого shortdesc нет");

        var noShortdescNoAbstract = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        Check(styled.Validate(noShortdescNoAbstract).Any(i => i.Severity == IssueSeverity.Info && i.Message.Contains("shortdesc")),
            "ни shortdesc, ни abstract — информационная подсказка");

        var mapRootSkipsTopicStyle = DitaDocument.Parse("""
<map>
  <title>Карта</title>
  <topicref href="x.dita"><linktext></linktext></topicref>
</map>
""");
        var mapIssues = styled.Validate(mapRootSkipsTopicStyle);
        Check(!mapIssues.Any(i => i.Message.Contains("Пустой заголовок топика") || i.Message.Contains("shortdesc")),
            "карта (не topic-тип) — ValidateTopicStyle не запускается вовсе");

        var emptyStyleElements = DitaDocument.Parse("""
<concept id="c">
  <title>T</title>
  <conbody>
    <p></p>
    <table><tgroup cols="1"><tbody><row><entry></entry></row></tbody></tgroup></table>
  </conbody>
</concept>
""");
        var emptyIssues = styled.Validate(emptyStyleElements);
        Check(emptyIssues.Count(i => i.Message.Contains("Пустой элемент")) == 2,
            $"пустые p и entry — по одному предупреждению на каждый: {emptyIssues.Count(i => i.Message.Contains("Пустой элемент"))}");

        var imageNoRefNoAlt = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image/></p></conbody></concept>");
        var imgIssues = styled.Validate(imageNoRefNoAlt);
        Check(imgIssues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("ни @href, ни @keyref")),
            "image без href и keyref — ошибка");
        Check(imgIssues.Any(i => i.Severity == IssueSeverity.Info && i.Message.Contains("альтернативного текста")),
            "image без alt (и без href/keyref) — тоже отдельная информационная подсказка");

        var imageWithAltAttr = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"x.png\" alt=\"описание\"/></p></conbody></concept>");
        Check(!styled.Validate(imageWithAltAttr).Any(i => i.Message.Contains("альтернативного текста")),
            "image с @alt — подсказки про alt нет");

        var imageWithAltChild = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p><image href=\"x.png\"><alt>описание</alt></image></p></conbody></concept>");
        Check(!styled.Validate(imageWithAltChild).Any(i => i.Message.Contains("альтернативного текста")),
            "image с дочерним <alt> тоже гасит подсказку (не только атрибут)");
    }

    internal static void ValidationIssueTests()
    {
        Section("ValidationIssue: SeverityText/Location/ToString (по отчёту покрытия)");

        var doc = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><p>Текст</p></conbody></concept>");
        var node = doc.Root.FindDescendant("p")!;
        node.Line = 7;

        var error = new ValidationIssue(IssueSeverity.Error, "ошибка", node, "file.dita");
        Check(error.SeverityText == "Ошибка", "SeverityText для Error");
        Check(error.Line == 7, "Line берётся из узла");
        Check(error.Location == node.Path, "Location берётся из Path узла");
        Check(error.ToString() == $"Ошибка: ошибка ({node.Path}, строка 7)", $"ToString() с узлом и строкой: '{error}'");

        var warning = new ValidationIssue(IssueSeverity.Warning, "предупреждение", node);
        Check(warning.SeverityText == "Предупреждение", "SeverityText для Warning");
        Check(warning.FilePath is null, "FilePath не задан по умолчанию");

        var info = new ValidationIssue(IssueSeverity.Info, "инфо", null);
        Check(info.SeverityText == "Сведения", "SeverityText для Info (значение по умолчанию switch)");
        Check(info.Line == 0 && info.Location == string.Empty, "Node == null — Line и Location по умолчанию");
        Check(info.ToString() == "Сведения: инфо ()", $"ToString() без узла и без строки не добавляет ', строка N': '{info}'");
    }
}
