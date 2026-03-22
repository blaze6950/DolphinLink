using System.Text.Json.Serialization;
using FlipperZero.NET.Abstractions;

namespace FlipperZero.NET.Commands.System;

/// <summary>Response to <see cref="RegionInfoCommand"/>.</summary>
public readonly partial struct RegionInfoResponse : IRpcCommandResponse
{
    /// <summary>Region name string, e.g. <c>"EU"</c> or <c>"US"</c>.</summary>
    [JsonPropertyName("rg")]
    public string? Region { get; init; }
}
