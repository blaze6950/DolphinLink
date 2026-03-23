using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlipperZero.Web.Playground;

/// <summary>
/// Runtime registry populated by parsing the schema JSON files that are embedded
/// directly into the assembly as resources.  Replaces the old codegen-generated
/// <c>PlaygroundRegistry.g.cs</c>.
/// </summary>
public static class SchemaRegistry
{
    // ── Lazy initialisation ───────────────────────────────────────────────────

    private static readonly Lazy<RegistryData> _data =
        new(BuildRegistry, isThreadSafe: false);

    // ── Public surface (mirrors old PlaygroundRegistry) ───────────────────────

    /// <summary>All subsystems, each with their commands and streams.</summary>
    public static IReadOnlyList<SubsystemDescriptor> Subsystems => _data.Value.Subsystems;

    /// <summary>Flat list of all command descriptors.</summary>
    public static IReadOnlyList<CommandDescriptor> AllCommands => _data.Value.AllCommands;

    /// <summary>Flat list of all stream descriptors.</summary>
    public static IReadOnlyList<StreamDescriptor> AllStreams => _data.Value.AllStreams;

    /// <summary>All enum descriptors keyed by name.</summary>
    public static IReadOnlyDictionary<string, EnumDescriptor> Enums => _data.Value.Enums;

    // ── Internal data holder ─────────────────────────────────────────────────

    private sealed record RegistryData(
        IReadOnlyList<SubsystemDescriptor> Subsystems,
        IReadOnlyList<CommandDescriptor>   AllCommands,
        IReadOnlyList<StreamDescriptor>    AllStreams,
        IReadOnlyDictionary<string, EnumDescriptor> Enums);

    // ── Builder ──────────────────────────────────────────────────────────────

