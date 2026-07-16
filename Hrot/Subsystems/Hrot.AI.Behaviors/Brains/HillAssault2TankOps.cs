using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Hill-attack blueprint-migration slice 1 read-only helper(s), consumed via blueprint
    /// <c>FunctionCall</c> nodes. Working name <c>HillAssault2</c> (mirrors slice 0's
    /// <c>HillAssault2_AbortEngagement</c>) -- does not modify or replace the C# oracle
    /// (<see cref="HillAttackTankNodes"/>).
    /// </summary>
    public static class HillAssault2TankOps
    {
        /// <summary>
        /// Blueprint-callable replica of the C# oracle
        /// <see cref="HillAttackTankNodes.Condition_HasTarget"/> (HillAttackTankNodes.cs ~line 113):
        /// resolves <paramref name="targetNetworkId"/> via the <c>NetworkEntityMap</c> world-singleton,
        /// then scans <paramref name="self"/>'s <see cref="TargetMemory"/> for that resolved entity
        /// with a positive threat score.
        /// <para>
        /// <b>P7 trailing-context convention:</b> the signature ends with <c>Entity self</c> and
        /// <c>ISimulationView view</c> -- the blueprint compiler's Stage5 (see
        /// <c>Stage5_Schedule.ResolveTrailingContext</c>) recognizes both and appends them at the
        /// FunctionCall's emitted call site automatically; only <paramref name="targetNetworkId"/>
        /// is a visible/wireable blueprint data pin.
        /// </para>
        /// <para>
        /// <b>FRICTION / GAP (see migration report):</b> <see cref="ISimulationView"/> exposes no
        /// singleton read API (no <c>GetSingletonManaged</c>/<c>HasSingletonManaged</c> -- only
        /// per-entity component reads, event reads, and queries), so it cannot reach the
        /// <c>NetworkEntityMap</c> world-singleton on its own. For AiPrimitive dispatch (the only
        /// dispatch kind this slice targets) the compiler's P7 "view" argument is always the real
        /// <see cref="EntityRepository"/> upcast to <see cref="ISimulationView"/> (see
        /// <c>EmissionContext.ViewVar</c>), so this helper downcasts back to
        /// <see cref="EntityRepository"/> to reach the singleton. This keeps the declared parameter
        /// type as the PREFERRED <see cref="ISimulationView"/> (so P7 still recognizes and hides it),
        /// at the cost of a runtime type-check; a future <see cref="ISimulationView"/>-only backing
        /// (e.g. a snapshot/SoD view with no singleton support) would gracefully fail this condition
        /// (return <c>false</c>) rather than throw.
        /// </para>
        /// </summary>
        public static bool HasTarget(uint targetNetworkId, Entity self, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return false;

            if (!world.HasSingletonManaged<NetworkEntityMap>())
                return false;

            var entityMap = world.GetSingletonManaged<NetworkEntityMap>();
            if (entityMap == null || !entityMap.TryGetEntity((long)targetNetworkId, out var targetEntity))
                return false;

            if (!view.HasComponent<TargetMemory>(self))
                return false;

            long targetPacked = (long)targetEntity.PackedValue;
            ref readonly var mem = ref view.GetComponentRO<TargetMemory>(self);

            unsafe
            {
                for (int i = 0; i < mem.Count; i++)
                {
                    if (mem.EntityIds[i] == targetPacked && mem.ThreatScores[i] > 0f)
                        return true;
                }
            }
            return false;
        }
    }
}
