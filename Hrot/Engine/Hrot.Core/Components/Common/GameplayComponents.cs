using Fdp.Core;

namespace Hrot.Common;

/// <summary>
/// ⭐ Player Join S2-a (D-10, <c>docs/DESIGN_Player_Join.md</c> §10) — a ROLE a Player may deploy into: the side that may take it, the person TKB type its man is made from, the name it shows and how many may
/// be alive at once. One authored entity per role (TKB "Deploy Role"): an unmanaged component cannot hold a list, and the editor already places, edits and saves entities. Recorded and saved with the scenario.
/// </summary>
[ComponentId(GlobalComponentIds.DeployRole)]
[DataPolicy(DataPolicy.Default)]
public struct DeployRole
{
    public ForceId Side;

    /// <summary>The TKB type of the person this role spawns (an infantry type).</summary>
    public long PersonTkbType;

    /// <summary>The role's name; the men are named "&lt;name&gt; &lt;n&gt;".</summary>
    public FixedString32 Name;

    /// <summary>How many men of this role may be alive at once; 0 = unlimited.</summary>
    public byte MaxCount;

    // ⭐ Player Join S3-a (D-10, kits) — up to THREE extra kits beside the role's own (kit 0 = PersonTkbType / Name): another person type the SAME role may spawn, sharing its count and max (S3-b).
    //   Fixed fields, not a list: an unmanaged component cannot hold one. Tkb 0 = the kit is not offered; Name "" = "Kit n".
    public long Kit1Tkb;
    public long Kit2Tkb;
    public long Kit3Tkb;
    public FixedString32 Kit1Name;
    public FixedString32 Kit2Name;
    public FixedString32 Kit3Name;

    /// <summary>The person type of kit <paramref name="kit"/> (0 = the role's own); 0 when the kit is unset or out of range.</summary>
    public readonly long KitTkb(int kit) => kit switch { 0 => PersonTkbType, 1 => Kit1Tkb, 2 => Kit2Tkb, 3 => Kit3Tkb, _ => 0L };
}

/// <summary>⭐ Player Join S2-b (D-11) — marks an AREA entity (TKB "Spawn Area", outline as any area) a role's man may spawn inside, for <see cref="Side"/>, facing <see cref="FacingDeg"/> (compass-free yaw, degrees about Z, 0 = +X).</summary>
[ComponentId(GlobalComponentIds.SpawnArea)]
[DataPolicy(DataPolicy.Default)]
public struct SpawnArea
{
    public ForceId Side;
    public float FacingDeg;
}

/// <summary>⭐ Player Join S2-d (D-14) — the scenario's gameplay settings, on the World Environment entity. Absent ⇒ the server uses <see cref="DefaultRespawnDelaySeconds"/>. 0 ⇒ a dead Player observes for good.</summary>
[ComponentId(GlobalComponentIds.GameplaySettings)]
[DataPolicy(DataPolicy.Default)]
public struct GameplaySettings
{
    public const float DefaultRespawnDelaySeconds = 10f;

    public float RespawnDelaySeconds;
}
