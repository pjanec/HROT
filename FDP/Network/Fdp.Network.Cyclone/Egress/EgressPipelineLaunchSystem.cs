using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Network.Cyclone.Egress
{
    /// <summary>
    /// ⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7) — LAST in <c>Export</c>: launches the world's off-frame state scan
    /// (<see cref="EgressPipeline.Launch"/>). Registering it is what lets a node's egress go off the frame at all (O-G: the SimHost first);
    /// the join needs no system of its own — every egress and cleanup system joins before it touches a translator.
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    [UpdateLastInPhase]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class EgressPipelineLaunchSystem : IEcsModuleSystem
    {
        private readonly int _workers;

        /// <param name="workers">Threads a scan uses (0 ⇒ <c>FDP_EGRESS_WORKERS</c>, else CPUs / 4, at most 4).</param>
        public EgressPipelineLaunchSystem(int workers = 0) => _workers = workers;

        /// <summary>The pipeline this system launches (after its first run).</summary>
        public EgressPipeline? Pipeline { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository live) return;
            Pipeline ??= EgressPipeline.Of(live, _workers);
            Pipeline.Launch();
        }
    }
}
