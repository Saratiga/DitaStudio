using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using DitaStudio.Presentation.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Проверка орфографии по словарям Hunspell (ru_RU, en_US), поставляемым с программой.</summary>
public sealed class SpellCheckerTests
{
    private static SpellChecker Loaded()
    {
        var checker = SpellChecker.Default;
        checker.EnsureLoadedAsync().Wait(TimeSpan.FromSeconds(60));
        Assert.True(checker.IsReady, "словари не загрузились — нет папки Dictionaries рядом с программой?");
        return checker;
    }

    [Fact]
    public void RussianAndEnglishWords_AreCheckedByTheirDictionaries()
    {
        var checker = Loaded();
        Assert.True(checker.Check("документация"));
        Assert.True(checker.Check("ещё"));
        Assert.True(checker.Check("documentation"));
        Assert.False(checker.Check("документацыя"));
        Assert.False(checker.Check("documantation"));
        Assert.Contains("документация", checker.Suggest("документацыя"));
    }

    [Fact]
    public void AcronymsCodeLikeWordsAndIgnoredWords_AreNotFlagged()
    {
        var checker = Loaded();
        Assert.True(checker.Check("DITA"));
        Assert.True(checker.Check("WebView"));
        Assert.True(checker.Check("x64"));
        Assert.True(checker.Check("кое-где"));

        var text = "Настройка сервера по-новому в конфигурацыи";
        var found = checker.Find(text);
        Assert.True(found.Count == 1, string.Join(", ", found.Select(f => text.Substring(f.Start, f.Length))));
        Assert.Equal("конфигурацыи", text.Substring(found.Single().Start, found.Single().Length));

        checker.Ignore("конфигурацыи");
        Assert.Empty(checker.Find(text));
    }

    [AvaloniaFact]
    public void BlockEditor_SkipsCode_AndFixesWordFromMenu_KeepingFormatting()
    {
        Loaded();
        var document = DitaDocument.Parse(
            "<concept id=\"c\"><title>T</title><conbody><p>Откройте <b>настройкы</b> и <codeph>abcdefg</codeph>.</p></conbody></concept>");
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = 800, Height = 400, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var p = document.Root.FirstElement("conbody")!.FirstElement("p")!;
        var editor = author.EditorFor(p)!;
        var word = Assert.Single(editor.Misspellings);
        Assert.Equal("настройкы", editor.Text.Substring(word.Start, word.Length));

        var menu = editor.BuildContextMenu(word.Start + 2);
        var fix = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "настройки");
        fix.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        editor.Flush();

        Assert.Equal("<p>Откройте <b>настройки</b> и <codeph>abcdefg</codeph>.</p>", XmlSerializer.ToXml(p));
        Assert.Empty(editor.Misspellings);
        window.Close();
    }
}