    private static RegistryData BuildRegistry()
    {
        var asm = Assembly.GetExecutingAssembly();

        // ── 1. Command-ID registry ──────────────────────────────────────────
        var registryJson = ReadEmbedded(asm, "Schema.command-registry.json");
        var rawRegistry  = JsonSerializer.Deserialize<Dictionary<string, string>>(registryJson)!;
        // name → integer ID
        var commandIdMap = rawRegistry.ToDictionary(kv => kv.Value, kv => int.Parse(kv.Key));

        // ── 2. Enum schemas ─────────────────────────────────────────────────
        var enumMap = new Dictionary<string, EnumDescriptor>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("Schema.enums.", StringComparison.Ordinal)) continue;
            var json   = ReadEmbedded(asm, name);
            var node   = JsonNode.Parse(json)!.AsObject();
            var desc   = ParseEnum(node);
            enumMap[desc.Name] = desc;
        }

        // ── 3. Command schemas ──────────────────────────────────────────────
        var commands = new List<CommandDescriptor>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("Schema.commands.", StringComparison.Ordinal)) continue;
            var json   = ReadEmbedded(asm, name);
            var node   = JsonNode.Parse(json)!.AsObject();
            var cmdName = node["command"]?.GetValue<string>();
            if (cmdName is null) continue;
            if (!commandIdMap.TryGetValue(cmdName, out var cmdId)) continue;
            commands.Add(ParseCommand(node, cmdId, enumMap));
        }

        // ── 4. Stream schemas ───────────────────────────────────────────────
        var streams = new List<StreamDescriptor>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("Schema.streams.", StringComparison.Ordinal)) continue;
            var json        = ReadEmbedded(asm, name);
            var node        = JsonNode.Parse(json)!.AsObject();
            var streamName  = node["stream"]?.GetValue<string>();
            if (streamName is null) continue;
            if (!commandIdMap.TryGetValue(streamName + "_start", out var cmdId)) continue;
            streams.Add(ParseStream(node, cmdId, enumMap));
        }

        // ── 5. Group into subsystems ────────────────────────────────────────
        var subsystemCmds    = commands.GroupBy(c => c.Subsystem)
                                       .ToDictionary(g => g.Key, g => (IReadOnlyList<CommandDescriptor>)g.OrderBy(c => c.Name).ToList());
        var subsystemStreams  = streams.GroupBy(s => s.Subsystem)
                                      .ToDictionary(g => g.Key, g => (IReadOnlyList<StreamDescriptor>)g.OrderBy(s => s.Name).ToList());

        var allSubsystems = new SortedSet<string>(subsystemCmds.Keys.Concat(subsystemStreams.Keys));
        var subsystems    = allSubsystems.Select(sub => new SubsystemDescriptor(
            sub,
            subsystemCmds.TryGetValue(sub, out var c) ? c : [],
            subsystemStreams.TryGetValue(sub, out var s) ? s : []
        )).ToList();

        var allCommands = subsystems.SelectMany(s => s.Commands).ToList();
        var allStreams   = subsystems.SelectMany(s => s.Streams).ToList();

        return new RegistryData(subsystems, allCommands, allStreams, enumMap);
    }

    // ── Parsers ──────────────────────────────────────────────────────────────

    private static EnumDescriptor ParseEnum(JsonObject obj)
    {
        var name     = obj["name"]!.GetValue<string>();
        var wireType = obj["wireType"]!.GetValue<string>();
        var values   = obj["values"]!.AsArray()
            .Select(v =>
            {
                var vo       = v!.AsObject();
                var valName  = vo["name"]!.GetValue<string>();
                var wireVal  = wireType == "int"
                    ? vo["wire"]!.GetValue<int>().ToString()
                    : vo["wire"]!.GetValue<string>();
                return new EnumValueDescriptor(valName, wireVal);
            })
            .ToList();
        return new EnumDescriptor(name, wireType, values);
    }

    private static CommandDescriptor ParseCommand(
        JsonObject obj, int cmdId,
        Dictionary<string, EnumDescriptor> enumMap)
    {
        var name      = obj["command"]!.GetValue<string>();
        var subsystem = obj["subsystem"]?.GetValue<string>() ?? "Core";
        var desc      = obj["description"]?.GetValue<string>() ?? name;
        var resource  = obj["resource"]?.GetValue<string?>();
        var errors    = obj["errors"]?.AsArray()
                            .Select(n => n!.GetValue<string>()).ToList()
                        ?? [];

        var reqFields  = ParseFields(obj["request"]?.AsObject(), enumMap);
        var respFields = ParseFields(obj["response"]?.AsObject(), enumMap);

        return new CommandDescriptor(
            name, MakeDisplayName(name), cmdId, subsystem, desc,
            resource, reqFields, respFields, errors);
    }

    private static StreamDescriptor ParseStream(
        JsonObject obj, int cmdId,
        Dictionary<string, EnumDescriptor> enumMap)
    {
        var name      = obj["stream"]!.GetValue<string>();
        var subsystem = obj["subsystem"]?.GetValue<string>() ?? "Core";
        var desc      = obj["description"]?.GetValue<string>() ?? name;
        var resource  = obj["resource"]?.GetValue<string?>();

        var reqFields   = ParseFields(obj["request"]?.AsObject(), enumMap);
        var eventFields = ParseFields(obj["event"]?.AsObject(), enumMap);

        return new StreamDescriptor(
            name, MakeDisplayName(name), cmdId, subsystem, desc,
            resource, reqFields, eventFields);
    }

    private static IReadOnlyList<FieldDescriptor> ParseFields(
        JsonObject? obj, Dictionary<string, EnumDescriptor> enumMap)
    {
        if (obj is null) return [];
        return obj.Select(kv =>
        {
            var fo       = kv.Value!.AsObject();
            var wire     = fo["wire"]!.GetValue<string>();
            var type     = fo["type"]!.GetValue<string>();
            var fieldDesc = fo["description"]?.GetValue<string>() ?? kv.Key;
            var optional = fo["optional"]?.GetValue<bool>() ?? false;
            var enumRef  = fo["enum"]?.GetValue<string>(); // "$GpioPin" or null

            EnumDescriptor? enumDesc = null;
            if (enumRef is not null)
            {
                var enumName = enumRef.TrimStart('$');
                enumMap.TryGetValue(enumName, out enumDesc);
            }

            return new FieldDescriptor(kv.Key, wire, type, fieldDesc, optional, enumDesc);
        }).ToList();
    }

    // ── Display name helper ──────────────────────────────────────────────────

    /// <summary>
    /// Converts <c>snake_case</c> to a human-readable display name.
    /// Short segments (≤3 chars) are uppercased; longer segments are title-cased.
    /// e.g. <c>"gpio_read"</c> → <c>"GPIO Read"</c>, <c>"ir_tx_raw"</c> → <c>"IR TX Raw"</c>.
    /// </summary>
    public static string MakeDisplayName(string snakeCase)
    {
        var parts = snakeCase.Split('_');
        return string.Join(" ", parts.Select(p =>
            p.Length == 0 ? p
            : p.Length <= 3 ? p.ToUpperInvariant()
            : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    // ── Embedded resource reader ─────────────────────────────────────────────

    private static string ReadEmbedded(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource not found: '{resourceName}'. " +
                $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
