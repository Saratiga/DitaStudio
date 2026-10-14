using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DitaStudio.Core.Model;
using DitaStudio.Desktop.Authoring;
using Xunit;

namespace DitaStudio.Desktop.Tests;

/// <summary>Размер картинки мышью за маркер в любом блоке: ячейка таблицы, примечание, пункт списка, рисунок в ячейке.</summary>
public sealed class ImageResizeAnywhereTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAJ0lEQVR4nGM8oaHBMBCAaUBsHbV41OJRi0ctHrV41OJRi0ctHhAAABx5AUDfR9jWAAAAAElFTkSuQmCC";

    [AvaloniaTheory]
    [InlineData("<table><tgroup cols=\"2\"><colspec colname=\"c1\"/><colspec colname=\"c2\"/><tbody><row><entry>Схема <image href=\"pic.png\"/></entry><entry>b</entry></row></tbody></tgroup></table>")]
    [InlineData("<note><p>Схема <image href=\"pic.png\"/></p></note>")]
    [InlineData("<ul><li>Пункт <image href=\"pic.png\"/><ul><li>вложенный</li></ul></li></ul>")]
    [InlineData("<table><tgroup cols=\"1\"><colspec colname=\"c1\"/><tbody><row><entry><fig><title>Рис</title><image href=\"pic.png\"/></fig></entry></row></tbody></tgroup></table>")]
    public void DragThumb_ChangesWidth(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DitaStudioImageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(Png));
        var path = Path.Combine(dir, "topic.dita");
        File.WriteAllText(path, "<concept id=\"c\"><title>Картинки</title><conbody>" + body + "</conbody></concept>");
        var document = DitaDocument.Load(path);
        var author = new AuthorView();
        author.Load(document);
        var window = new Window { Width = 900, Height = 700, Content = author };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var picture = Assert.Single(author.GetVisualDescendants().OfType<ResizableImage>());
        var before = picture.ShownWidth;
        window.CaptureRenderedFrame();
        window.MouseMove(new Point(1, 1), RawInputModifiers.None);
        window.MouseMove(picture.TranslatePoint(new Point(5, 5), window)!.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var thumb = picture.Children.OfType<Border>().Single();
        Assert.True(thumb.IsVisible);
        var corner = thumb.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(corner + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(corner + new Vector(40, 0), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var image = document.Root.Descendants().First(n => n.Name == "image");
        Assert.True(Math.Abs(picture.ShownWidth - before - 40) < 1, $"показанная ширина {picture.ShownWidth} (была {before})");
        Assert.True(Math.Abs(picture.ShownWidth - before - 40) < 1, $"показанная ширина {picture.ShownWidth} (была {before}), меню: {author.ChipEditor is not null}");
        Assert.Equal($"{Math.Round(before + 40)}px", image.GetAttribute("width"));
        Assert.Null(author.SelectedCells); // протаскивание маркера не выделяет ячейки
        window.Close();
    }
}
