using Fdp.Core;

namespace Hrot.Common;

/// <summary>⭐ Player Join S2-b (D-11) — marks an AREA entity (TKB "Spawn Area", outline as any area) a role's man may spawn inside, for <see cref="Side"/>, facing <see cref="FacingDeg"/> (compass-free yaw, degrees about Z, 0 = +X).</summary>
[ComponentId(GlobalComponentIds.SpawnArea)]
[DataPolicy(DataPolicy.Default)]
public struct SpawnArea
{
    public ForceId Side;
    public float FacingDeg;
}
