using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Tests;

// Д5: диапазон соседних блоков по двум узлам (выделение протяжкой мыши) и список элементов для «Обернуть в…».
internal static partial class CoreChecks
{
    internal static void BlockRangeTests()
    {
        Section("Выделение блоков и «Обернуть в…»");

        var document = DitaDocument.Parse("""
            <concept id="c"><title>Т</title><conbody>
            <p>А</p><p>Б</p><note><p>В</p></note><p>Г</p>
            <ul><li>Один</li><li>Два</li></ul>
            <table><tgroup cols="2"><tbody><row><entry><p>х1</p><p>х2</p></entry><entry><p>у</p></entry></row></tbody></tgroup></table>
            </conbody></concept>
            """);
        var body = document.Root.FirstElement("conbody")!;
        var ps = body.ElementChildren().Where(n => n.Name == "p").ToList();
        var note = body.FirstElement("note")!;
        var inNote = note.FirstElement("p")!;
        var ul = body.FirstElement("ul")!;
        var table = body.FirstElement("table")!;
        var entries = table.DescendantsAndSelf().Where(n => n.Name == "entry").ToList();

        List<string> Names(BlockSpan? span) => span is null ? new List<string> { "—" } : span.Blocks.Select(n => n.Kind == NodeKind.Element ? n.Name + ":" + n.InnerText : "#").ToList();

        // Диапазон между двумя абзацами и в обратном порядке; «Г» — после заметки.
        var forward = BlockRanges.Between(ps[0], ps[2]);
        Check(forward is not null && ReferenceEquals(forward.Parent, body) && string.Join(",", Names(forward)) == "p:А,p:Б,note:В,p:Г", "диапазон от первого абзаца до последнего захватывает блоки между ними, заметку — целиком");
        var backward = BlockRanges.Between(ps[2], ps[0]);
        Check(backward is not null && backward.First == forward!.First && backward.Last == forward.Last, "направление протяжки (вверх или вниз) диапазон не меняет");
        Check(BlockRanges.Between(ps[0], ps[0]) is null, "один и тот же блок — диапазона нет");

        // Из абзаца внутри заметки в абзац снаружи: заметка берётся целиком.
        var mixed = BlockRanges.Between(ps[1], inNote);
        Check(mixed is not null && string.Join(",", Names(mixed)) == "p:Б,note:В", "от абзаца к абзацу внутри заметки — абзац и вся заметка");

        // Блок и его потомок — внешний блок.
        var outer = BlockRanges.Between(note, inNote);
        Check(outer is not null && string.Join(",", Names(outer)) == "note:В", "блок и его вложенный блок — один внешний блок");

        // Пункты списка и заголовок с телом.
        var items = ul.ElementChildren().ToList();
        Check(string.Join(",", Names(BlockRanges.Between(items[0], items[1]))) == "li:Один,li:Два", "два пункта списка");
        var wide = BlockRanges.Between(document.Root.FirstElement("title")!, ps[0]);
        Check(wide is not null && ReferenceEquals(wide.Parent, document.Root) && string.Join(",", Names(wide)).StartsWith("title:Т,conbody:"), "от заголовка в тело — соседи корня");

        // Таблица: между ячейками — выделение ячеек (не блоков), внутри одной ячейки — блоки.
        Check(BlockRanges.Between(entries[0].FirstElement("p")!, entries[1].FirstElement("p")!) is null, "абзацы в разных ячейках — не диапазон блоков (это выделение ячеек)");
        var inCell = BlockRanges.Between(entries[0].ElementChildren().First(), entries[0].ElementChildren().Last());
        Check(inCell is not null && ReferenceEquals(inCell.Parent, entries[0]) && string.Join(",", Names(inCell)) == "p:х1,p:х2", "абзацы в одной ячейке — диапазон блоков");

        // Допустимые обёртки: для двух абзацев — контейнеры, принимающие абзацы, и только они.
        var first = forward!;
        var candidates = BlockRanges.WrapCandidates(first.Parent, 0, 1).Select(d => d.Name).ToList();
        Check(candidates.Contains("note") && candidates.Contains("div") && candidates.Contains("section") && candidates.Contains("fig") && candidates.Contains("example"), $"два абзаца можно обернуть в note, div, section, fig, example ({string.Join(" ", candidates.Take(12))}…)");
        Check(!candidates.Contains("ul") && !candidates.Contains("li") && !candidates.Contains("table") && !candidates.Contains("entry") && !candidates.Contains("b"),
            $"в ul, li, table, entry и фразы абзацы не оборачиваются ({string.Join(" ", candidates)})");
        Check(candidates.SequenceEqual(candidates.OrderBy(n => n, StringComparer.Ordinal)), "список упорядочен по имени");

        // Пункты списка в note не вкладываются (у ul другие дети); а несколько абзацев внутри пункта — можно.
        var itemWrap = BlockRanges.WrapCandidates(ul, 0, 1).Select(d => d.Name).ToList();
        Check(!itemWrap.Contains("note") && !itemWrap.Contains("div"), "два пункта списка в note или div не оборачиваются: список принимает только li");
        Check(BlockRanges.WrapCandidates(body, 5, 5).Any(d => d.Name == "div"), "таблицу можно обернуть в div");
        Check(BlockRanges.WrapCandidates(body, -1, 2).Count == 0 && BlockRanges.WrapCandidates(body, 3, 99).Count == 0, "диапазон вне родителя — пустой список");

        // Обёртка по выбранному кандидату действительно даёт допустимый документ.
        var wrapped = EditCommands.Wrap(first.Parent, 0, 1, "note");
        Check(wrapped is not null && wrapped.Name == "note" && wrapped.ElementChildren().Select(n => n.InnerText).SequenceEqual(new[] { "А", "Б" }) &&
              new DitaStudio.Core.Validation.DitaValidator { CheckStyleRules = false }.Validate(document).All(i => i.Severity != DitaStudio.Core.Validation.IssueSeverity.Error),
            "после «Обернуть в note» блоки внутри, документ допустим по контент-модели");
    }
}
