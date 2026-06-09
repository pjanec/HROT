namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Declarative blueprint assignment for scenario persistence.
/// Stored inside <see cref="Hrot.Common.Serializers.InitialBlueprintsIntent"/>.
/// </summary>
public sealed class BlueprintAssignmentDto
{
    /// <summary>The stable Asset GUID of the Instance Blueprint.</summary>
    public required Guid AssetId { get; init; }

    /// <summary>Per-variable overrides. Null/empty in MVP (see Design §6).</summary>
    public Dictionary<string, object>? Overrides { get; init; }
}
