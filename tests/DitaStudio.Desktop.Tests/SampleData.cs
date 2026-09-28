using System.Collections.ObjectModel;

namespace DitaStudio.Desktop.Tests;

// Примерные данные для снимков главного окна — те же имена свойств, к которым привязана
// разметка (MainWindow.axaml). Настоящие ViewModel'и появятся на этапе 2 миграции.
public sealed class SampleMain
{
    public string WindowTitle => "DITA Studio — GuideSample";
    public string StatusText => "Проект открыт: 14 файлов, 6 ключей.";
    public string ContextText => "concept › conbody › p";
    public int BottomTabIndex { get; set; }
    public SampleProject ProjectPanel { get; } = new();
    public SampleDocuments Documents { get; } = new();
    public SampleValidation Validation { get; } = new();
    public SampleMap Map { get; } = new();
    public SamplePublish Publish { get; } = new();
}

public sealed class SampleProject
{
    public ObservableCollection<SampleKey> Keys { get; } = new()
    {
        new("product", "concepts/about.dita"),
        new("install", "tasks/install.dita"),
        new("settings", "reference/settings.dita"),
    };

    public string ScopedKeysHintText => string.Empty;
    public bool ScopedKeysHintVisible => false;
}

public sealed record SampleKey(string Key, string Href);

public sealed class SampleDocuments
{
    public ObservableCollection<SampleTab> Tabs { get; } = new()
    {
        new("about.dita"),
        new("install.dita •"),
    };
}

public sealed record SampleTab(string Title)
{
    public object? Pane => null;
}

public sealed class SampleValidation
{
    public ObservableCollection<SampleIssue> Issues { get; } = new()
    {
        new("Ошибка", "Элемент <b> не допускается в <title> на этой позиции.", "/concept/title", "concepts/about.dita"),
        new("Предупреждение", "Пустой элемент <p>.", "/task/taskbody/context/p", "tasks/install.dita"),
        new("Информация", "Ключ settings определён в двух картах.", string.Empty, "guide.ditamap"),
    };
}

public sealed record SampleIssue(string SeverityText, string Message, string Location, string FilePath);

public sealed class SampleMap
{
    public ObservableCollection<SampleMapFile> Maps { get; } = new() { new("guide.ditamap") };
    public SampleMapFile? SelectedMap => Maps[0];
}

public sealed record SampleMapFile(string RelativePath);

public sealed class SamplePublish
{
    public string BuildLogText => "Сборка карты guide.ditamap…\nФайлов записано: 16\n";
}
