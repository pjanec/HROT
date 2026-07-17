using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Hill-attack blueprint-migration slice 3 curated helper, consumed via blueprint
    /// <c>FunctionCall</c> nodes (see <c>docs/blueprints/AimAndFireSpecific_Slice_Design.md</c>,
    /// architect Q6-B). Resolves a replicated <c>TargetNetworkId</c> to a local <see cref="Entity"/>
    /// via the <c>NetworkEntityMap</c> world-singleton -- the first slice needing the target-resolve
    /// capability. Does not modify or replace the C# oracle (<c>HillAttackTankNodes</c>).
    /// </summary>
    public static class NetworkEntityMapOps
    {
        /// <summary>
        /// Blueprint-callable replica of the C# oracle's target-resolution step
        /// (<c>HillAttackTankNodes.cs</c> ~line 334-438, <c>Action_AimAndFireSpecific</c> step 2):
        /// resolves <paramref name="targetNetworkId"/> via the <c>NetworkEntityMap</c> world-singleton.
        /// Returns <see cref="Entity.Null"/> when the singleton is absent or the id is unresolved.
        /// <para>
        /// <b>P7 trailing-context convention:</b> the signature ends with <c>ISimulationView view</c> --
        /// the blueprint compiler's Stage5 (see <c>Stage5_Schedule.ResolveFunctionCallTrailingContext</c>)
        /// recognizes it and appends it at the FunctionCall's emitted call site automatically (baked
        /// <c>TrailingContext: "View"</c> on the asset's FunctionCall node); only
        /// <paramref name="targetNetworkId"/> is a visible/wireable blueprint data pin.
        /// </para>
        /// <para>
        /// <b>GAP-10 (mirrors <see cref="HillAssault2TankOps.HasTarget"/>):</b> <see cref="ISimulationView"/>
        /// exposes no singleton read API, so this helper downcasts back to the real
        /// <see cref="EntityRepository"/> (which the compiler's P7 "view" argument always is, for the
        /// AiPrimitive dispatch this slice targets) to reach the singleton. Declared parameter type
        /// stays the PREFERRED <see cref="ISimulationView"/> (so P7 still recognizes and hides it), at
        /// the cost of a runtime type-check; a future ISimulationView-only backing gracefully returns
        /// <see cref="Entity.Null"/> rather than throw.
        /// </para>
        /// </summary>
        public static Entity ResolveTarget(long targetNetworkId, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return Entity.Null;

            if (!world.HasSingletonManaged<NetworkEntityMap>())
                return Entity.Null;

            var map = world.GetSingletonManaged<NetworkEntityMap>();
            return (map != null && map.TryGetEntity(targetNetworkId, out var target)) ? target : Entity.Null;
        }
    }
}
