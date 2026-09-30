using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Г9: список продуктов проекта, импорт/экспорт, условная сборка по выбранным продуктам.
internal static partial class CoreChecks
{
    internal static void ProductListTests()
    {
        Section("Список продуктов проекта");

        Check(ProductList.NormalizeName("  Мой  продукт ") == "Мой_продукт" && ProductList.NormalizeName("   ") == string.Empty && ProductList.NormalizeName(null) == string.Empty,
            "имя продукта — одно слово: пробелы заменяются на «_»");
        var cleaned = ProductList.Clean(new[] { new ProductInfo("Альфа", " первый "), new ProductInfo("альфа"), new ProductInfo(" "), new ProductInfo("Бета гамма") });
        Check(cleaned.Select(p => p.Name).SequenceEqual(new[] { "Альфа", "Бета_гамма" }) && cleaned[0].Description == "первый",
            "очистка: без пустых и повторов (без учёта регистра), описание обрезается");

        var json = ProductList.ToJson(cleaned);
        Check(ProductList.Parse(json).Select(p => (p.Name, p.Description)).SequenceEqual(cleaned.Select(p => (p.Name, p.Description))) && json.Contains("Альфа"), "JSON: список читается обратно без потерь");
        var text = ProductList.Parse("Альфа; основной\nБета\n\nАльфа\n");
        Check(text.Select(p => p.Name).SequenceEqual(new[] { "Альфа", "Бета" }) && text[0].Description == "основной", "простой текст: по продукту в строке, описание после «;»");
        Check(ProductList.Parse(string.Empty).Count == 0 && ProductList.Merge(new[] { new ProductInfo("А") }, new[] { new ProductInfo("Б"), new ProductInfo("а") }).Select(p => p.Name).SequenceEqual(new[] { "А", "Б" }),
            "пустой файл — пустой список; объединение добавляет только недостающее");

        WithProject(new Dictionary<string, string>
        {
            ["t.dita"] = """
                <topic id="t"><title>T</title><body>
                <p>ОБЩИЙ</p><p product="Альфа">ТОЛЬКО_АЛЬФА</p><p product="Бета">ТОЛЬКО_БЕТА</p><p product="Альфа Бета">ОБА</p><p product="Гамма">ТОЛЬКО_ГАММА</p>
                </body></topic>
                """,
            ["m.ditamap"] = "<map><title>M</title><topicref href=\"t.dita\"/></map>"
        }, (root, project) =>
        {
            project.SetProducts(new[] { new ProductInfo("Альфа", "основной"), new ProductInfo("Бета") });
            Check(File.Exists(Path.Combine(root, ".ditastudio-products")) && project.Products.Count == 2, "список сохраняется в .ditastudio-products");
            var reopened = new DitaProject(root);
            Check(reopened.Products.Select(p => p.Name).SequenceEqual(new[] { "Альфа", "Бета" }) && reopened.Products[0].Description == "основной", "список переживает переоткрытие проекта");

            // Экспорт и импорт в другой проект.
            var exported = Path.Combine(root, "export.ditastudio-products");
            ProductList.Write(exported, project.Products);
            var other = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(other);
            try
            {
                var second = new DitaProject(other);
                second.SetProducts(ProductList.Merge(second.Products, ProductList.Read(exported)));
                Check(second.Products.Select(p => p.Name).SequenceEqual(new[] { "Альфа", "Бета" }), "импорт списка в другой проект");
            }
            finally
            {
                Directory.Delete(other, true);
            }

            // Условная сборка: отмечены Альфа и Бета — входят общий, оба, «Альфа Бета»; Гамма не отмечена — исключена.
            string Html(params string[] excludeProducts)
            {
                var options = new PublishOptions { OutputDirectory = Path.Combine(root, "out" + Guid.NewGuid().ToString("N")[..6]), SingleFile = true };
                if (excludeProducts.Length > 0)
                {
                    options.ExcludeConditions["product"] = new HashSet<string>(excludeProducts);
                }

                return File.ReadAllText(new HtmlPublisher(project).Publish(Path.Combine(root, "m.ditamap"), options).EntryFile);
            }

            var all = Html();
            Check(new[] { "ОБЩИЙ", "ТОЛЬКО_АЛЬФА", "ТОЛЬКО_БЕТА", "ОБА", "ТОЛЬКО_ГАММА" }.All(all.Contains), "без исключений входит всё");
            var alpha = Html("Бета", "Гамма");
            Check(alpha.Contains("ОБЩИЙ") && alpha.Contains("ТОЛЬКО_АЛЬФА") && alpha.Contains("ОБА") && !alpha.Contains("ТОЛЬКО_БЕТА") && !alpha.Contains("ТОЛЬКО_ГАММА"),
                "выбран только «Альфа»: общий, «Альфа» и элемент с несколькими продуктами (есть Альфа) входят, «Бета» и «Гамма» нет");
            var none = Html("Альфа", "Бета", "Гамма");
            Check(none.Contains("ОБЩИЙ") && !none.Contains("ОБА") && !none.Contains("ТОЛЬКО_АЛЬФА"), "ничего не выбрано: входят только элементы без product");
        });
    }
}
