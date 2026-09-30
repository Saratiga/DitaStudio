using System.Text.Json;
using System.Text.Json.Serialization;
using DitaStudio.Core.IO;

namespace DitaStudio.Core.Project;

/// <summary>Продукт проекта: имя (значение атрибута <c>product</c>) и необязательное описание.</summary>
public sealed record ProductInfo(string Name, string Description = "");

/// <summary>
/// Список продуктов проекта — значения для атрибута <c>product</c>, которые выбираются из выпадающего списка, а не
/// набираются вручную, и из которых собирается условная публикация. У проекта один список (файл <c>.ditastudio-products</c>,
/// JSON); из другого проекта он переносится импортом. Имя продукта — одно слово: пробел разделяет значения атрибута, поэтому
/// при добавлении он заменяется на «_».
/// </summary>
public static class ProductList
{
    private sealed class FileModel
    {
        [JsonPropertyName("products")]
        public List<ProductInfo> Products { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Имя продукта как значение атрибута: без пробелов по краям, внутренние пробелы — «_». Пусто — недопустимо.</summary>
    public static string NormalizeName(string? name) =>
        string.Join('_', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Приводит список к допустимому виду: имена нормализованы, без пустых и повторов (без учёта регистра — первый остаётся).</summary>
    public static List<ProductInfo> Clean(IEnumerable<ProductInfo> products)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductInfo>();
        foreach (var product in products)
        {
            var name = NormalizeName(product.Name);
            if (name.Length > 0 && seen.Add(name))
            {
                result.Add(new ProductInfo(name, (product.Description ?? string.Empty).Trim()));
            }
        }

        return result;
    }

    public static string ToJson(IEnumerable<ProductInfo> products) =>
        JsonSerializer.Serialize(new FileModel { Products = Clean(products) }, JsonOptions);

    /// <summary>Разбирает список из JSON файла проекта или из простого текста (по продукту в строке, описание — после «;»).</summary>
    public static List<ProductInfo> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<ProductInfo>();
        }

        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (trimmed.StartsWith('{'))
        {
            var model = JsonSerializer.Deserialize<FileModel>(trimmed, JsonOptions) ?? new FileModel();
            return Clean(model.Products ?? new List<ProductInfo>());
        }

        return Clean(text.Split('\n').Select(line =>
        {
            var parts = line.Trim().Split(';', 2);
            return new ProductInfo(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
        }));
    }

    /// <summary>Читает файл списка (проекта или экспортированный); ошибка чтения — исключение <see cref="IOException"/>.</summary>
    public static List<ProductInfo> Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new IOException($"Файл списка продуктов повреждён: {ex.Message}", ex);
        }
    }

    public static void Write(string path, IEnumerable<ProductInfo> products) =>
        AtomicFile.WriteAllText(path, ToJson(products), new System.Text.UTF8Encoding(false));

    /// <summary>Объединяет списки: к <paramref name="current"/> добавляются продукты из <paramref name="imported"/>, которых там нет.</summary>
    public static List<ProductInfo> Merge(IEnumerable<ProductInfo> current, IEnumerable<ProductInfo> imported) =>
        Clean(current.Concat(imported));
}
