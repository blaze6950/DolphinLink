#!/usr/bin/env dotnet-script
// normalizer-codegen.csx — generates RpcJsonNormalizer.g.cs from schema/
// Emits ONLY the private lookup-table switch methods.
// The Normalize() / NormalizeCore() logic lives in the hand-written RpcJsonNormalizer.cs.
// Run from repo root: dotnet script codegens/normalizer-codegen.csx

#r "nuget: System.Text.Json, 8.0.0"
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Linq;

// ─── Paths ───────────────────────────────────────────────────────────────────
static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(GetScriptPath()));
var schemaDir = Path.Combine(repoRoot, "schema");
var outFile   = Path.Combine(repoRoot, "src", "FlipperZero.NET.Client", "Generated", "RpcJsonNormalizer.g.cs");

// ─── Load registry ────────────────────────────────────────────────────────────
var registryRaw = JsonSerializer.Deserialize<Dictionary<string, string>>(
    File.ReadAllText(Path.Combine(schemaDir, "command-registry.json")))!;

// Ordered list: index = command ID, value = registry name (e.g. "gpio_watch_start")
var registry = registryRaw
    .OrderBy(kv => int.Parse(kv.Key))
    .Select(kv => kv.Value)
    .ToArray();

// ─── Load enum schemas ────────────────────────────────────────────────────────
var enumDir = Path.Combine(schemaDir, "enums");

// intEnumValues["GpioPin"] = [(wire:1,"Pin1"), (wire:2,"Pin2"), ...]  — int-wire enums only
var intEnumValues = new Dictionary<string, List<(int wire, string name)>>();
var enumWireTypes = new Dictionary<string, string>();

foreach (var f in Directory.GetFiles(enumDir, "*.json"))
{
    var o = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
    var eName = o["name"]!.GetValue<string>();
    var wt    = o["wireType"]!.GetValue<string>();
    enumWireTypes[eName] = wt;
    if (wt == "int")
    {
        var vals = o["values"]!.AsArray()
            .Select(v => (wire: v!["wire"]!.GetValue<int>(), name: v!["name"]!.GetValue<string>()))
            .ToList();
        intEnumValues[eName] = vals;
    }
}

bool IsIntEnum(string enumRef) =>
    enumRef != null &&
    enumWireTypes.TryGetValue(enumRef.TrimStart('$'), out var wt) &&
    wt == "int";

// ─── Load command schemas ─────────────────────────────────────────────────────
record FieldInfo(string Wire, string Human, string Type, bool Optional, string EnumRef);

var cmdSchemaDir    = Path.Combine(schemaDir, "commands");
var streamSchemaDir = Path.Combine(schemaDir, "streams");

static List<FieldInfo> ParseFields(JsonObject fields)
{
    if (fields == null) return new();
    return fields.Select(kv => {
        var fo      = kv.Value!.AsObject();
        var wire    = fo["wire"]!.GetValue<string>();
        var type    = fo["type"]!.GetValue<string>();
        var opt     = fo["optional"]?.GetValue<bool>() ?? false;
        var enumRef = fo["enum"]?.GetValue<string>();
        return new FieldInfo(wire, kv.Key, type, opt, enumRef);
    }).ToList();
}

// Map: registry name → (requestFields, responseFields)
var requestFieldsByCmd  = new Dictionary<string, List<FieldInfo>>();
var responseFieldsByCmd = new Dictionary<string, List<FieldInfo>>();

foreach (var f in Directory.GetFiles(cmdSchemaDir, "*.json", SearchOption.AllDirectories))
{
    var o       = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
    var cmdName = o["command"]!.GetValue<string>();
    requestFieldsByCmd[cmdName]  = ParseFields(o["request"]?.AsObject());
    responseFieldsByCmd[cmdName] = ParseFields(o["response"]?.AsObject());
}

foreach (var f in Directory.GetFiles(streamSchemaDir, "*.json"))
{
    var o          = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
    var streamName = o["stream"]!.GetValue<string>();
    var startName  = streamName + "_start";
    requestFieldsByCmd[startName]  = ParseFields(o["request"]?.AsObject());
    responseFieldsByCmd[startName] = ParseFields(o["event"]?.AsObject());
}

// ─── Build lookup tables ──────────────────────────────────────────────────────
var reqKeyMap  = new List<(string cmd, string wire, string human)>();
var respKeyMap = new List<(string cmd, string wire, string human)>();
var reqBoolSet  = new HashSet<(string, string)>();
var respBoolSet = new HashSet<(string, string)>();
var reqEnumMap  = new List<(string cmd, string wire, int val, string label)>();
var respEnumMap = new List<(string cmd, string wire, int val, string label)>();

