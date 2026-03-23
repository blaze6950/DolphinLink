using FlipperZero.NET;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.Web.Pages;

/// <summary>
/// Writes every RPC log entry to the browser console via <see cref="Console.WriteLine"/>
/// (which maps to <c>console.log</c> in Blazor WASM).  Used for debugging.
/// </summary>
internal sealed class BrowserDiagnostics : IRpcDiagnostics
{
    public void Log(RpcLogEntry entry)
    {
        Console.WriteLine(entry.RawJson);
    }
}