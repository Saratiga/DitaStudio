using DitaStudio.Core.Model;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Г6: «живой» постраничный предпросмотр — геометрия листа, страница с листами, вызов подмены текста.
// Саму раскладку (JS) проверяет CefPreviewTests в настоящем Chromium.
internal static partial class CoreChecks
{
    internal static void PagedPreviewTests()
    {
        Section("Постраничный предпросмотр");

        var layout = new DocxLayout();
        var a4 = PageGeometry.For(layout);
        Check(a4 == PageGeometry.A4 && a4.ContentWidthMm == 170 && a4.ContentHeightMm == 257, "без параметров страницы — A4 с полями 20 мм");

        layout.PaperSize = "A5";
        layout.Landscape = true;
        layout.MarginTopMm = 10;
        layout.MarginLeftMm = 15;
        var a5 = PageGeometry.For(layout);
        Check(a5.WidthMm == 210 && a5.HeightMm == 148 && a5.TopMm == 10 && a5.LeftMm == 15 && a5.RightMm == 20, "A5 альбомная: бумага повёрнута, заданные поля взяты из параметров, остальные — 20 мм");

        layout.PaperWidthMm = 100;
        layout.PaperHeightMm = 150;
        layout.Landscape = false;
        var custom = PageGeometry.For(layout);
        Check(custom.WidthMm == 100 && custom.HeightMm == 150, "свой размер бумаги главнее выбранного формата");

        var html = PagedPreview.Build(a5, ".p{color:red}", "Заголовок <1>", "Шапка {title}", "{page} из {pages}", "<h1>Тема</h1><p>Текст</p>");
        Check(html.Contains("width:210mm") && html.Contains("height:148mm"), "размер листа в стилях страницы");
        Check(html.Contains("left:15mm") && html.Contains("top:10mm") && html.Contains("width:175mm") && html.Contains("height:118mm"), "поля листа: область текста = бумага минус поля");
        Check(html.Contains(".p{color:red}") && html.Contains("ditaSetContent") && html.Contains("<title>Заголовок &lt;1&gt;</title>"), "стили публикации, скрипт раскладки и название на месте");
        Check(html.Contains("\"{page} из {pages}\"") && html.Contains("Шапка {title}"), "колонтитулы передаются скрипту как шаблоны с полями");

        var tricky = PagedPreview.SetContentCall("<p class=\"a\">x\\y</p>\n</script><!--" + (char)0x2028);
        Check(tricky.StartsWith("if(window.ditaSetContent){ditaSetContent(\"") && !tricky.Contains('\n') && !tricky.Contains("</script>") && !tricky.Contains((char)0x2028) &&
              tricky.Contains("\\\"a\\\"") && tricky.Contains("x\\\\y") && tricky.Contains("\\n") && tricky.Contains("\\u2028"),
            "вызов подмены текста: кавычки, обратный слэш, перевод строки, </script> и U+2028 экранируются");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = "<topic id=\"t\"><title>Тема</title><body><p>АБЗАЦ_ТЕКСТ</p></body></topic>"
        }, (root, project) =>
        {
            var document = DitaDocument.Load(Path.Combine(root, "t.dita"));
            var (body, css) = new HtmlPublisher(project).RenderPagedParts(document);
            Check(body.Contains("АБЗАЦ_ТЕКСТ") && body.Contains("<h1") && !body.Contains("<html"), "тело топика — без оболочки страницы");
            Check(css.Contains(".toc") || css.Contains("body"), "стили публикации переданы");

            project.DocxLayout.PaperSize = "A3";
            var page = PageGeometry.For(project.DocxLayout);
            Check(page.WidthMm == 297 && page.HeightMm == 420, "параметры страницы проекта попадают в предпросмотр");
        });
    }
}
