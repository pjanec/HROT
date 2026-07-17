using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Hill-attack blueprint-migration slice 3 curated helper(s), consumed via blueprint
    /// <c>FunctionCall</c> nodes (see <c>docs/blueprints/AimAndFireSpecific_Slice_Design.md</c>,
    /// architect Q6-B). General-purpose entity/world predicates -- broadly reusable beyond this
    /// slice. Does not modify or replace the C# oracle (<c>HillAttackTankNodes</c>).
    /// </summary>
    public static class WorldOps
    {
        /// <summary>
        /// Blueprint-callable liveness check, mirroring the C# oracle's <c>!IsAlive(target)</c> guard
        /// (<c>HillAttackTankNodes.cs</c> ~line 334-438, <c>Action_AimAndFireSpecific</c> step 3).
        /// <para>
        /// <b>P7 trailing-context convention:</b> the signature ends with <c>ISimulationView view</c> --
        /// recognized and auto-appended by Stage5 (baked <c>TrailingContext: "View"</c>); only
        /// <paramref name="e"/> is a visible/wireable blueprint data pin.
        /// </para>
        /// <para>
        /// <b>GAP-10 (mirrors <see cref="HillAssault2TankOps.HasTarget"/> /
        /// <see cref="NetworkEntityMapOps.ResolveTarget"/>):</b> downcasts <paramref name="view"/> back
        /// to the real <see cref="EntityRepository"/> to reach <c>IsAlive</c> (not exposed on
        /// <see cref="ISimulationView"/>). Gracefully returns <c>false</c> for a non-<see cref="EntityRepository"/>
        /// view rather than throw.
        /// </para>
        /// </summary>
        public static bool IsAlive(Entity e, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return false;

            return world.IsAlive(e);
        }

        /// <summary>
        /// Blueprint-callable resolve-failure test (<see cref="Entity.Null"/> equality). Contextless
        /// (<c>TrailingContext: "None"</c>) -- pure value comparison, no world access needed.
        /// </summary>
        public static bool IsNull(Entity e) => e == Entity.Null;

        /// <summary>
        /// Blueprint-callable current simulation time (seconds), mirroring the oracle's
        /// <c>ctx.World.SimulationTime</c> read used by the EQS 5 s timeout
        /// (<c>HillAttackCommanderNodes.Condition_IsAreaQueryResolved</c>). The graph subtracts a cached
        /// timestamp from this and compares against the timeout via visual <c>BinaryOp</c>/<c>Compare</c>
        /// (architect Q#7-C: no native <c>GetTime</c> node until sim-time proves broadly needed).
        /// <para>
        /// <b>P7 trailing-context:</b> the sole parameter is the auto-appended <c>ISimulationView view</c>
        /// (baked <c>TrailingContext:"View"</c>) — the node has NO visible data-in pin. <b>GAP-10:</b>
        /// <c>SimulationTime</c> lives on the concrete <see cref="EntityRepository"/> (not
        /// <see cref="ISimulationView"/>), so downcast; returns <c>0</c> for a non-repository view.
        /// </para>
        /// </summary>
        public static float SimTime(ISimulationView view)
        {
            if (view is not EntityRepository world)
                return 0f;

            return world.SimulationTime;
        }
    }
}
