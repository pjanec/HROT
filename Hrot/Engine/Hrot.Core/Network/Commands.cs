namespace Hrot.Core.Network;

// ── Egress commands ───────────────────────────────────────────────────────────

/// <summary>
/// Protocol-neutral create-entity command.
///
/// <para>⭐ <c>CE-4073</c> (AQ107 B's tail, R-4092): the position is LOCAL ENU METRES — <see cref="X"/> east, <see cref="Y"/> north,
/// <see cref="Z"/> up, relative to the cluster's one origin — the frame the NED wire and the scenario files already speak. It used to be a
/// geodetic latitude / longitude / altitude converted at the NED boundary (<c>NeutralGeoBoundary</c>, deleted).</para>
/// </summary>
public sealed class CreateEntityCommand
{
    /// <summary>Correlation ID for tracking the create request lifecycle.</summary>
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public long TkbType { get; set; }
    /// <summary>Local ENU east, metres.</summary>
    public double X { get; set; }
    /// <summary>Local ENU north, metres.</summary>
    public double Y { get; set; }
    /// <summary>Local ENU up, metres.</summary>
    public double Z { get; set; }
    public string? PropertiesJson { get; set; }
    public int ForceId { get; set; }
}

/// <summary>Protocol-neutral update-entity-descriptor command.</summary>
public sealed class UpdateEntityDescriptorCommand
{
    public int EntityId { get; set; }
    public string DescriptorJson { get; set; } = string.Empty;
    public long BaseVersion { get; set; }
}

/// <summary>Protocol-neutral mission-control command (wrapper for a mission plan or imperative).</summary>
public sealed class MissionControlCommand
{
    public int EntityId { get; set; }
    public Hrot.Core.Mission.eMissionCommandType CommandType { get; set; }
    public Hrot.Core.Mission.MissionPlan? Plan { get; set; }
    public Guid TaskId { get; set; }
    public long BaseVersion { get; set; }
}

/// <summary>Result returned from a mission-control commit or control command.</summary>
public sealed class MissionCommitResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long NewVersion { get; init; }
    public int ErrorCode { get; init; }
}

/// <summary>Protocol-neutral map config DTO.</summary>
public sealed class MapConfigDto
{
    /// <summary>Interaction context ID embedded in the publication (active tool state).</summary>
    public Guid ActiveContextId { get; set; } = Guid.Empty;
    public string ConfigJson { get; set; } = string.Empty;
}

/// <summary>Protocol-neutral map command DTO.</summary>
public sealed class MapCommandDto
{
    /// <summary>Correlation ID for tracking the command lifecycle.</summary>
    public Guid RequestId { get; set; } = Guid.NewGuid();
    /// <summary>Target IG instance MapId (0 = broadcast).</summary>
    public int TargetMapId { get; set; }
    /// <summary>Command type discriminator string, e.g. "CMD_PLACE_ENTITY".</summary>
    public string CommandType { get; set; } = string.Empty;
    /// <summary>JSON-encoded command arguments.</summary>
    public string CommandArgsJson { get; set; } = string.Empty;
}

// ── Neutral entity property patch ────────────────────────────────────────────

/// <summary>
/// Neutral DTO for optional entity property overrides used when creating or placing
/// an entity from the ExCon UI.
/// </summary>
public sealed class EntityPropertyPatch
{
    /// <summary>Human-readable entity name override. Null = use TKB default.</summary>
    public string? Name { get; set; }
    /// <summary>Force affiliation override as a string (e.g. "FORCE_FRIENDLY"). Null = use TKB default.</summary>
    public string? Affiliation { get; set; }
    /// <summary>Local ENU east override, metres (<c>CE-4073</c>; was a geodetic latitude). Null = use click position.</summary>
    public double? X { get; set; }
    /// <summary>Local ENU north override, metres (<c>CE-4073</c>; was a geodetic longitude). Null = use click position.</summary>
    public double? Y { get; set; }
    /// <summary>Local ENU up override, metres (<c>CE-4073</c>; was a geodetic altitude). Null = terrain-clamped.</summary>
    public double? Z { get; set; }
    /// <summary>Height above ground override in metres.</summary>
    public double? HeightAboveGround { get; set; }

