using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>
/// Returns the RF region name and the list of allowed frequency bands.
///
/// Wire format (request):
/// <code>{"id":N,"cmd":"region_info"}</code>
///
/// Wire format (response):
/// <code>{"t":0,"i":N,"p":{"region":"EU"}}</code>
/// </summary>
public readonly struct RegionInfoCommand : IRpcCommand<RegionInfoResponse>
{
    /// <inheritdoc />
    public string CommandName => "region_info";

    /// <summary>No arguments.</summary>
    public void WriteArgs(Utf8JsonWriter writer) { }
}

/// <summary>Response to <see cref="RegionInfoCommand"/>.</summary>
public readonly struct RegionInfoResponse : IRpcCommandResponse
{
    /// <summary>Region name string, e.g. <c>"EU"</c> or <c>"US"</c>.</summary>
    [JsonPropertyName("region")]
    public string? Region { get; init; }
}
