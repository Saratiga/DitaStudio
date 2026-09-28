using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;

namespace DitaStudio.Desktop.Preview;

/// <summary>Браузер CefGlue с доступом к хосту Chromium — нужен для печати в PDF
/// (<c>UnderlyingBrowser</c> у базового контрола защищённый).</summary>
public sealed class HostedCefBrowser : AvaloniaCefBrowser
{
    protected override Type StyleKeyOverride => typeof(AvaloniaCefBrowser);

    /// <summary>Хост Chromium; null, пока браузер не создан.</summary>
    public CefBrowserHost? Host => UnderlyingBrowser?.GetHost();
}
