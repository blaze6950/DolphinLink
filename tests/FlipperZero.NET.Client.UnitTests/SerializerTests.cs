using FlipperZero.NET.Dispatch;

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
        var json = RpcMessageSerializer.Serialize(42, 0, _ => { });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(42u, root.GetProperty("i").GetUInt32());
        Assert.Equal(0, root.GetProperty("c").GetInt32());
    }

    [Fact]
    public void Serialize_NoArgs_ProducesExactlyTwoFields()
    {
        var json = RpcMessageSerializer.Serialize(1, 3, _ => { });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Only "i" and "c" — no extra fields
        var properties = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "c", "i" }, properties);
    }

    [Fact]
    public void Serialize_WithArgs_InjectsArgFields()
    {
        var json = RpcMessageSerializer.Serialize(7, 5, writer =>
        {
            writer.WriteNumber("channel", 3);
            writer.WriteString("mode", "rx");
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(7u, root.GetProperty("i").GetUInt32());
        Assert.Equal(5, root.GetProperty("c").GetInt32());
        Assert.Equal(3, root.GetProperty("channel").GetInt32());
        Assert.Equal("rx", root.GetProperty("mode").GetString());
    }

    [Fact]
    public void Serialize_IdZero_IsValid()
    {
        var json = RpcMessageSerializer.Serialize(0, 0, _ => { });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0u, doc.RootElement.GetProperty("i").GetUInt32());
    }

    [Fact]
    public void Serialize_MaxId_IsValid()
    {
        var json = RpcMessageSerializer.Serialize(uint.MaxValue, 0, _ => { });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(uint.MaxValue, doc.RootElement.GetProperty("i").GetUInt32());
    }

    [Fact]
    public void Serialize_OutputIsValidJson()
    {
        var json = RpcMessageSerializer.Serialize(1, 0, _ => { });

        // Must not throw
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Serialize_DoesNotAppendNewline()
    {
        var json = RpcMessageSerializer.Serialize(1, 0, _ => { });

        Assert.False(json.EndsWith('\n'));
        Assert.False(json.EndsWith('\r'));
    }
}
