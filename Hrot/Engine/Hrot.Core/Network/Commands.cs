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






// ── Neutral DER descriptor types ─────────────────────────────────────────────



