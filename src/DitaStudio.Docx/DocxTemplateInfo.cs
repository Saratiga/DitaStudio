namespace DitaStudio.Docx;

/// <summary>Сведения о шаблоне DOCX для интерфейса (список стилей таблиц в «Оформление DOCX»).</summary>
public static class DocxTemplateInfo
{
    /// <summary>Стили таблиц шаблона (id, название); пусто — файла нет, он не открылся или стилей таблиц в нём нет.</summary>
    public static IReadOnlyList<(string Id, string Name)> TableStyles(string path) => DocxTemplate.ReadTableStyles(path);
}
