namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Describes one field of a Blueprint state struct, used by the inspector and debugger.
/// </summary>
public sealed record BlueprintFieldDescriptor(
    string Name,
    Type   ClrType,
    int    OffsetBytes,
    int    SizeBytes,
    string CategoryOrEmpty);
