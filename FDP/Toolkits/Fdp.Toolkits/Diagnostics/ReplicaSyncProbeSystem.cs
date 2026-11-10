using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// ⭐ CE-3427 N-1 — a MEASURING instrument, off unless <c>HROT_REPLICA_PROBE=1</c>: what a per-frame replica of the live world (the
    /// kernel's <c>FastReplica</c> / <see cref="DoubleBufferProvider"/>, the egress-off-the-frame idea) would cost the main thread. It keeps
    /// one FULL replica (every component — the upper bound; a real egress replica syncs only what its translators read) and times its
    /// <see cref="DoubleBufferProvider.Update"/> each frame into the query slot <c>probe.replica.sync (full)</c> (the scale rig lists it).
    /// ⚠ It doubles the world's chunk memory while on. Its own empty event accumulator, so it holds no event history.
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class ReplicaSyncProbeSystem : IEcsModuleSystem
    {
        /// <summary>The switch: <c>HROT_REPLICA_PROBE=1</c>.</summary>
        public static bool Requested => System.Environment.GetEnvironmentVariable("HROT_REPLICA_PROBE") == "1";

        private static readonly int S_Sync = QueryTimings.Slot("probe.replica.sync (full)");
        private DoubleBufferProvider? _replica;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository live) return;
            _replica ??= new DoubleBufferProvider(live, new EventAccumulator());
            long t0 = QueryTimings.Begin();
            long b0 = QueryTimings.Bytes();
            _replica.Update();
            QueryTimings.End(S_Sync, t0, b0);
        }
    }
}
