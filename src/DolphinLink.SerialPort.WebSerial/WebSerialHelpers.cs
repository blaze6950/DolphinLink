using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DolphinLink.SerialPort.WebSerial;

/// <summary>
/// General-purpose helpers for working with the WebSerial API in a Blazor WASM context.
/// </summary>
public static class WebSerialHelpers
{
    /// <summary>
    /// Returns <c>true</c> if the WebSerial API is available in the current browser.
    ///
    /// <para>
    /// WebSerial is only supported in Chromium-based browsers (Chrome, Edge, Opera).
    /// Firefox and Safari do not support it. Call this before displaying any
    /// port-picker UI and show a browser compatibility warning if it returns <c>false</c>.
    /// </para>
    ///
    /// <para>
    /// Safe to call on any platform — returns <c>false</c> when not running in a browser
    /// or before the interop module has been loaded.
    /// </para>
    /// </summary>
    public static bool IsSupported()
    {
        if (!OperatingSystem.IsBrowser())
        {
            return false;
        }

        try
        {
            return WebSerialInterop.IsSupported();
        }
        catch
        {
            // Module not yet loaded or running outside a browser context.
            return false;
        }
    }

    /// <summary>
    /// Asynchronously checks whether the WebSerial API is available in the current browser.
    ///
    /// <para>
    /// Unlike <see cref="IsSupported"/>, this method loads the JS interop module first,
    /// so it is safe to call during component initialization (e.g. <c>OnInitializedAsync</c>)
    /// before any port has been opened.
    /// </para>
    ///
    /// <para>
    /// Returns <c>false</c> when not running in a browser or if the browser does not support
    /// WebSerial. Throws if the interop module fails to load (e.g. 404), so callers can
    /// distinguish a missing asset from a genuinely unsupported browser.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("browser")]
    public static async Task<bool> IsSupportedAsync()
    {
        if (!OperatingSystem.IsBrowser())
        {
            return false;
        }

        await JSHost.ImportAsync(WebSerialInterop.ModuleName, WebSerialInterop.GetModuleUrl());
        return WebSerialInterop.IsSupported();
    }
}
