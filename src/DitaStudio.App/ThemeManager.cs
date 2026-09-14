using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DitaStudio.App;

/// <summary>Переключение между светлой и тёмной палитрой и запоминание выбора между запусками.</summary>
public static class ThemeManager
{
    public enum Theme
    {
        Light,
        Dark
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DitaStudio", "theme.txt");

    public static Theme Current { get; private set; } = Theme.Light;

    /// <summary>Срабатывает после смены темы — подписчики перестраивают то, что раскрашено в коде напрямую.</summary>
    public static event Action? Changed;

    public static void Initialize() => Apply(ReadSaved(), save: false);

    public static void Toggle() => Apply(Current == Theme.Light ? Theme.Dark : Theme.Light, save: true);

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    /// <summary>Красит системную (не WPF-рисованную) рамку окна в тёмный режим через DWM —
    /// Controls.xaml раскрашивает только содержимое, заголовок остаётся Windows-нативным.
    /// Вызывать после создания окна и повторно при переключении темы.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetTitleBarDark(handle, Current == Theme.Dark);
        }
        else
        {
            window.SourceInitialized += (_, _) =>
                SetTitleBarDark(new WindowInteropHelper(window).Handle, Current == Theme.Dark);
        }
    }

    private static void SetTitleBarDark(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+/11); 19 — старые сборки Windows 10.
        if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
        }
    }

    private static void Apply(Theme theme, bool save)
    {
        Current = theme;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var paletteSource = theme == Theme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";

        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var name = dictionaries[i].Source?.OriginalString;
            if (name is "Themes/Light.xaml" or "Themes/Dark.xaml")
            {
                index = i;
                break;
            }
        }

        var palette = new ResourceDictionary { Source = new Uri(paletteSource, UriKind.Relative) };
        if (index >= 0)
        {
            dictionaries[index] = palette;
        }
        else
        {
            dictionaries.Insert(0, palette);
        }

        if (save)
        {
            SaveChoice(theme);
        }

        Changed?.Invoke();
    }

    private static Theme ReadSaved()
    {
        try
        {
            return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "dark"
                ? Theme.Dark
                : Theme.Light;
        }
        catch
        {
            return Theme.Light;
        }
    }

    private static void SaveChoice(Theme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, theme == Theme.Dark ? "dark" : "light");
        }
        catch
        {
            // настройка темы не критична — молча продолжаем со значением по умолчанию
        }
    }
}