foreach (var (cmd, fields) in requestFieldsByCmd)
{
    foreach (var fi in fields)
    {
        reqKeyMap.Add((cmd, fi.Wire, fi.Human));
        if (fi.Type == "bool") reqBoolSet.Add((cmd, fi.Wire));
        if (fi.EnumRef != null && IsIntEnum(fi.EnumRef))
        {
            var eName = fi.EnumRef.TrimStart('$');
            foreach (var (wv, label) in intEnumValues[eName])
                reqEnumMap.Add((cmd, fi.Wire, wv, label));
        }
    }
}

foreach (var (cmd, fields) in responseFieldsByCmd)
{
    foreach (var fi in fields)
    {
        respKeyMap.Add((cmd, fi.Wire, fi.Human));
        if (fi.Type == "bool") respBoolSet.Add((cmd, fi.Wire));
        if (fi.EnumRef != null && IsIntEnum(fi.EnumRef))
        {
            var eName = fi.EnumRef.TrimStart('$');
            foreach (var (wv, label) in intEnumValues[eName])
                respEnumMap.Add((cmd, fi.Wire, wv, label));
        }
    }
}

// ─── Emit file ────────────────────────────────────────────────────────────────
var sb = new System.Text.StringBuilder();

sb.AppendLine("// <auto-generated />");
sb.AppendLine("// Generated by codegens/normalizer-codegen.csx -- do not edit by hand.");
sb.AppendLine("#nullable enable");
sb.AppendLine();
sb.AppendLine("namespace FlipperZero.NET;");
sb.AppendLine();
sb.AppendLine("public static partial class RpcJsonNormalizer");
sb.AppendLine("{");

// ── CommandIdToName ───────────────────────────────────────────────────────────
sb.AppendLine("    private static string? CommandIdToName(int id) => id switch");
sb.AppendLine("    {");
for (int i = 0; i < registry.Length; i++)
    sb.AppendLine($"        {i} => \"{registry[i]}\",");
sb.AppendLine("        _ => null,");
sb.AppendLine("    };");
sb.AppendLine();

// ── ExpandRequestKey ──────────────────────────────────────────────────────────
sb.AppendLine("    private static string? ExpandRequestKey(string? command, string wireKey) => (command, wireKey) switch");
sb.AppendLine("    {");
foreach (var (cmd, wire, human) in reqKeyMap.OrderBy(x => x.cmd).ThenBy(x => x.wire))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\") => \"{human}\",");
sb.AppendLine("        _ => null,");
sb.AppendLine("    };");
sb.AppendLine();

// ── ExpandResponseKey ─────────────────────────────────────────────────────────
sb.AppendLine("    private static string? ExpandResponseKey(string? command, string wireKey) => (command, wireKey) switch");
sb.AppendLine("    {");
sb.AppendLine("        (null, \"s\") => \"stream_id\",");
foreach (var (cmd, wire, human) in respKeyMap.OrderBy(x => x.cmd).ThenBy(x => x.wire))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\") => \"{human}\",");
sb.AppendLine("        _ => null,");
sb.AppendLine("    };");
sb.AppendLine();

// ── IsRequestBoolField ────────────────────────────────────────────────────────
sb.AppendLine("    private static bool IsRequestBoolField(string? command, string wireKey) => (command, wireKey) switch");
sb.AppendLine("    {");
foreach (var (cmd, wire) in reqBoolSet.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\") => true,");
sb.AppendLine("        _ => false,");
sb.AppendLine("    };");
sb.AppendLine();

// ── IsResponseBoolField ───────────────────────────────────────────────────────
sb.AppendLine("    private static bool IsResponseBoolField(string? command, string wireKey) => (command, wireKey) switch");
sb.AppendLine("    {");
foreach (var (cmd, wire) in respBoolSet.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\") => true,");
sb.AppendLine("        _ => false,");
sb.AppendLine("    };");
sb.AppendLine();

// ── ExpandRequestEnum ─────────────────────────────────────────────────────────
sb.AppendLine("    private static string? ExpandRequestEnum(string? command, string wireKey, long value) => (command, wireKey, value) switch");
sb.AppendLine("    {");
foreach (var (cmd, wire, val, label) in reqEnumMap.OrderBy(x => x.cmd).ThenBy(x => x.wire).ThenBy(x => x.val))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\", {val}) => \"{label}\",");
sb.AppendLine("        _ => null,");
sb.AppendLine("    };");
sb.AppendLine();

// ── ExpandResponseEnum ────────────────────────────────────────────────────────
sb.AppendLine("    private static string? ExpandResponseEnum(string? command, string wireKey, long value) => (command, wireKey, value) switch");
sb.AppendLine("    {");
foreach (var (cmd, wire, val, label) in respEnumMap.OrderBy(x => x.cmd).ThenBy(x => x.wire).ThenBy(x => x.val))
    sb.AppendLine($"        (\"{cmd}\", \"{wire}\", {val}) => \"{label}\",");
sb.AppendLine("        _ => null,");
sb.AppendLine("    };");
sb.AppendLine();

sb.AppendLine("}");

File.WriteAllText(outFile, sb.ToString());
Console.WriteLine($"Generated: {outFile}");
