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

// Шаблоны документов, структурные правки, таблицы, история отмены.
internal static partial class CoreChecks
{
    internal static void TemplateTests()
    {
        Section("Заготовки документов");
        var validator = new DitaValidator { CheckStyleRules = false };

        foreach (var template in DocumentTemplates.All)
        {
            var document = DocumentTemplates.Create(template.Key, "Проверка");
            var issues = validator.Validate(document);
            Check(issues.Count == 0,
                $"заготовка «{template.DisplayName}» валидна" +
                (issues.Count == 0 ? string.Empty : ": " + issues[0].Message));
        }

        Check(DocumentTemplates.SuggestId("Установка сервера", "task") == "ustanovka_servera",
            "идентификатор транслитерируется из русского заголовка");

        Check(DocumentTemplates.Find("TASK") is { Key: "task" }, "Find регистронезависим");
        Check(DocumentTemplates.Find("no-such-template") is null, "Find возвращает null для неизвестного ключа");

        var fallback = DocumentTemplates.Create("no-such-template", "Заголовок");
        Check(fallback.Root.Name == DocumentTemplates.All[0].RootElement,
            $"Create с неизвестным ключом использует первую заготовку из All: {fallback.Root.Name}");

        var topic = DocumentTemplates.Create("topic", "Заголовок универсального топика");
        Check(topic.Root.Name == "topic", "Create('topic') идёт по ветке default switch (Topic())");

        var t1 = DocumentTemplates.All[0];
        var t2 = t1 with { };
        Check(t1 == t2 && t1.Equals(t2), "DocumentTemplate — record со структурным равенством");
        Check(t1.ToString() == t1.DisplayName, "DocumentTemplate.ToString() возвращает DisplayName");

        Check(DocumentTemplates.SuggestId("...", "topic") == "topic",
            "SuggestId: заголовок без букв/цифр целиком — используется префикс");
        Check(DocumentTemplates.SuggestId("123 сервер", "topic") == "topic_123_server",
            $"SuggestId: результат, начинающийся с цифры, получает префикс спереди: {DocumentTemplates.SuggestId("123 сервер", "topic")}");
        Check(DocumentTemplates.SuggestId("a  --  b", "topic") == "a_b",
            $"SuggestId: подряд идущие пробелы/дефисы схлопываются в один '_': {DocumentTemplates.SuggestId("a  --  b", "topic")}");
        Check(DocumentTemplates.SuggestId("中文 текст", "topic") == "tekst",
            $"SuggestId: символы вне транслит-таблицы и вне ASCII молча пропускаются (вместе с последующим пробелом, раз до него ничего не накопилось): {DocumentTemplates.SuggestId("中文 текст", "topic")}");
        var longTitle = string.Concat(Enumerable.Repeat("word ", 20));
        Check(DocumentTemplates.SuggestId(longTitle, "topic").Length <= 60,
            $"SuggestId обрезает результат до 60 символов: {DocumentTemplates.SuggestId(longTitle, "topic").Length}");
    }

    internal static void EditingTests()
    {
        Section("Редактирование");

        var document = DitaDocument.Parse(
            "<concept id=\"c1\"><title>З</title><conbody><p>Первый абзац</p></conbody></concept>");
        var conbody = document.Root.FirstElement("conbody")!;
        var paragraph = conbody.FirstElement("p")!;

        var second = EditCommands.SplitBlock(paragraph, 6);
        Check(second is not null && paragraph.InnerText == "Первый" && second.InnerText == " абзац",
            "разделение абзаца по позиции курсора");

        var merged = EditCommands.MergeWithPrevious(second!);
        Check(merged is not null && merged.InnerText == "Первый абзац", "объединение абзацев");

        var inserted = EditCommands.InsertAfter(paragraph, "ul");
        Check(inserted is not null && inserted.Name == "ul" && inserted.FirstElement("li") is not null,
            "вставка списка с обязательным li");

        Check(EditCommands.InsertAfter(paragraph, "title") is null,
            "недопустимый элемент не вставляется");

        var textNode = paragraph.Children.First(c => c.Kind == NodeKind.Text);
        var wrapped = EditCommands.WrapTextRange(textNode, 0, 6, "b");
        Check(wrapped is not null && wrapped.InnerText == "Первый" && paragraph.InnerText == "Первый абзац",
            "оформление части текста фразовым элементом");

        var undo = new UndoStack();
        undo.Push(document, "проверка");
        conbody.RemoveSelf();
        Check(document.Root.FirstElement("conbody") is null, "элемент удалён");
        undo.Undo(document);
        Check(document.Root.FirstElement("conbody") is not null, "отмена вернула элемент");

        var title = document.Root.FirstElement("title")!;
        var enabled = EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(enabled && title.GetAttribute("outputclass") == "page-break-before",
            "класс вывода добавлен на заголовок");
        var disabled = EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(!disabled && title.GetAttribute("outputclass") is null,
            "повторное переключение снимает класс вывода");

        EditCommands.ToggleOutputClassToken(title, "existing");
        EditCommands.ToggleOutputClassToken(title, "page-break-before");
        Check(title.GetAttribute("outputclass") == "existing page-break-before",
            "класс вывода добавляется рядом с уже существующим, не заменяя его");

        TableMergeTests();
    }

