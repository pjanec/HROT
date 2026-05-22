using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a single pin on a node.</summary>
public interface IPinModel
{
    /// <summary>Stable id.</summary>
    PinId Id { get; }

    /// <summary>Id of the owning node.</summary>
    NodeId OwnerNodeId { get; }

    /// <summary>Display label of the pin.</summary>
    string Label { get; }

    /// <summary>Input vs output side.</summary>
    PinDirection Direction { get; }

    /// <summary>Execution-control vs typed-data.</summary>
    PinKind Kind { get; }

    /// <summary>Type key; null for Exec pins.</summary>
    TypeKey? Type { get; }

    /// <summary>Visual shape (Circle for single data, Diamond for array, …).</summary>
    PinShape Shape { get; }

    /// <summary>True if the pin is "advanced" (hidden behind disclosure by default).</summary>
    bool IsAdvanced { get; }

    /// <summary>True if the pin is optional (rendered with subdued styling).</summary>
    bool IsOptional { get; }

    /// <summary>Tooltip when hovering this pin.</summary>
    string? Tooltip { get; }

    /// <summary>
    /// Default value for input data pins when no wire is connected;
    /// null for exec pins, output pins, or input pins with no editor.
    /// </summary>
    IPinDefaultValue? Default { get; }

    /// <summary>
    /// True if this pin accepts multiple simultaneous connections.
    /// Computed; not stored.
    /// </summary>
    bool AcceptsMultipleConnections =>
        (Direction == PinDirection.Output && Kind == PinKind.Data) ||
        (Direction == PinDirection.Input && Kind == PinKind.Exec);
}

/// <summary>
/// Opaque container for a pin's default value. The actual value is
/// retrieved via the registered <c>IPinDefaultValueEditor</c> for the
/// pin's type.
/// </summary>
public interface IPinDefaultValue
{
    /// <summary>The current value (boxed). Type matches the pin's TypeKey.</summary>
    object? Value { get; }

    /// <summary>Metadata controlling editor presentation (range, units, …).</summary>
    PinDefaultMetadata Metadata { get; }
}

/// <summary>Metadata controlling how a default-value editor presents itself.</summary>
public sealed record PinDefaultMetadata(
    double? RangeMin,
    double? RangeMax,
    double? Step,
    string? Units,
    string? PickerSourceKey,
    string? PlaceholderText,
    bool ClampToRange);
