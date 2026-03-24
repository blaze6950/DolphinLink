using System.Text.Json;
using DolphinLink.Client;
using DolphinLink.Client.Abstractions;
using RpcJsonNormalizer = DolphinLink.Client.RpcJsonNormalizer;

namespace DolphinLink.Web.Services;

/// <summary>
/// A buffered log entry: the raw <see cref="RpcLogEntry"/> from the client
/// plus a wall-clock <see cref="Timestamp"/> captured at arrival time.
/// </summary>
public readonly record struct ConsoleEntry(RpcLogEntry Entry, DateTime Timestamp);

/// <summary>
/// Singleton service that implements <see cref="IRpcDiagnostics"/> and buffers every
/// RPC log entry so the <c>RpcConsole</c> Blazor component can render them in the UI.
///
/// <para>
/// This replaces the old <c>BrowserDiagnostics</c> class: it still writes raw JSON to
/// <see cref="Console.WriteLine"/> (which maps to <c>console.log</c> in Blazor WASM)
/// and additionally stores each entry for in-page display with a wall-clock timestamp.
/// </para>
///
/// <para>
/// <see cref="IRpcDiagnostics.Log"/> is called synchronously from the reader/writer loop
/// and must never throw or block.  All state mutations here are safe because Blazor WASM
/// runs on a single cooperative thread — there is no actual concurrency.
/// </para>
/// </summary>
public sealed class RpcConsoleService : IRpcDiagnostics
{
    private const int MaxEntries = 1000;

    private static readonly JsonSerializerOptions PrettyPrintOptions = new() { WriteIndented = true };

    private readonly List<ConsoleEntry> _entries = new();

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Buffered log entries, oldest first.  Never exceeds 1000 items.</summary>
    public IReadOnlyList<ConsoleEntry> Entries => _entries;

    /// <summary>
    /// When <c>true</c> the console displays humanized JSON (expanded wire keys,
    /// enum names, bool values) via <see cref="Client.RpcJsonNormalizer.Normalize"/>.
    /// </summary>
    public bool HumanizeEnabled { get; private set; }

    /// <summary>
    /// When <c>true</c> the console pretty-prints the (possibly humanized) JSON
    /// with newlines and indentation for easier reading.
    /// </summary>
    public bool PrettyPrintEnabled { get; private set; }

    /// <summary>Raised on the same synchronous call as <see cref="Log"/>.</summary>
    public event Action? StateChanged;

    // ── IRpcDiagnostics ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Log(RpcLogEntry entry)
    {
        // Mirror to browser DevTools console.
        Console.WriteLine(entry.RawJson);

        if (_entries.Count >= MaxEntries)
            _entries.RemoveAt(0);

        _entries.Add(new ConsoleEntry(entry, DateTime.Now));
        StateChanged?.Invoke();
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>Removes all buffered entries.</summary>
    public void Clear()
    {
        _entries.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>Toggles humanized output on or off.</summary>
    public void ToggleHumanize()
    {
        HumanizeEnabled = !HumanizeEnabled;
        StateChanged?.Invoke();
    }

    /// <summary>Toggles pretty-print (JSON indentation) on or off.</summary>
    public void TogglePrettyPrint()
    {
        PrettyPrintEnabled = !PrettyPrintEnabled;
        StateChanged?.Invoke();
    }

    // ── Helpers used by the UI component ─────────────────────────────────────

    /// <summary>
    /// Returns the text to display for a given entry's JSON body, applying humanization
    /// and/or pretty-printing according to the current toggle state.
    /// </summary>
    public string GetDisplayJson(in ConsoleEntry ce)
    {
        var raw = ce.Entry.RawJson ?? string.Empty;

        // Humanize first (key expansion, enum resolution, bool conversion).
        var text = HumanizeEnabled
            ? RpcJsonNormalizer.Normalize(raw, ce.Entry.CommandName)
            : raw;

        // Then pretty-print if requested.
        if (PrettyPrintEnabled)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                text = JsonSerializer.Serialize(doc.RootElement, PrettyPrintOptions);
            }
            catch
            {
                // Malformed JSON — return as-is.
            }
        }

        return text;
    }

    /// <summary>
    /// Extracts daemon-side metrics from a response entry's raw JSON.
    /// Returns a display string like <c>parse:1 dispatch:0 execute:3 serialize:0 total:4ms</c>,
    /// or <c>null</c> if the entry has no <c>_m</c> metrics object.
    /// </summary>
    public static string? GetMetrics(in ConsoleEntry ce)
    {
        if (ce.Entry.Kind != RpcLogKind.ResponseReceived) return null;
        var raw = ce.Entry.RawJson;
        if (raw is null || !raw.Contains("\"_m\"")) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("_m", out var m)) return null;

            var pr = m.TryGetProperty("pr", out var v) ? v.GetInt32() : -1;
            var dp = m.TryGetProperty("dp", out v) ? v.GetInt32() : -1;
            var ex = m.TryGetProperty("ex", out v) ? v.GetInt32() : -1;
            var sr = m.TryGetProperty("sr", out v) ? v.GetInt32() : -1;
            var tt = m.TryGetProperty("tt", out v) ? v.GetInt32() : -1;

            static string Fmt(int v) => v == 0 ? "<1ms" : $"{v}ms";
            return $"parse:{Fmt(pr)} dispatch:{Fmt(dp)} execute:{Fmt(ex)} serialize:{Fmt(sr)} total:{Fmt(tt)}";
        }
        catch
        {
            return null;
        }
    }
}
