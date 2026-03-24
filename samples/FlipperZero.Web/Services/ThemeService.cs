using Microsoft.JSInterop;

namespace FlipperZero.Web.Services;

/// <summary>
/// Manages light/dark theme. Persists the user's preference in localStorage and
/// applies it to the &lt;html data-theme="..."&gt; attribute so CSS variables switch.
/// Defaults to the OS <c>prefers-color-scheme</c> when no preference is stored.
/// </summary>
public sealed class ThemeService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private bool _isDark;
    private bool _initialized;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>True when dark theme is currently active.</summary>
    public bool IsDark => _isDark;

    /// <summary>Fires whenever the theme changes.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Reads the stored preference and system setting, then applies the theme.
    /// Must be called once after JS interop is available (i.e., from OnAfterRenderAsync).
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // theme.js already set the attribute on <html> to avoid a flash.
            // Just read what it decided so our C# state matches.
            var attr = await _js.InvokeAsync<string?>(
                "eval", "document.documentElement.getAttribute('data-theme')")
                .ConfigureAwait(false);

            _isDark = attr == "dark";
        }
        catch
        {
            _isDark = false;
        }
    }

    /// <summary>Toggles between light and dark, persists the choice.</summary>
    public async Task ToggleAsync()
    {
        _isDark = !_isDark;
        await ApplyAsync().ConfigureAwait(false);
        StateChanged?.Invoke();
    }

    private async Task ApplyAsync()
    {
        var theme = _isDark ? "dark" : "light";
        try
        {
            await _js.InvokeVoidAsync("eval",
                $"document.documentElement.setAttribute('data-theme','{theme}');" +
                $"localStorage.setItem('fz-theme','{theme}')")
                .ConfigureAwait(false);
        }
        catch { /* JS interop may be unavailable during prerender */ }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
