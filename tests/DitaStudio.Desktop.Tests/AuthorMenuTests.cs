using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Контекстные меню «Автора»: открыто не больше одного, щелчок мимо и прокрутка закрывают его.</summary>
public sealed class AuthorMenuTests
{
    private const string Topic =
        "<concept id=\"c\"><title>Меню</title><conbody><p>Первый абзац с текстом.</p><p>Второй абзац.</p>" +
        "<table><tgroup cols=\"2\"><colspec colname=\"c1\"/><colspec colname=\"c2\"/><tbody><row><entry>a</entry><entry>b</entry></row></tbody></tgroup></table>" +
        "</conbody></concept>";

    private static Point Center(Visual visual, Window window) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, Math.Min(8, visual.Bounds.Height / 2)), window)!.Value;

    [AvaloniaFact]
    public void RightClick_OpensOneMenu_ClickElsewhereCloses_SecondRightClickReplaces()
    {
        var document = DitaDocument.Parse(Topic);
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = 800, Height = 600, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        var paragraphs = document.Root.Descendants().Where(n => n.Name == "p").ToList();
        var first = author.EditorFor(paragraphs[0])!;
        var second = author.EditorFor(paragraphs[1])!;
        var cell = author.EditorFor(document.Root.Descendants().First(n => n.Name == "entry"))!;

        window.MouseDown(Center(first, window), MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(Center(first, window), MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var menu = AuthorMenus.Current;
        Assert.NotNull(menu);

        // Правая кнопка по ячейке таблицы: прежнее меню закрыто, открыто новое.
        window.MouseDown(Center(cell, window), MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(Center(cell, window), MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu!.IsOpen);
        var cellMenu = AuthorMenus.Current;
        Assert.NotNull(cellMenu);
        Assert.NotSame(menu, cellMenu);

        // Левая кнопка мимо меню — меню закрыто.
        window.MouseDown(Center(second, window), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(Center(second, window), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(AuthorMenus.Current);
        Assert.False(cellMenu!.IsOpen);

        // Закрытие извне (уход из окна вызывает то же AuthorMenus.Close).
        window.MouseDown(Center(first, window), MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(Center(first, window), MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(AuthorMenus.Current);
        AuthorMenus.Close();
        Assert.Null(AuthorMenus.Current);

        window.Close();
    }
}
