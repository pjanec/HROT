using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// P1 (GAP-1) -- the reflection-free curated accessor surface for <see cref="UnitRoster"/> that
    /// <c>FlowForEachNode</c>'s <c>GetUnitRoster</c>-style source lowers to (baked
    /// <c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c> strings on the node, see
    /// <c>Hrot.Blueprints.Core.Assets.FlowForEachNode</c>). <see cref="UnitRoster"/> carries a
    /// <c>fixed long SubordinateEntities[16]</c> buffer that requires <c>unsafe</c> access -- the
    /// architect ruling (Q#5-C) keeps that raw fixed-array access OUT of the visual graph entirely,
    /// confined to this tiny curated helper instead (mirrors <c>HillAssault2NavOps</c>'s "keep the
    /// unsafe/reflection-adjacent bit off-graph" shape).
    /// </summary>
    public static class UnitRosterOps
    {
        /// <summary>Number of currently registered subordinates in <paramref name="r"/> (0-16).</summary>
        public static int Count(in UnitRoster r) => r.Count;

        /// <summary>
        /// The <paramref name="i"/>-th subordinate's entity handle, unpacked from the roster's
        /// packed <c>long</c> storage via <see cref="Entity(ulong)"/>. Caller (the emitted
        /// <c>IrOp_ForEach</c> loop) guarantees <c>0 &lt;= i &lt; Count(in r)</c>.
        /// </summary>
        public static Entity Subordinate(in UnitRoster r, int i)
        {
            unsafe { return new Entity((ulong)r.SubordinateEntities[i]); }
        }
    }
}
