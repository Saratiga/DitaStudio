using DitaStudio.Core.Editing;
using DitaStudio.Core.Model;

namespace DitaStudio.Tests;

// Перенос строк карты перетаскиванием: на другой уровень и в другого родителя, по контент-модели.
internal static partial class CoreChecks
{
    internal static void MapMoveTests()
    {
        Section("Перенос строк карты: перед, после, внутрь");

        static DitaNode Find(DitaNode root, string href) =>
            root.DescendantsAndSelf().First(n => n.Kind == NodeKind.Element && n.GetAttribute("href") == href);
        static string Layout(DitaNode node) =>
            string.Join(",", node.ElementChildren().Where(c => c.Name is not ("topicmeta" or "title")).Select(c =>
                (c.GetAttribute("href") ?? c.Name) + (c.ElementChildren().Any(x => x.Name != "topicmeta") ? "(" + Layout(c) + ")" : string.Empty)));

        DitaDocument Map() => DitaDocument.Parse(
            "<map><title>M</title>" +
            "<topicref href=\"a.dita\"><topicref href=\"a1.dita\"/><topicref href=\"a2.dita\"/></topicref>" +
            "<topicref href=\"b.dita\"/>" +
            "<topichead><topicmeta><navtitle>Раздел</navtitle></topicmeta><topicref href=\"c.dita\"/></topichead>" +
            "<keydef keys=\"k\"/></map>");

        // Внутрь: строка становится последней дочерней цели
        var doc = Map();
        var root = doc.Root;
        Check(MapMoves.Move(Find(root, "b.dita"), Find(root, "a.dita"), DropPosition.Child), "внутрь: b.dita → в a.dita");
        Check(Layout(root).StartsWith("a.dita(a1.dita,a2.dita,b.dita)"), "внутрь: b.dita — последняя дочерняя a.dita: " + Layout(root));

        // Перед и после, на другом уровне
        doc = Map();
        root = doc.Root;
        Check(MapMoves.Move(Find(root, "c.dita"), Find(root, "a1.dita"), DropPosition.Before), "перед: c.dita перед a1.dita (в другом родителе)");
        Check(Layout(root).StartsWith("a.dita(c.dita,a1.dita,a2.dita),b.dita,topichead"), "перед: " + Layout(root));
        Check(MapMoves.Move(Find(root, "a2.dita"), Find(root, "b.dita"), DropPosition.After), "после: a2.dita после b.dita — вынос на верхний уровень");
        Check(Layout(root).Contains("a.dita(c.dita,a1.dita),b.dita,a2.dita"), "после: " + Layout(root));

        // Внутрь раздела без файла
        doc = Map();
        root = doc.Root;
        var head = root.ElementChildren().First(n => n.Name == "topichead");
        Check(MapMoves.Move(Find(root, "b.dita"), head, DropPosition.Child), "внутрь: топик в раздел (topichead)");
        Check(head.ElementChildren().Last().GetAttribute("href") == "b.dita", "внутрь: в разделе последним стоит b.dita");

        // Запреты
        doc = Map();
        root = doc.Root;
        var a = Find(root, "a.dita");
        Check(!MapMoves.CanMove(a, Find(root, "a1.dita"), DropPosition.Child, out var reason) && reason.Contains("собственную ветку"),
            $"запрет: в собственного потомка — «{reason}»");
        Check(!MapMoves.CanMove(a, Find(root, "a2.dita"), DropPosition.After, out reason) && reason.Contains("собственную ветку"),
            "запрет: рядом со своим потомком (в собственной ветке)");
        Check(!MapMoves.CanMove(a, a, DropPosition.Child, out reason) && reason.Contains("саму себя"), $"запрет: на саму себя — «{reason}»");
        Check(!MapMoves.CanMove(root, a, DropPosition.Child, out reason) && reason.Contains("Корень"), "запрет: переносить корень карты");
        Check(!MapMoves.CanMove(a, root, DropPosition.Before, out reason) && reason.Contains("корнем"), $"запрет: перед корнем — «{reason}»");
        var foreign = DitaDocument.Parse("<map><title>Другая</title><topicref href=\"z.dita\"/></map>");
        Check(!MapMoves.CanMove(a, Find(foreign.Root, "z.dita"), DropPosition.After, out reason) && reason.Contains("разных файлов"),
            $"запрет: между разными файлами карты — «{reason}»");
        var before = Layout(root);
        Check(!MapMoves.Move(a, Find(root, "a1.dita"), DropPosition.Child) && Layout(root) == before, "запрещённый перенос карту не меняет");

        // Контент-модель bookmap: chapter нельзя вложить в chapter и поставить до frontmatter
        var book = DitaDocument.Parse(
            "<bookmap><booktitle><mainbooktitle>Книга</mainbooktitle></booktitle>" +
            "<frontmatter/><chapter href=\"c1.dita\"/><chapter href=\"c2.dita\"/><part><chapter href=\"c3.dita\"/></part><backmatter/></bookmap>");
        var bookRoot = book.Root;
        var c1 = Find(bookRoot, "c1.dita");
        Check(!MapMoves.CanMove(c1, Find(bookRoot, "c2.dita"), DropPosition.Child, out reason), "bookmap: chapter в chapter — недопустимо");
        Check(!MapMoves.CanMove(c1, bookRoot.FirstElement("frontmatter")!, DropPosition.Before, out reason),
            "bookmap: chapter перед frontmatter — недопустимо (frontmatter идёт раньше глав)");
        Check(MapMoves.CanMove(c1, bookRoot.FirstElement("part")!, DropPosition.Child, out _), "bookmap: chapter в part — допустимо");
        Check(MapMoves.CanMove(Find(bookRoot, "c3.dita"), Find(bookRoot, "c2.dita"), DropPosition.After, out _), "bookmap: chapter из part на верхний уровень — допустимо");
        Check(MapMoves.CanMove(Find(book.Root, "c2.dita"), Find(bookRoot, "c1.dita"), DropPosition.Before, out _), "bookmap: перестановка глав — допустима");
    }
}
