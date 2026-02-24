using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async threat evaluation — runs inside <see cref="PerceptionModule"/> on the
    /// background thread via the Snapshot-on-Demand (SoD) pattern.
    /// <para>
    /// Each frame this system does two things:
    /// <list type="number">
    ///   <item>
    ///     <b>Decay:</b> Multiplies every existing threat score in every
    ///     <see cref="TargetMemory"/> by <c>1 − dt × ThreatScoreDecayPerSecond</c>,
    ///     keeping threat awareness up to date even if the target is temporarily out of sight.
    ///   </item>
    ///   <item>
    ///     <b>Boost:</b> Consumes <see cref="TargetVisibleEvent"/>s and calls
    ///     <see cref="TargetMemory.AddOrUpdateTarget"/> with a score boost of 50 for each
    ///     confirmed visible target.
    ///   </item>
    /// </list>
    /// All mutations go through <c>view.GetCommandBuffer().SetComponent&lt;TargetMemory&gt;</c>
    /// — never direct world writes.
    /// </para>
    /// <para>
    /// <b>Read-modify-write contract:</b>
    /// <list type="bullet">
    ///   <item>Read: <c>view.GetComponentRO&lt;TargetMemory&gt;</c> reads from the snapshot.</item>
    ///   <item>Modify: local copy mutated in-memory (no shared state).</item>
    ///   <item>Write: <c>ecb.SetComponent&lt;TargetMemory&gt;</c> enqueues the update.</item>
    ///   <item>Flush: ECB is replayed on the main thread after module execution completes.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class ThreatEvaluationSystem : IModuleSystem
    {
        // Boost applied when a target is confirmed visible by LosRequestBatchingSystem.
        private const float VisibleTargetScoreBoost = 50f;

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var ecb  = view.GetCommandBuffer();
            uint tick = view.Tick;

            // ── Step 1: Decay all existing threat scores ───────────────────────────
            var memQuery = view.Query().With<TargetMemory>().Build();
            foreach (var entity in memQuery)
            {
                ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(entity);

                // Local copy so we can mutate without violating the snapshot contract.
                TargetMemory mem = memRO;

                float decayFactor = 1f - (deltaTime * PerceptionConstants.ThreatScoreDecayPerSecond);
                if (decayFactor < 0f) decayFactor = 0f;

                bool changed = false;
                for (int i = 0; i < mem.Count; i++)
                {
                    float newScore = mem.ThreatScores[i] * decayFactor;
                    if (newScore != mem.ThreatScores[i])
                    {
                        mem.ThreatScores[i] = newScore;
                        changed = true;
                    }
                }

                if (changed)
                    ecb.SetComponent(entity, mem);
            }

            // ── Step 2: Boost scores from confirmed visible events ─────────────────
            var visibleEvents = view.ConsumeEvents<TargetVisibleEvent>();
            foreach (ref readonly var evt in visibleEvents)
            {
                // Find the observer entity by index in the query.
                // We iterate again (harmless — entity count is bounded in Phase 2).
                var observerQuery = view.Query().With<TargetMemory>().With<SimTransform>().Build();
                foreach (var observer in observerQuery)
                {
                    if (observer.Index != evt.ObserverEntityIndex) continue;

                    ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(observer);
                    TargetMemory mem = memRO;

                    // Resolve target position for TargetMemory update.
                    float posX = 0f, posY = 0f;
                    var targetQuery = view.Query().With<SimTransform>().Build();
                    foreach (var tgt in targetQuery)
                    {
                        if (tgt.Index != evt.TargetEntityIndex) continue;
                        ref readonly var tgtTf = ref view.GetComponentRO<SimTransform>(tgt);
                        posX = tgtTf.Position.X;
                        posY = tgtTf.Position.Y;
                        break;
                    }

                    TargetMemory.AddOrUpdateTarget(
                        ref mem,
                        entityId:   evt.TargetEntityIndex,
                        posX:       posX,
                        posY:       posY,
                        scoreBoost: VisibleTargetScoreBoost,
                        tick:       tick);

                    ecb.SetComponent(observer, mem);
                    break;
                }
            }
        }
    }
}