    private static void TableMergeTests()
    {
        var tableDoc = DitaDocument.Parse("""
<reference id="r1"><title>Р</title><refbody><table><tgroup cols="3">
<colspec colname="c1" colnum="1"/><colspec colname="c2" colnum="2"/><colspec colname="c3" colnum="3"/>
<tbody>
<row><entry>A1</entry><entry>B1</entry><entry>C1</entry></row>
<row><entry>A2</entry><entry>B2</entry><entry>C2</entry></row>
</tbody>
</tgroup></table></refbody></reference>
""");
        var rows = tableDoc.Root.FindDescendant("tbody")!.ElementChildren().Where(r => r.Name == "row").ToList();
        var row1Entries = rows[0].ElementChildren().Where(e => e.Name == "entry").ToList();

        var mergedRight = EditCommands.MergeTableCellRight(row1Entries[0]);
        Check(mergedRight is not null && mergedRight.GetAttribute("namest") == "c1" && mergedRight.GetAttribute("nameend") == "c2",
            "объединение ячеек по горизонтали проставляет namest/nameend");
        Check(rows[0].ElementChildren().Count(e => e.Name == "entry") == 2, "соседняя ячейка справа удалена после объединения");
        Check(mergedRight!.InnerText == "A1 B1", "содержимое объединённых ячеек сохранено");

        var row2Entries = rows[1].ElementChildren().Where(e => e.Name == "entry").ToList();
        var thirdColumnEntry = row2Entries.Single(e => e.InnerText == "C2");
        var mergedDown = EditCommands.MergeTableCellDown(row1Entries[2]);
        Check(mergedDown is not null && mergedDown.GetAttribute("morerows") == "1",
            "объединение ячеек по вертикали проставляет morerows");
        Check(rows[1].ElementChildren().Count(e => e.Name == "entry") == 2,
            "поглощённая нижняя ячейка удалена из строки");
        Check(mergedDown!.InnerText == "C1 C2", "содержимое объединённой по вертикали ячейки сохранено");
        _ = thirdColumnEntry;

        // Строку нельзя опустошить целиком объединением по вертикали.
        var lastRowSingleEntry = DitaDocument.Parse("""
<reference id="r2"><title>Р</title><refbody><table><tgroup cols="1">
<colspec colname="c1" colnum="1"/>
<tbody><row><entry>X1</entry></row><row><entry>X2</entry></row></tbody>
</tgroup></table></refbody></reference>
""");
        var soleRows = lastRowSingleEntry.Root.FindDescendant("tbody")!.ElementChildren().Where(r => r.Name == "row").ToList();
        var soleEntry = soleRows[0].FirstElement("entry")!;
        Check(EditCommands.MergeTableCellDown(soleEntry) is null,
            "объединение по вертикали не опустошает строку целиком");
    }

