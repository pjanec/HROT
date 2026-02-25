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
                // Generational guard — entity may have been destroyed between LOS submission and now.
                // Using full Entity handles (Observer/Target) means a recycled index cannot silently
                // match a different entity; generation mismatch causes IsAlive to return false.
                if (!view.IsAlive(evt.Observer) || !view.IsAlive(evt.Target))
                    continue;

                if (!view.HasComponent<TargetMemory>(evt.Observer))
                    continue;

                ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(evt.Observer);
                TargetMemory mem = memRO;

                // Resolve target position directly — no loop needed with full Entity handle.
                ref readonly var tgtTf = ref view.GetComponentRO<SimTransform>(evt.Target);

                // entityId uses the full packed handle (Index + Generation) so that a recycled
                // entity slot never matches an existing TargetMemory entry for the original entity.
                TargetMemory.AddOrUpdateTarget(
                    ref mem,
                    entityId:   (long)evt.Target.PackedValue,
                    posX:       tgtTf.Position.X,
                    posY:       tgtTf.Position.Y,
                    scoreBoost: VisibleTargetScoreBoost,
                    tick:       tick);

                ecb.SetComponent(evt.Observer, mem);
            }
        }
    }
}
