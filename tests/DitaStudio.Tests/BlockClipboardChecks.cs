using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Tests;

// Копирование, вырезание и вставка блоков: текст буфера обмена, разбор, уникальные id, допустимое место вставки.
internal static partial class CoreChecks
{
    internal static void BlockClipboardTests()
    {
        Section("Буфер обмена блоков");

        var document = DitaDocument.Parse("""
            <concept id="c"><title>Т</title><conbody>
            <p id="a">Первый &amp; важный.</p><note id="n"><p id="np">Внутри <b>жирное</b>.</p></note><!-- комментарий --><p>Третий.</p>
            <ul><li>Один</li><li>Два</li></ul>
            </conbody></concept>
            """);
        var body = document.Root.FirstElement("conbody")!;
        var blocks = body.Children.Where(n => n.Kind != NodeKind.Text || !string.IsNullOrWhiteSpace(n.Value)).Take(3).ToList(); // p, note, комментарий

        // Текст и обратный разбор: те же блоки, копии, без потерь.
        var text = BlockClipboard.Serialize(blocks);
        Check(text.Contains("<p id=\"a\">Первый &amp; важный.</p>") && text.Contains("<note id=\"n\">") && text.Contains("<!--"), "текст буфера: XML блоков, комментарий тоже");
        var parsed = BlockClipboard.Parse(text);
        Check(parsed is not null && parsed.Count == 3 && parsed[0].Name == "p" && parsed[0].InnerText == "Первый & важный." && parsed[1].Name == "note" && parsed[2].Kind == NodeKind.Comment,
            "разбор: три блока (абзац, заметка, комментарий), текст с & сохранён");
        Check(parsed is not null && !blocks.Any(b => parsed.Contains(b)) && parsed[1].FirstElement("p")!.FirstElement("b")!.InnerText == "жирное", "разобранное — копии (не те же узлы), вложенные фразы на месте");

        // Не блоки.
        Check(BlockClipboard.Parse("просто текст") is null && BlockClipboard.Parse("   ") is null && BlockClipboard.Parse(null) is null, "обычный текст и пустое — не блоки");
        Check(BlockClipboard.Parse("<p>один</p> и текст") is null && BlockClipboard.Parse("<p>незакрытый") is null, "текст вперемешку с блоками и неверный XML — не блоки");
        Check(BlockClipboard.Parse("<b>жирный</b>") is null && BlockClipboard.Parse("<неизвестный>x</неизвестный>") is null, "фраза и неизвестный элемент — не блоки");
        Check(BlockClipboard.Parse("<p>один</p>\n<p>два</p>")?.Count == 2, "два абзаца подряд (с пробелами между ними) — два блока");

        // Уникальные id.
        var copy = BlockClipboard.Parse(text)!;
        var renamed = BlockClipboard.MakeIdsUnique(document, copy);
        var ids = copy.SelectMany(c => c.DescendantsAndSelf()).Where(n => n.Kind == NodeKind.Element).Select(n => n.GetAttribute("id")).Where(i => i is not null).ToList();
        Check(renamed == 3 && ids.SequenceEqual(new[] { "a-копия", "n-копия", "np-копия" }), $"id, которые уже есть в документе, получают «-копия» ({string.Join(", ", ids)})");
        body.Add(copy[0]);
        var second = BlockClipboard.Parse(text)!;
        BlockClipboard.MakeIdsUnique(document, second);
        Check(second[0].GetAttribute("id") == "a-копия-2", "второй раз — «-копия-2»");
        copy[0].RemoveSelf();

        // Место вставки: после абзаца в теле — допустимо; после пункта списка — абзац недопустим в ul, поднимаемся и ставим после ul.
        var p = body.ElementChildren().First(n => n.Name == "p");
        var plain = BlockClipboard.Parse("<p>Новый</p>")!;
        var here = BlockClipboard.FindInsertion(p, plain);
        Check(here is not null && ReferenceEquals(here.Parent, body) && here.Index == body.IndexOf(p) + 1, "абзац после абзаца — в тот же родитель сразу за ним");
        var item = body.FirstElement("ul")!.ElementChildren().First();
        var up = BlockClipboard.FindInsertion(item, plain);
        Check(up is not null && ReferenceEquals(up.Parent, body) && up.Index == body.IndexOf(body.FirstElement("ul")!) + 1, "абзац после пункта списка — после самого списка (в ul абзацу не место)");
        var items = BlockClipboard.Parse("<li>Три</li>")!;
        var inList = BlockClipboard.FindInsertion(item, items);
        Check(inList is not null && inList.Parent.Name == "ul" && inList.Index == 1, "пункт после пункта — в тот же список");
        var impossible = BlockClipboard.Parse("<li>Не сюда</li>")!;
        Check(BlockClipboard.FindInsertion(p, impossible) is null, "пункт списка после абзаца в теле — нигде не допустим (null)");
        Check(!BlockClipboard.Accepts(body, 0, items) && BlockClipboard.Accepts(body, 0, plain), "проверка допустимости по контент-модели родителя");
    }
}