    internal static void EditCommandsExtraTests()
    {
        Section("EditCommands: остальные операции (по отчёту покрытия)");

        var doc = DitaDocument.Parse("""
<concept id="c1"><title>T</title><conbody>
  <p id="p1">Первый</p>
  <p id="p2">Второй</p>
  <p id="p3">Третий</p>
</conbody></concept>
""");
        var conbody = doc.Root.FirstElement("conbody")!;
        var ps = () => conbody.ElementChildren().Where(e => e.Name == "p").ToList();
        var p1 = ps()[0];
        var p2 = ps()[1];
        var p3 = ps()[2];

        var beforeP2 = EditCommands.InsertBefore(p2, "p");
        Check(beforeP2 is not null && ps().IndexOf(beforeP2) == 1, "InsertBefore вставляет непосредственно перед узлом");
        beforeP2!.RemoveSelf();

        var appended = EditCommands.Append(conbody, "p");
        Check(appended is not null && ReferenceEquals(ps()[^1], appended), "Append вставляет в конец родителя");
        appended!.RemoveSelf();

        // Ячейка только с текстом: блок добавляется после текста, а не перед ним (нет элементов-детей — индекс «после последнего» был началом).
        var textCell = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><table><tgroup cols=\"1\"><tbody><row><entry>Текст ячейки</entry></row></tbody></tgroup></table></conbody></concept>")
            .Root.DescendantsAndSelf().First(n => n.Name == "entry");
        var listInCell = EditCommands.Append(textCell, "ul");
        Check(listInCell is not null && textCell.Children.Count == 2 && textCell.Children[0].Kind == NodeKind.Text && ReferenceEquals(textCell.Children[1], listInCell),
            "Append в ячейку с одним текстом ставит блок после текста");

        var wrapped = EditCommands.Wrap(conbody, 0, 1, "note");
        Check(wrapped is not null && wrapped.Name == "note", "Wrap создаёт обёртку заданного имени");
        Check(wrapped!.ElementChildren().Select(e => e.Name == "p" ? e.InnerText : e.Name).SequenceEqual(new[] { "Первый", "Второй" }),
            "Wrap переносит внутрь обёртки именно указанный диапазон детей, в исходном порядке");
        Check(conbody.ElementChildren().Count() == 2 && ReferenceEquals(conbody.ElementChildren().First(), wrapped),
            "обёрнутые дети убраны из родителя, на их месте — обёртка");

        Check(EditCommands.Unwrap(wrapped) && conbody.ElementChildren().Count() == 3,
            "Unwrap возвращает детей обёртки на её место и убирает саму обёртку");
        Check(ps().Select(p => p.InnerText).SequenceEqual(new[] { "Первый", "Второй", "Третий" }),
            "после Wrap→Unwrap порядок и содержимое узлов не изменились");

        Check(EditCommands.Wrap(conbody, 1, 0, "note") is null, "Wrap отклоняет диапазон с first > last");
        Check(EditCommands.Wrap(conbody, 0, 99, "note") is null, "Wrap отклоняет диапазон за пределами числа детей");

        Check(!EditCommands.Unwrap(doc.Root), "Unwrap корневого узла (нет родителя) возвращает false");

        Check(EditCommands.MoveDown(p1) && ps()[0] == p2 && ps()[1] == p1, "MoveDown меняет местами с следующим элементом");
        Check(EditCommands.MoveUp(p1) && ps()[0] == p1 && ps()[1] == p2, "MoveUp возвращает элемент обратно наверх");
        Check(!EditCommands.MoveUp(p1), "MoveUp первого элемента возвращает false");
        Check(!EditCommands.MoveDown(p3), "MoveDown последнего элемента возвращает false");

        p1.SetAttribute("totally-fake-attr", "x");
        Check(EditCommands.ChangeElementName(p1, "note"), "ChangeElementName переименовывает известный элемент");
        Check(p1.Name == "note", "имя узла обновлено");
        Check(p1.GetAttribute("totally-fake-attr") is null,
            "атрибут, недопустимый для нового имени элемента, снят при переименовании");
        Check(!EditCommands.ChangeElementName(p1, "no-such-element-xyz"), "ChangeElementName отклоняет неизвестное целевое имя");
        Check(!EditCommands.ChangeElementName(doc.Root, "task"), "ChangeElementName корневого узла (нет родителя) возвращает false");

        Check(EditCommands.MergeWithPrevious(p2) is null, "MergeWithPrevious не объединяет узлы разного имени (сосед p1 теперь note)");
        Check(!EditCommands.Delete(doc.Root), "Delete корневого узла (нет родителя) возвращает false");
        Check(EditCommands.Delete(p3) && conbody.ElementChildren().Count() == 2, "Delete убирает узел из дерева");

        var id1 = EditCommands.GenerateId(doc, "fig");
        Check(id1 == "fig_1", $"GenerateId выдаёт первый свободный номер: {id1}");
        doc.Root.FirstElement("title")!.SetAttribute("id", "fig_1");
        var id2 = EditCommands.GenerateId(doc, "fig");
        Check(id2 == "fig_2", $"GenerateId пропускает уже занятый id: {id2}");

