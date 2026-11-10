using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Network.Cyclone.Egress
{
    /// <summary>
    /// ⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7, O-B) — an egress translator whose STATE scan may run on a replica of the
    /// world, off the main thread, one frame late. Its egress system then calls only <see cref="BeginFrame"/> in its slot (on the live world)
    /// and the <see cref="EgressPipeline"/> calls <see cref="ScanState"/> on the replica after the phase. While the pipeline is on the frame
    /// (the default), <see cref="INetworkTranslator.ScanAndPublish"/> runs as always — it is <c>BeginFrame</c> then <c>ScanState</c>.
    /// <para>⛔ The contract a marked translator keeps — each a hazard of §7:</para>
    /// <list type="bullet">
    ///   <item><see cref="ScanState"/> READS only the view it is given: no bus, no <c>NetworkEntityMap</c>, no live-world singleton;</item>
    ///   <item>it writes nothing to that view when <c>offFrame</c> — a world write is queued and made by <see cref="ApplyDeferred"/> on the
    ///         live world at the next join (main thread);</item>
    ///   <item>everything it does per frame that reads the bus or the world epoch (<c>PublishedInstances.OnFrame</c>) is in
    ///         <see cref="BeginFrame"/>;</item>
    ///   <item>its <c>Dispose</c> / <c>ReleaseInstance</c> (the cleanup system) run on the main thread, never while a scan runs — the pipeline
    ///         joins first.</item>
    /// </list>
    /// </summary>
    public interface IOffFrameEgress : IDescriptorTranslator
    {
        /// <summary>The per-frame bookkeeping on the LIVE world, in the egress system's slot: the world-epoch and destruction checks.</summary>
        void BeginFrame(ISimulationView live);

        /// <summary>The state scan: publishes what changed. <paramref name="offFrame"/> ⇒ <paramref name="view"/> is a replica: queue every
        /// world write for <see cref="ApplyDeferred"/>.</summary>
        void ScanState(ISimulationView view, bool offFrame);

        /// <summary>Makes the world writes queued by the last off-frame scan, on the live world (main thread, at the join).</summary>
        void ApplyDeferred(EntityRepository live) { }
    }
}
