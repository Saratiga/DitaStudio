using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace DitaStudio.Desktop.Tests;

public class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void MainWindow_RendersInBothThemes(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var window = new MainWindow { DataContext = new SampleMain() };
            FillTrees(window);
            window.Show();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, $"main-window-{theme}.png"));

            Assert.Equal("DITA Studio — GuideSample", window.Title);
            window.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    // Деревья проекта и структуры в WPF-версии заполняет код окна — для снимка кладём
    // несколько узлов вручную.
    private static void FillTrees(MainWindow window)
    {
        var tree = window.FindControl<TreeView>("ProjectTree")!;
        var concepts = new TreeViewItem { Header = "concepts", IsExpanded = true };
        concepts.Items.Add(new TreeViewItem { Header = "about.dita — О программе" });
        concepts.Items.Add(new TreeViewItem { Header = "overview.dita — Обзор" });
        var tasks = new TreeViewItem { Header = "tasks", IsExpanded = true };
        tasks.Items.Add(new TreeViewItem { Header = "install.dita — Установка" });
        tasks.Items.Add(new TreeViewItem { Header = "first-run.dita — Первый запуск" });
        tree.Items.Add(new TreeViewItem { Header = "guide.ditamap — Руководство" });
        tree.Items.Add(concepts);
        tree.Items.Add(tasks);
        tree.SelectedItem = concepts.Items[0];

        var palette = window.FindControl<ListBox>("PaletteList")!;
        foreach (var name in new[] { "p — абзац", "ul — маркированный список", "note — примечание", "codeblock — блок кода", "fig — рисунок" })
        {
            palette.Items.Add(name);
        }
    }
}
