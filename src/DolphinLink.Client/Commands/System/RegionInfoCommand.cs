using System.Text.Json.Serialization;
using DolphinLink.Client.Abstractions;

namespace DolphinLink.Client.Commands.System;

/// <summary>Response to <see cref="RegionInfoCommand"/>.</summary>
public readonly partial struct RegionInfoResponse : IRpcCommandResponse
{
    /// <summary>Region name string, e.g. <c>"EU"</c> or <c>"US"</c>.</summary>
    [JsonPropertyName("rg")]
    public string? Region { get; init; }
}
