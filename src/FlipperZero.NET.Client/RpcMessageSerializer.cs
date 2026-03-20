using System.Buffers;
using System.Text;

namespace FlipperZero.NET;

/// <summary>
/// Serialises outbound RPC commands to NDJSON strings.
///
/// Produces: <c>{"id":N,"cmd":"name",...args...}</c>
///
/// Uses <see cref="ArrayBufferWriter{T}"/> instead of <see cref="System.IO.MemoryStream"/>
/// to avoid an intermediate <c>byte[]</c> allocation on every call.
/// </summary>
internal static class RpcMessageSerializer
{
    /// <summary>
    /// Serialises an RPC command to a compact JSON string (no trailing newline).
    /// </summary>
    /// <param name="id">The monotonically-increasing request id.</param>
    /// <param name="cmdName">The wire command name (e.g. <c>"ping"</c>).</param>
    /// <param name="writeArgs">
    /// Delegate that writes the command's argument fields into the open JSON
    /// object.  Called after <c>"id"</c> and <c>"cmd"</c> are written.
    /// </param>
    /// <returns>The fully-serialised JSON line, without a trailing newline.</returns>
    public static string Serialize(uint id, string cmdName, Action<Utf8JsonWriter> writeArgs)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteNumber("id", id);
        writer.WriteString("cmd", cmdName);
        writeArgs(writer);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
