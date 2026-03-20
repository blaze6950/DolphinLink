using System.Text.Json;

namespace FlipperZero.NET.Client.UnitTests;

/// <summary>
/// Unit tests for <see cref="RpcMessageSerializer"/>.
/// No transport or hardware required.
/// </summary>
public sealed class SerializerTests
{
    [Fact]
    public void Serialize_ProducesCorrectIdAndCmd()
    {
        var json = RpcMessageSerializer.Serialize(42, "ping", _ => { });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(42u, root.GetProperty("id").GetUInt32());
        Assert.Equal("ping", root.GetProperty("cmd").GetString());
    }

    [Fact]
    public void Serialize_NoArgs_ProducesExactlyTwoFields()
    {
        var json = RpcMessageSerializer.Serialize(1, "daemon_info", _ => { });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Only "id" and "cmd" — no extra fields
        var properties = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "id", "cmd" }, properties);
    }

    [Fact]
    public void Serialize_WithArgs_InjectsArgFields()
    {
        var json = RpcMessageSerializer.Serialize(7, "test_cmd", writer =>
        {
            writer.WriteNumber("channel", 3);
            writer.WriteString("mode", "rx");
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(7u, root.GetProperty("id").GetUInt32());
        Assert.Equal("test_cmd", root.GetProperty("cmd").GetString());
        Assert.Equal(3, root.GetProperty("channel").GetInt32());
        Assert.Equal("rx", root.GetProperty("mode").GetString());
    }

    [Fact]
    public void Serialize_IdZero_IsValid()
    {
        var json = RpcMessageSerializer.Serialize(0, "ping", _ => { });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0u, doc.RootElement.GetProperty("id").GetUInt32());
    }

    [Fact]
    public void Serialize_MaxId_IsValid()
    {
        var json = RpcMessageSerializer.Serialize(uint.MaxValue, "ping", _ => { });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(uint.MaxValue, doc.RootElement.GetProperty("id").GetUInt32());
    }

    [Fact]
    public void Serialize_OutputIsValidJson()
    {
        var json = RpcMessageSerializer.Serialize(1, "ping", _ => { });

        // Must not throw
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Serialize_DoesNotAppendNewline()
    {
        var json = RpcMessageSerializer.Serialize(1, "ping", _ => { });

        Assert.False(json.EndsWith('\n'));
        Assert.False(json.EndsWith('\r'));
    }
}
