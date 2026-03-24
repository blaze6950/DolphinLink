namespace DolphinLink.Web.Playground;

/// <summary>Describes a single enum value as seen on the wire.</summary>
public sealed record EnumValueDescriptor(string Name, string WireValue);

/// <summary>Describes a complete enum type defined in the schema.</summary>
public sealed record EnumDescriptor(string Name, string WireType, IReadOnlyList<EnumValueDescriptor> Values);

/// <summary>Describes a single field (request or response) of a command or stream.</summary>
public sealed record FieldDescriptor(
    string Name,
    string WireKey,
    string Type,
    string Description,
    bool Optional,
    EnumDescriptor? Enum);

/// <summary>Describes a request/response command from the schema.</summary>
public sealed record CommandDescriptor(
    string Name,
    string DisplayName,
    int CommandId,
    string Subsystem,
    string Description,
    string? Resource,
    IReadOnlyList<FieldDescriptor> RequestFields,
    IReadOnlyList<FieldDescriptor> ResponseFields,
    IReadOnlyList<string> Errors);

/// <summary>Describes a streaming command from the schema.</summary>
public sealed record StreamDescriptor(
    string Name,
    string DisplayName,
    int CommandId,
    string Subsystem,
    string Description,
    string? Resource,
    IReadOnlyList<FieldDescriptor> RequestFields,
    IReadOnlyList<FieldDescriptor> EventFields);

/// <summary>Groups commands and streams by subsystem.</summary>
public sealed record SubsystemDescriptor(
    string Name,
    IReadOnlyList<CommandDescriptor> Commands,
    IReadOnlyList<StreamDescriptor> Streams);
