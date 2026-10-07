using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐ <c>AQ85</c> A (R-216) — the shooter's count of fired rounds: <c>k</c> in the deflection sequence <c>d(k)</c>, so the same
    /// scenario gives the same hits every run. Muscle-local (the node that spawns the bullet), never saved.
    /// </summary>
    [ComponentId(GlobalComponentIds.ShotOrdinal)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ShotOrdinal
    {
        public uint Count;
    }

    /// <summary>
    /// ⭐ <c>AQ85</c> E (R-216) — when the unit was last hit or nearly missed (sim seconds). For <see cref="HitModel.UnderFireSeconds"/>
    /// after it, its own aim is spoiled (σ × 2): suppression. Stamped on the Muscle by the near-miss and hit producers.
    /// </summary>
    [ComponentId(GlobalComponentIds.UnderFire)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct UnderFire
    {
        public double LastTime;
    }
}
