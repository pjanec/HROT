using Fdp.Kernel;

namespace Bagira.IG.Components;

/// <summary>
/// ECS value component caching damage level for IG rendering.
/// </summary>
[ComponentId(GlobalComponentIds.IgHealthState)]
public struct IgHealthState
{
    /// <summary>0 = healthy, 100 = fully destroyed.</summary>
    public float Damage;
}
