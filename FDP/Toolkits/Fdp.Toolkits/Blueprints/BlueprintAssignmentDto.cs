namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Declarative blueprint assignment for scenario persistence.
/// Stored inside <see cref="Hrot.Common.Serializers.InitialBlueprintsIntent"/>.
/// </summary>
public sealed class BlueprintAssignmentDto
{
    /// <summary>The stable Asset GUID of the Instance Blueprint.</summary>
    public required Guid AssetId { get; init; }

    /// <summary>
    /// ⭐⭐ The per-entity <b>resolved param bytes</b> — the payload's param region
    /// <c>[ParamsOffset .. ParamsOffset+ParamsSize)</c>, exactly what
    /// <see cref="BlueprintInstanceService.AttachToEntity"/> produces and the tick reads.
    /// <c>null</c> when the assignment carries only declared defaults (the common case), so a
    /// default assignment stays <c>{ AssetId }</c> only. Serialized as base64 by System.Text.Json.
    ///
    /// <para><b>Why bytes, not a name→value dict.</b> A name→value <c>Overrides</c> dict and the
    /// resolver's byte region are two implementations of one concept — ruling 9. The resolver shape
    /// wins (it already carries defaults, overlay and world-context; the dict carries none). See
    /// <c>EXPLAINER_Where_Parameters_And_State_Live.md</c> §"two supply shapes, one concept". The old
    /// deferred <c>Overrides</c> field (BLUEPRINT-SCENARIO-DESIGN §6, never built) is replaced by this.</para>
    /// </summary>
    public byte[]? Params { get; init; }

    /// <summary>
    /// ⭐ The blueprint's <see cref="BlueprintDefinition.StructureHash"/> at save time. The param bytes
    /// are LAYOUT-VERSIONED, so materialization applies them only when this matches the live
    /// definition's hash; a recompiled blueprint (different layout) falls back to <c>InitDefault</c>
    /// rather than reading stale bytes. <c>null</c> when <see cref="Params"/> is null.
    /// </summary>
    public ulong? ParamsStructureHash { get; init; }
}
