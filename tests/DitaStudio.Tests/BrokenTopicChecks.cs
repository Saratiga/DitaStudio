using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Г3: «битая» строка карты — понятная причина и способ исправления; предпросмотр показывает плашку на месте картинки.
internal static partial class CoreChecks
{
    internal static void BrokenTopicTests()
    {
        Section("Битая ссылка в карте: причина, исправление, предпросмотр");

        WithProject(new Dictionary<string, string>
        {
            ["ok.dita"] = "<topic id=\"ok\"><title>Есть</title><body><p>Текст.</p><image href=\"images/none.png\" placement=\"break\"/><p>После <image href=\"none2.png\"/> картинки.</p></body></topic>",
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"ok.dita\"/><topicref href=\"tasks/missing.dita\" navtitle=\"Нет файла\"/>" +
                            "<topicref keyref=\"undefined-key\"/></map>"
        }, (root, project) =>
        {
            var tree = MapTree.Build(project, Path.Combine(root, "m.ditamap"));
            var items = tree.Items.ToList();
            var good = items.First(i => i.TargetPath?.EndsWith("ok.dita") == true);
            Check(!good.IsBroken && good.BrokenReason.Length == 0, "нормальная строка — не битая, причины нет");

            var missing = items.First(i => i.Node.GetAttribute("href") == "tasks/missing.dita");
            Check(missing.IsBroken && missing.BrokenReason.Contains("Файл не найден") && missing.BrokenReason.Contains("missing.dita") &&
                  missing.BrokenReason.Contains("Создайте файл") && missing.BrokenReason.Contains("уберите строку"),
                $"нет файла: причина с путём и советом ({missing.BrokenReason})");

            var noKey = items.First(i => i.Node.GetAttribute("keyref") == "undefined-key");
            Check(noKey.IsBroken && noKey.BrokenReason.Contains("undefined-key") && noKey.BrokenReason.Contains("не определён"),
                $"ключ не определён: причина названа ({noKey.BrokenReason})");

            // Проверка проекта: сообщение с путём и советом.
            var issues = RefResolver.ValidateReferences(project, project.GetDocument(Path.Combine(root, "m.ditamap")));
            var text = string.Join(" | ", issues.Select(i => i.Message));
            Check(text.Contains("missing.dita") && text.Contains("не найден") && text.Contains("Создайте файл"), $"проверка: сообщение об ошибке подсказывает исправление ({text})");

            // Предпросмотр: на месте не найденной картинки — плашка; публикация её не выводит.
            var preview = new HtmlPublisher(project).RenderPreview(project.GetDocument(Path.Combine(root, "ok.dita")));
            Check(preview.Contains("class=\"image-missing\"") && preview.Contains("Картинка не найдена: images/none.png") && preview.Contains("Картинка не найдена: none2.png"),
                "предпросмотр: «Картинка не найдена: путь» на месте блочной и строчной картинок");
            Check(preview.Contains("Текст.") && preview.Contains("картинки."), "предпросмотр: текст топика выводится целиком");
            var html = File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"),
                new PublishOptions { OutputDirectory = Path.Combine(root, "out"), SingleFile = true }).EntryFile);
            Check(!html.Contains("class=\"image-missing\">"), "публикация: плашки нет (это только предпросмотр)");
            var mapPreview = File.ReadAllText(new HtmlPublisher(project).RenderMapPreview(Path.Combine(root, "m.ditamap"), Path.Combine(root, "mapview")));
            Check(mapPreview.Contains("Текст.") && mapPreview.Contains("Картинка не найдена: images/none.png") && mapPreview.Contains("Моя карта") == false && mapPreview.Contains("<h1 class=\"book-title\">M</h1>"),
                "предпросмотр карты: текст топиков целиком, плашка вместо картинки, заголовок издания");
        });
    }
}
