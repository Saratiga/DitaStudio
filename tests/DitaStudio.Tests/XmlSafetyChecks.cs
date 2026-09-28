using System.Diagnostics;
using System.Text;
using System.Xml;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;

namespace DitaStudio.Tests;

// Разбор недоверенного XML: сущности DOCTYPE раскрываются, но с пределом — файл
// с вложенными сущностями («billion laughs») не должен вешать редактор.
internal static partial class CoreChecks
{
    internal static void XmlSafetyTests()
    {
        Section("Разбор XML: предел раскрытия сущностей");

        var small = DitaDocument.Parse(
            "<!DOCTYPE topic [<!ENTITY product \"DITA Studio\">]>\n" +
            "<topic id=\"t\"><title>О программе &product;</title></topic>");
        Check(small.Title == "О программе DITA Studio", $"обычная внутренняя сущность раскрывается: {small.Title}");

        var bomb = EntityBomb("topic", "<topic id=\"t\"><title>&e9;</title></topic>");
        var watch = Stopwatch.StartNew();
        var rejected = false;
        try
        {
            DitaDocument.Parse(bomb);
        }
        catch (XmlException)
        {
            rejected = true;
        }

        watch.Stop();
        Check(rejected, "DitaDocument.Parse: «billion laughs» (10⁹ символов) отклонён XmlException");
        Check(watch.Elapsed < TimeSpan.FromSeconds(10), $"отказ быстрый, без раскрытия всего текста ({watch.ElapsedMilliseconds} мс)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ditaval = Path.Combine(root, "bomb.ditaval");
            File.WriteAllText(ditaval,
                EntityBomb("val", "<val>&e9;<prop att=\"audience\" val=\"x\" action=\"exclude\"/></val>"),
                new UTF8Encoding(false));
            var ditavalRejected = false;
            try
            {
                DitavalReader.Read(ditaval);
            }
            catch (XmlException)
            {
                ditavalRejected = true;
            }

            Check(ditavalRejected, "DitavalReader: тот же предел раскрытия сущностей");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // временные файлы удалятся системой
            }
        }
    }

    /// <summary>Документ, где сущность e9 раскрывается в 10⁹ символов: каждая следующая —
    /// десять копий предыдущей.</summary>
    private static string EntityBomb(string rootName, string body)
    {
        var sb = new StringBuilder("<!DOCTYPE ").Append(rootName).Append(" [\n");
        sb.Append("<!ENTITY e0 \"aaaaaaaaaa\">\n");
        for (var i = 1; i <= 9; i++)
        {
            sb.Append("<!ENTITY e").Append(i).Append(" \"");
            for (var j = 0; j < 10; j++)
            {
                sb.Append("&e").Append(i - 1).Append(';');
            }

            sb.Append("\">\n");
        }

        return sb.Append("]>\n").Append(body).ToString();
    }
}
