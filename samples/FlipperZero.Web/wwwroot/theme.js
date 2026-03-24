// theme.js — runs before Blazor loads to apply the saved theme immediately,
// preventing a flash of the wrong theme on page load.
(function () {
    var stored = localStorage.getItem('fz-theme');
    var theme;
    if (stored === 'dark' || stored === 'light') {
        theme = stored;
    } else {
        // No saved preference — follow the OS setting.
        theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    document.documentElement.setAttribute('data-theme', theme);
}());