        // --- механические защитные проверки MergeTableCellRight/Down на входах не по форме
        var notEntry = DitaDocument.Parse("<reference id=\"r\"><title>Р</title><refbody><p>Абзац</p></refbody></reference>")
            .Root.FindDescendant("p")!;
        Check(EditCommands.MergeTableCellRight(notEntry) is null, "MergeTableCellRight отклоняет узел, который не является entry в row");
        Check(EditCommands.MergeTableCellDown(notEntry) is null, "MergeTableCellDown отклоняет узел, который не является entry в row");

        var singleEntryRow = DitaDocument.Parse("""
<reference id="r3"><title>Р</title><refbody><table><tgroup cols="2">
<colspec colname="c1" colnum="1"/><colspec colname="c2" colnum="2"/>
<tbody><row><entry>only</entry></row></tbody>
</tgroup></table></refbody></reference>
""");
        var lastInRow = singleEntryRow.Root.FindDescendant("row")!.FirstElement("entry")!;
        Check(EditCommands.MergeTableCellRight(lastInRow) is null, "MergeTableCellRight отклоняет последнюю ячейку строки (нет соседа справа)");
        Check(EditCommands.MergeTableCellDown(lastInRow) is null, "MergeTableCellDown отклоняет ячейку, если строки снизу нет");

        var detachedRow = DitaDocument.Parse("<concept id=\"c\"><title>T</title><conbody><row><entry>X</entry><entry>Y</entry></row></conbody></concept>");
        var detachedEntry = detachedRow.Root.FindDescendant("row")!.FirstElement("entry")!;
        Check(EditCommands.MergeTableCellRight(detachedEntry) is null, "MergeTableCellRight отклоняет entry вне tgroup");
    }

    internal static void UndoStackExtraTests()
    {
        Section("UndoStack: Redo, лимит, событие Changed (по отчёту покрытия)");

        var document = DitaDocument.Parse("<concept id=\"c1\"><title>T</title><conbody><p>A</p></conbody></concept>");
        var undo = new UndoStack(limit: 3);
        var changedCount = 0;
        undo.Changed += (_, _) => changedCount++;

        Check(!undo.CanUndo && !undo.CanRedo, "новый UndoStack пуст");
        Check(!undo.Undo(document), "Undo на пустом стеке возвращает false");
        Check(!undo.Redo(document), "Redo на пустом стеке возвращает false");
        Check(changedCount == 0, "Undo/Redo впустую не генерируют событие Changed");

        undo.Push(document, "шаг 1");
        document.Root.FirstElement("conbody")!.FirstElement("p")!.SetText("B");
        Check(undo.CanUndo && undo.NextUndoDescription == "шаг 1", "Push запоминает описание снимка");
        Check(changedCount == 1, "Push генерирует событие Changed");

        undo.Push(document, "шаг 2");
        document.Root.FirstElement("conbody")!.FirstElement("p")!.SetText("C");
        Check(undo.NextUndoDescription == "шаг 2", "второй Push — следующий кандидат на отмену");

        Check(undo.Undo(document) && document.Root.FirstElement("conbody")!.FirstElement("p")!.InnerText == "B",
            "Undo восстанавливает состояние до шага 2");
        Check(undo.CanRedo && undo.NextRedoDescription == "шаг 2", "после Undo появляется кандидат на Redo с тем же описанием");

        Check(undo.Redo(document) && document.Root.FirstElement("conbody")!.FirstElement("p")!.InnerText == "C",
            "Redo возвращает состояние вперёд");
        Check(!undo.CanRedo, "после Redo стек повтора снова пуст");

        undo.Undo(document);
        undo.Push(document, "шаг 2b — новая ветка после отмены");
        Check(!undo.CanRedo, "новый Push после Undo очищает стек Redo (старая ветка истории отброшена)");

        undo.Clear();
        Check(!undo.CanUndo && !undo.CanRedo, "Clear опустошает оба стека");

        // Лимит истории: limit=3, пятая запись должна вытеснить самую старую.
        var limited = new UndoStack(limit: 3);
        for (var i = 1; i <= 5; i++)
        {
            limited.Push(document, $"запись {i}");
        }

        var descriptions = new List<string>();
        while (limited.CanUndo)
        {
            descriptions.Add(limited.NextUndoDescription!);
            limited.Undo(document);
        }

        Check(descriptions.Count == 3, $"история обрезана по лимиту: {descriptions.Count} записей вместо 5");
        Check(descriptions[0] == "запись 5" && descriptions[^1] == "запись 3",
            $"вытеснены самые старые записи, остались последние {string.Join(",", descriptions)}");
    }
}
