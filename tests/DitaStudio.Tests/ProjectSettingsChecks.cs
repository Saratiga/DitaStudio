using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;

namespace DitaStudio.Tests;

// Служебные настройки проекта (.ditastudio-*) — класс ProjectSettings сам по себе и через DitaProject.
internal static partial class CoreChecks
{
    internal static void ProjectSettingsTests()
    {
        Section("Настройки проекта (ProjectSettings)");

        var root = Path.Combine(Path.GetTempPath(), "DitaStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // --- запись, повторное чтение, удаление файла у пустой настройки
            var settings = new ProjectSettings(root);
            settings.SetCustomCssPath(@"styles\my.css");
            settings.SetDitavalPath("  ");
            settings.SetExternalDtdPath("dtd/custom.dtd");
            settings.SetConditions(new Dictionary<string, HashSet<string>> { ["audience"] = new() { "admin", "dev" } }, true);
            settings.SetProducts(new[] { new ProductInfo("Alpha", "Первый"), new ProductInfo("Alpha", "дубль"), new ProductInfo("Beta") });
            settings.SetPinnedFiles(new[] { @"a\one.dita","A/ONE.dita", "two.dita" });
            settings.SetPdfHeaderFooter(true, "Шапка", "Подвал");
            Check(settings.CustomCssPath == "styles/my.css", "путь css нормализован к слэшам вперёд");
            Check(settings.DitavalPath is null && !File.Exists(Path.Combine(root, ".ditastudio-ditaval")), "пустой путь — настройки нет, файла нет");
            Check(settings.PinnedFiles.Count == 2, $"закреплённые без дублей (регистр не важен): {settings.PinnedFiles.Count}");

            var reread = new ProjectSettings(root);
            Check(reread.CustomCssPath == "styles/my.css", "css переживает перечитывание");
            Check(reread.ExternalDtdPath == "dtd/custom.dtd", "внешний DTD переживает перечитывание");
            Check(reread.ShowDraftComments && reread.ExcludedConditionValues["audience"].SetEquals(new[] { "admin", "dev" }),
                "условия переживают перечитывание");
            Check(reread.Products.Select(p => p.Name).SequenceEqual(new[] { "Alpha", "Beta" }), "продукты очищены от дублей и сохранены");
            Check(reread.PinnedFiles.SequenceEqual(new[] { "a/one.dita", "two.dita" }), "закреплённые сохранены");
            Check(reread.PdfShowHeaderFooter && reread.PdfHeaderText == "Шапка" && reread.PdfFooterText == "Подвал", "колонтитулы PDF сохранены");
            Check(reread.Warnings.Count == 0, "без сбоев — без предупреждений");

            settings.SetConditions(new Dictionary<string, HashSet<string>>(), false);
            settings.SetPinnedFiles(Array.Empty<string>());
            settings.SetPdfHeaderFooter(false, null, null);
            settings.SetProducts(Array.Empty<ProductInfo>());
            Check(!File.Exists(Path.Combine(root, ".ditastudio-conditions")), "пустые условия — файл удалён");
            Check(!File.Exists(Path.Combine(root, ".ditastudio-pinned")), "нет закреплённых — файл удалён");
            Check(!File.Exists(Path.Combine(root, ".ditastudio-pdf-header")), "колонтитулы выключены и пусты — файл удалён");
            Check(!File.Exists(Path.Combine(root, ".ditastudio-products")), "нет продуктов — файл удалён");

            // --- проекты-источники: Add/Remove сообщают, что изменилось
            var other = Path.Combine(root, "other");
            Check(settings.AddReferencedProject(other), "первое подключение источника — изменение");
            Check(!settings.AddReferencedProject(other.ToUpperInvariant()) || OperatingSystem.IsLinux(), "повтор (регистр не важен на Windows) — не изменение");
            Check(settings.RemoveReferencedProject(other), "отключение подключённого источника — изменение");
            Check(!settings.RemoveReferencedProject(other), "отключение неподключённого — не изменение");
            Check(new ProjectSettings(root, loadReferencedProjects: false).ReferencedProjectPaths.Count == 0,
                "источник-проект чужие источники не загружает");

            // --- непрочитанный служебный файл: значение по умолчанию + предупреждение, не исключение
            // (блокировка файла — только на Windows: на Linux чтение занятого файла не падает)
            var broken = Path.Combine(root, "broken");
            Directory.CreateDirectory(broken);
            var cssFile = Path.Combine(broken, ".ditastudio-css");
            File.WriteAllText(cssFile, "old.css");
            if (OperatingSystem.IsWindows())
            {
                var seen = new List<string>();
                using (new FileStream(cssFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var locked = new ProjectSettings(broken);
                    Check(locked.CustomCssPath is null, "нечитаемый css-файл — настройка по умолчанию");
                    Check(locked.Warnings.Count == 1 && locked.Warnings[0].Contains(".ditastudio-css"), "сбой чтения попал в предупреждения");
                    locked.Warning += seen.Add;
                    locked.SetCustomCssPath("x.css");
                    Check(locked.CustomCssPath == "x.css", "значение действует, даже если записать не удалось");
                    Check(seen.Count == 1 && seen[0].Contains("Не удалось сохранить"), "сбой записи сообщён событием сразу");
                }
            }

            File.WriteAllText(Path.Combine(broken, ".ditastudio-docx"), "{ не json");
            var badDocx = new ProjectSettings(broken);
            Check(badDocx.DocxLayoutWarning is not null && badDocx.DocxLayout is not null, "битый .ditastudio-docx — предупреждение и вёрстка по умолчанию");

            // --- DitaProject переадресует в Settings
            var project = new DitaProject(root);
            project.SetPdfHeaderFooter(true, "H", "F");
            project.SetProducts(new[] { new ProductInfo("P") });
            Check(ReferenceEquals(project.DocxLayout, project.Settings.DocxLayout), "DocxLayout проекта — из Settings");
            Check(project.Settings.PdfShowHeaderFooter && project.PdfHeaderText == "H", "колонтитулы через проект видны в Settings");
            Check(project.Products.Count == 1 && project.Settings.Products.Count == 1, "продукты через проект видны в Settings");
            Check(project.SettingsWarnings == project.Settings.Warnings, "предупреждения проекта — предупреждения Settings");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // временная папка удалится системой
            }
        }
    }
}
