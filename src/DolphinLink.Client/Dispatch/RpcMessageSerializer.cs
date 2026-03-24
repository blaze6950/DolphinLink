using System.Buffers;
using System.Text;

namespace DolphinLink.Client.Dispatch;

/// <summary>
/// Serialises outbound RPC commands to NDJSON strings.
///
/// Produces: <c>{"c":ID,"i":N,...args...}</c>  (V1 wire format)
///
/// Uses <see cref="ArrayBufferWriter{T}"/> instead of <see cref="System.IO.MemoryStream"/>
/// to avoid an intermediate <c>byte[]</c> allocation on every call.
/// </summary>
internal static class RpcMessageSerializer
{
    /// <summary>
    /// Serialises an RPC command to a compact JSON string (no trailing newline).
    /// </summary>
    /// <param name="id">The monotonically-increasing request id (wire key <c>"i"</c>).</param>
    /// <param name="commandId">The numeric command id from the registry (wire key <c>"c"</c>).</param>
    /// <param name="writeArgs">
    /// Delegate that writes the command's argument fields into the open JSON
    /// object.  Called after <c>"c"</c> and <c>"i"</c> are written.
    /// </param>
    /// <returns>The fully-serialised JSON line, without a trailing newline.</returns>
    public static string Serialize(uint id, int commandId, Action<Utf8JsonWriter> writeArgs)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteNumber("c", commandId);
        writer.WriteNumber("i", id);
        writeArgs(writer);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
