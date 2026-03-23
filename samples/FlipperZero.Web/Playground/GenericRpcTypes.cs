using System.Text.Json;
using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.Web.Playground;

/// <summary>
/// A generic command struct that carries pre-parsed JSON args.
/// Allows the Playground to send any command without needing its specific typed struct.
/// </summary>
public readonly struct GenericRpcCommand : IRpcCommand<GenericRpcResponse>
{
    private readonly JsonElement _args;

    public GenericRpcCommand(int commandId, string commandName, JsonElement args)
    {
        CommandId   = commandId;
        CommandName = commandName;
        _args       = args;
    }

    /// <inheritdoc />
    public string CommandName { get; }

    /// <inheritdoc />
    public int CommandId { get; }

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        // Write each property from the pre-built args JSON object into the wire envelope.
        foreach (var prop in _args.EnumerateObject())
        {
            prop.WriteTo(writer);
        }
    }
}

/// <summary>
/// Generic response deserialized from the "p" payload sub-object.
/// All fields are captured via <see cref="Fields"/> so the Playground
/// can display any command response without compile-time knowledge of its shape.
/// </summary>
public struct GenericRpcResponse : IRpcCommandResponse
{
    /// <summary>
    /// Captures every key/value pair in the "p" payload.
    /// System.Text.Json populates this automatically for any property
    /// not otherwise declared on the struct.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Fields { get; set; }

    /// <summary>
    /// Returns the payload as a <see cref="JsonElement"/> built from <see cref="Fields"/>,
    /// or a null element if the payload was empty.
    /// </summary>
    public readonly JsonElement? ToJsonElement()
    {
        if (Fields is null || Fields.Count == 0)
            return null;

        using var ms = new System.IO.MemoryStream();
        using var w  = new Utf8JsonWriter(ms);
        w.WriteStartObject();
        foreach (var kv in Fields)
        {
            w.WritePropertyName(kv.Key);
            kv.Value.WriteTo(w);
        }
        w.WriteEndObject();
        w.Flush();

        // Clone so the MemoryStream can be disposed safely.
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Returns a pretty-printed raw JSON string of the payload for display.
    /// </summary>
    public readonly string ToRawJson()
    {
        if (Fields is null || Fields.Count == 0)
            return "{}";

        using var ms = new System.IO.MemoryStream();
        var opts = new JsonWriterOptions { Indented = true };
        using var w  = new Utf8JsonWriter(ms, opts);
        w.WriteStartObject();
        foreach (var kv in Fields)
        {
            w.WritePropertyName(kv.Key);
            kv.Value.WriteTo(w);
        }
        w.WriteEndObject();
        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>
/// A generic stream-start command struct for the Playground.
/// Works the same way as <see cref="GenericRpcCommand"/> but implements
/// <see cref="IRpcStreamCommand{TEvent}"/> so it can be used with
/// <c>FlipperRpcClient.SendStreamAsync</c>.
/// </summary>
public readonly struct GenericStreamStartCommand : IRpcStreamCommand<GenericStreamEvent>
{
    private readonly JsonElement _args;

    public GenericStreamStartCommand(int commandId, string commandName, JsonElement args)
    {
        CommandId   = commandId;
        CommandName = commandName;
        _args       = args;
    }

    /// <inheritdoc />
    public string CommandName { get; }

    /// <inheritdoc />
    public int CommandId { get; }

    /// <inheritdoc />
    public void WriteArgs(Utf8JsonWriter writer)
    {
        foreach (var prop in _args.EnumerateObject())
        {
            prop.WriteTo(writer);
        }
    }
}

/// <summary>
/// Generic stream event deserialized from the "p" payload sub-object of each stream message.
/// All event field values are captured in <see cref="Fields"/> via <see cref="JsonExtensionDataAttribute"/>.
/// </summary>
public struct GenericStreamEvent : IRpcCommandResponse
{
    /// <summary>
    /// Captures every key/value pair in the "p" event payload.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Fields { get; set; }
}
