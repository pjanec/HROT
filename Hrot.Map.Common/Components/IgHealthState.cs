using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.IG.Components;

/// <summary>
/// ECS value component caching damage level for IG rendering.
/// </summary>
[ComponentId(HrotComponentIds.IgHealthState)]
public struct IgHealthState
{
    /// <summary>0 = healthy, 100 = fully destroyed.</summary>
    public float Damage;
}
