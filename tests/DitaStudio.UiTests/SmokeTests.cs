using Xunit;

namespace DitaStudio.UiTests;

/// <summary>
/// Автоматизированное подмножество docs/TESTPLAN.md — быстрые, не разрушающие состояние сценарии,
/// пригодные для CI на каждый PR. Полный ручной прогон чек-листа перед релизом это не заменяет
/// (WYSIWYG-вёрстка, PDF-пагинация и т.п. UI Automation не проверит — см. docs/TESTPLAN.md).
///
/// Каждый тест сам приводит приложение к нужному ему состоянию (открывает проект, если он ещё не
/// открыт) — xUnit не гарантирует порядок методов между тестовыми классами, а все классы здесь
/// делят один процесс DitaStudio.exe (см. <see cref="AppCollection"/>).
/// </summary>
[Collection("DitaStudio App")]
public sealed class SmokeTests
{
    private readonly DitaStudioAppFixture _fixture;

    public SmokeTests(DitaStudioAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void MainWindow_LaunchesWithExpectedTitle()
    {
        Assert.Contains("DITA Studio", _fixture.MainWindow.Title);
    }

    [Fact]
    public void OpenRecentProject_UpdatesStatusBarAndWindowTitle()
    {
        _fixture.OpenGuideSampleViaRecentProjects();

        // Не через ProjectTree/TreeItem — см. комментарий в OpenGuideSampleViaRecentProjects о
        // том, что содержимое вкладок TabControl не видно этому обходу автомейшн-дерева.
        var status = _fixture.Driver.ReadText("Проект открыт");
        Assert.Matches(@"Проект открыт: \d+ файлов, \d+ ключ", status ?? string.Empty);

        Assert.Contains("GuideSample", _fixture.MainWindow.Title);
    }

    [Fact]
    public void HelpAbout_OpensDialogWithVersionInfoAndCloses()
    {
        _fixture.Driver.Menu("Справка|О программе");

        _fixture.Driver.WaitForWindow("О программе", timeoutMs: 5000);
        var aboutText = string.Join(" ", _fixture.Driver.Tree("О программе"));
        Assert.Contains("DITA Studio", aboutText);

        _fixture.Driver.Click("Закрыть", "О программе");

        Assert.Throws<InvalidOperationException>(() => _fixture.Driver.ResolveWindow("О программе"));
        // Основное окно осталось на месте и по-прежнему доступно — модальный диалог не утащил его за собой.
        Assert.Contains("DITA Studio", _fixture.Driver.ResolveWindow().Title);
    }
}