    // ── Auto-name generation ──────────────────────────────────────────────────

    /// <summary>
    /// When true, the IG automatically generates a unique sequential name
    /// for each entity created during the placement session.
    /// </summary>
    public bool? AutogenerateName { get; set; }

    /// <summary>
    /// Prefix used when <see cref="AutogenerateName"/> is true.
    /// When null or empty, the IG falls back to the TKB template name followed by a hyphen.
    /// </summary>
    public string? NamePrefix { get; set; }
}

// ── Neutral ingress event DTOs ────────────────────────────────────────────────

/// <summary>
/// Neutral DTO for a map click event from the IG. ⭐ <c>CE-4073</c>: the click position is LOCAL ENU METRES (<see cref="X"/> east,
/// <see cref="Y"/> north, <see cref="Z"/> up) — a copy of the wire's <c>MapClickEvent.Position</c>, no conversion either side.
/// </summary>
public sealed class MapClickEventDto
{
    public Guid InteractionContextId { get; set; }
    /// <summary>Local ENU east, metres.</summary>
    public double X { get; set; }
    /// <summary>Local ENU north, metres.</summary>
    public double Y { get; set; }
    /// <summary>Local ENU up, metres.</summary>
    public double Z { get; set; }
    /// <summary>Ordered list of entity IDs under the cursor (top to bottom).</summary>
    public System.Collections.Generic.IReadOnlyList<int> HitEntityIds { get; set; }
        = System.Array.Empty<int>();
}

/// <summary>Neutral DTO for a selection-changed event from the IG.</summary>
public sealed class SelectionChangedEventDto
{
    public int MapId { get; set; }
    public System.Collections.Generic.IReadOnlyList<int> SelectedEntityIds { get; set; }
        = System.Array.Empty<int>();
}

/// <summary>Neutral DTO for an entity lifecycle ACK (create / delete).</summary>
public sealed class EntityLifecycleAckDto
{
    public Guid RequestId { get; set; }
    public int EntityId { get; set; }
    /// <summary>0 = success, 1 = in-progress, >=2 = error (matches NED status codes).</summary>
    public int StatusCode { get; set; }

    // Status code constants matching NED protocol values.
    public const int StatusSuccess    = 0;
    public const int StatusInProgress = 1;
    public const int StatusFailureMin = 2;
}

/// <summary>Neutral DTO for a map command ACK from the IG.</summary>
public sealed class MapCommandAckDto
{
    public Guid RequestId { get; set; }
    /// <summary>0 = finished, 1 = in-progress, 2 = cancelled.</summary>
    public int StatusCode { get; set; }
    public string? DataJson { get; set; }
}

/// <summary>Neutral DTO for a context-action invoked event from the IG.</summary>
public sealed class ContextActionInvokedDto
{
    public int MapId { get; set; }
    public int ActionId { get; set; }
    public int EntityId { get; set; }
}

// ── Neutral DER descriptor types ─────────────────────────────────────────────

/// <summary>Neutral descriptor pushed into the DER repo for entity identity data.</summary>
public sealed class EntityInfoDescriptor
{
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Affiliation { get; set; } = string.Empty;
    public int CommanderId { get; set; }
}

/// <summary>Neutral descriptor pushed into the DER repo for entity mission state.</summary>
public sealed class EntityMissionDescriptor
{
    public int EntityId { get; set; }
    public Hrot.Core.Mission.MissionPlan? Plan { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// Neutral marker descriptor pushed into the DER repo to indicate an entity
/// has an associated map visual overlay (polygon/polyline). Used by ExCon to
/// decide whether to show the "Edit Polyline" context menu item.
/// </summary>
public sealed class MapOverlayDescriptor
{
    public int EntityId { get; set; }
    /// <summary>True when the overlay can be dragged and re-shaped by the operator.</summary>
    public bool IsEditable { get; set; }
}
