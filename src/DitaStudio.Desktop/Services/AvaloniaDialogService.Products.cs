using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DitaStudio.Core.Project;
using DitaStudio.Presentation.Services;

namespace DitaStudio.Desktop.Services;

// Список продуктов проекта и «Условия сборки» с выбором продуктов.
public sealed partial class AvaloniaDialogService
{
    /// <summary>
    /// Окно «Список продуктов»: добавить и удалить продукты проекта, импортировать список из файла другого проекта (объединить или
    /// заменить) и экспортировать свой. Возвращает новый список или null (отмена).
    /// </summary>
    public async Task<IReadOnlyList<ProductInfo>?> EditProductsAsync(DitaProject project)
    {
        var products = project.Products.ToList();
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Wrapped("Продукты проекта — значения атрибута product. Они выбираются из списка на панели «Атрибуты» (можно несколько) " +
                                   "и отмечаются в «Условиях сборки». Имя — одно слово: пробел заменяется на «_»."));

        var list = new ListBox { Height = 200, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(list, "Продукты проекта");
        panel.Children.Add(list);

        void Refresh()
        {
            list.ItemsSource = products.Select(p => p.Description.Length > 0 ? $"{p.Name} — {p.Description}" : p.Name).ToList();
        }

        Refresh();

        var nameBox = Input(string.Empty);
        nameBox.Watermark = "Имя продукта";
        AutomationProperties.SetName(nameBox, "Имя нового продукта");
        var descriptionBox = Input(string.Empty);
        descriptionBox.Watermark = "Описание (необязательно)";
        AutomationProperties.SetName(descriptionBox, "Описание нового продукта");
        var add = new Button { Content = "Добавить", Padding = new Thickness(12, 3), Margin = new Thickness(0, 6, 0, 0) };
        var remove = new Button { Content = "Удалить выбранный", Padding = new Thickness(12, 3), Margin = new Thickness(8, 6, 0, 0) };
        var status = Muted(new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        void AddCurrent()
        {
            var name = ProductList.NormalizeName(nameBox.Text);
            if (name.Length == 0)
            {
                return;
            }

            if (products.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                status.Text = $"Продукт «{name}» уже есть в списке.";
                return;
            }

            products.Add(new ProductInfo(name, (descriptionBox.Text ?? string.Empty).Trim()));
            nameBox.Text = string.Empty;
            descriptionBox.Text = string.Empty;
            status.Text = $"Добавлен: {name}.";
            Refresh();
        }

        add.Click += (_, _) => AddCurrent();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                AddCurrent();
                e.Handled = true;
            }
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedIndex is >= 0 and var index && index < products.Count)
            {
                status.Text = $"Удалён из списка: {products[index].Name}. Значения product в топиках не меняются.";
                products.RemoveAt(index);
                Refresh();
            }
        };

        var import = new Button { Content = "Импорт из файла…", Padding = new Thickness(12, 3), Margin = new Thickness(0, 6, 0, 0) };
        var export = new Button { Content = "Экспорт в файл…", Padding = new Thickness(12, 3), Margin = new Thickness(8, 6, 0, 0) };
        var filters = new[] { new FileFilter("Список продуктов", "*.ditastudio-products", "*.json", "*.txt"), FileFilter.All };
        import.Click += async (_, _) =>
        {
            var file = await _files.OpenFileAsync("Список продуктов другого проекта", filters, project.RootPath);
            if (file is null)
            {
                return;
            }

            List<ProductInfo> imported;
            try
            {
                imported = ProductList.Read(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Text = "Не удалось прочитать список: " + ex.Message;
                return;
            }

            if (imported.Count == 0)
            {
                status.Text = "В файле нет продуктов.";
                return;
            }

            var answer = products.Count == 0
                ? AskResult.Yes
                : await AskAsync("Импорт списка продуктов",
                    $"В файле продуктов: {imported.Count}. Да — добавить недостающие к текущему списку, Нет — заменить текущий список списком из файла.",
                    AskButtons.YesNoCancel);
            if (answer == AskResult.Cancel)
            {
                return;
            }

            var before = products.Count;
            products = answer == AskResult.Yes ? ProductList.Merge(products, imported) : imported;
            status.Text = answer == AskResult.Yes ? $"Добавлено продуктов: {products.Count - before}." : $"Список заменён: {products.Count} продуктов.";
            Refresh();
        };
        export.Click += async (_, _) =>
        {
            var file = await _files.SaveFileAsync("Сохранить список продуктов", filters, "products.ditastudio-products", project.RootPath);
            if (file is null)
            {
                return;
            }

            try
            {
                ProductList.Write(file, products);
                status.Text = "Список сохранён: " + file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Text = "Не удалось сохранить: " + ex.Message;
            }
        };

        panel.Children.Add(Label("Новый продукт"));
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { add, remove } });
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { import, export } });
        panel.Children.Add(status);

        IReadOnlyList<ProductInfo>? result = null;
        var window = Shell("Список продуктов", new ScrollViewer { Content = panel }, 520, 640);
        panel.Children.Add(Buttons(window, () => result = ProductList.Clean(products)));
        return await ShowAsync(window) ? result : null;
    }

    /// <summary>
    /// «Условия сборки»: продукты отмечаются флажками — в публикацию попадают отмеченные (и элементы без product); у элемента
    /// с несколькими продуктами — если отмечен хотя бы один. Остальные условные атрибуты — как раньше: отметка исключает значение.
    /// Кнопка «Список продуктов…» правит список проекта.
    /// </summary>
    public async Task<ConditionsResult?> PublishConditionsAsync(DitaProject project, ConditionsResult? current)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var found = CollectConditionValues(project);

        // ---- продукты: отмечены — входят в публикацию
        panel.Children.Add(Label("Продукты: что войдёт в публикацию"));
        panel.Children.Add(Muted(new TextBlock
        {
            Text = "Отметьте продукты, для которых собирается публикация. Элементы без атрибута product входят всегда; " +
                   "элемент с несколькими продуктами — если отмечен хотя бы один.",
            TextWrapping = TextWrapping.Wrap
        }));
        var productPanel = new StackPanel();
        var productChecks = new List<(string Name, CheckBox Box)>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        var excludedNow = current is not null && current.Exclude.TryGetValue("product", out var ex) ? ex : new HashSet<string>();

        void BuildProductChecks(IEnumerable<string> names, Func<string, bool> isIncluded)
        {
            productPanel.Children.Clear();
            productChecks.Clear();
            var all = names.Distinct(StringComparer.Ordinal).ToList();
            foreach (var name in all)
            {
                var box = new CheckBox { Content = name, Margin = new Thickness(8, 2, 0, 2), IsChecked = isIncluded(name) };
                AutomationProperties.SetName(box, "Продукт " + name);
                productChecks.Add((name, box));
                productPanel.Children.Add(box);
            }

            if (all.Count == 0)
            {
                productPanel.Children.Add(Muted(new TextBlock { Text = "Список продуктов пуст — добавьте продукты кнопкой ниже.", Margin = new Thickness(0, 4, 0, 0) }));
            }
        }

        IEnumerable<string> ProductNames() =>
            project.Products.Select(p => p.Name).Concat(found.TryGetValue("product", out var values) ? values.OrderBy(v => v, StringComparer.Ordinal) : Enumerable.Empty<string>());

        BuildProductChecks(ProductNames(), name => !excludedNow.Contains(name));
        panel.Children.Add(productPanel);

        var editProducts = new Button { Content = "Список продуктов…", Padding = new Thickness(12, 3), Margin = new Thickness(0, 6, 0, 0) };
        editProducts.Click += async (_, _) =>
        {
            var edited = await EditProductsAsync(project);
            if (edited is null)
            {
                return;
            }

            project.SetProducts(edited);
            var states = productChecks.ToDictionary(c => c.Name, c => c.Box.IsChecked == true, StringComparer.Ordinal);
            BuildProductChecks(ProductNames(), name => !states.TryGetValue(name, out var was) || was); // новые продукты — отмечены
        };
        panel.Children.Add(editProducts);

        // ---- остальные условные атрибуты: отметка исключает значение
        panel.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 14, 0, 0) });
        panel.Children.Add(Wrapped("Остальные условные атрибуты: отметьте значения, которые нужно исключить из сборки."));
        var others = ConditionChecks(panel, project, (attribute, value) =>
            current is not null && current.Exclude.TryGetValue(attribute, out var excluded) && excluded.Contains(value), skipAttribute: "product");

        var drafts = new CheckBox
        {
            Content = "Включать черновые комментарии (draft-comment)",
            Margin = new Thickness(0, 14, 0, 0),
            IsChecked = current?.ShowDraftComments ?? false
        };
        panel.Children.Add(drafts);

        ConditionsResult? result = null;
        var window = Shell("Условия сборки", new ScrollViewer { Content = panel }, 500, 640);
        panel.Children.Add(Buttons(window, () =>
        {
            var exclude = CheckedExclusions(others);
            var notIncluded = productChecks.Where(c => c.Box.IsChecked != true).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            if (notIncluded.Count > 0)
            {
                exclude["product"] = notIncluded;
            }

            result = new ConditionsResult(exclude, drafts.IsChecked == true);
        }));
        return await ShowAsync(window) ? result : null;
    }
}
