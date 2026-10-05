using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Brain-tier threat evaluation -- runs on the CGF node inside
    /// <c>CgfThreatEvaluationSystem</c> (via <c>CgfLogicPack</c>).
    ///
    /// <para>
    /// Each frame this system does two things:
    /// <list type="number">
    ///   <item>
    ///     <b>Decay:</b> Multiplies every existing threat score in every
    ///     <see cref="TargetMemory"/> by <c>1 - dt x ThreatScoreDecayPerSecond</c>,
    ///     providing smooth temporal forgetting.
    ///   </item>
    ///   <item>
    ///     <b>Edges</b> (CE-3039): publishes <see cref="SensorChangedEvent"/> FirstThreat / AllClear when the memory
    ///     fills from empty or empties, and Hit when the unit's health drops.
    ///   </item>
    ///   <item>
    ///     <b>Forget</b> (CE-3046): drops an entry whose target is gone, or whose score faded below
    ///     <see cref="PerceptionConstants.ForgetThreatScore"/> while no sensor tracks it.
    ///   </item>
    ///   <item>
    ///     <b>Boost:</b> For entities that also carry an <see cref="ActiveSensorTracks"/>
    ///     cognitive buffer (written by <c>SensorTrackStateIngressTranslator</c>),
    ///     calls <see cref="TargetMemory.AddOrUpdateTarget"/> for each acquired track,
    ///     applying a continuous <c>50 x deltaTime</c> score boost per second.
    ///   </item>
    /// </list>
    /// All mutations go through <c>view.GetCommandBuffer().SetComponent&lt;TargetMemory&gt;</c>
    /// -- never direct world writes.
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
    public class ThreatEvaluationSystem : IEcsModuleSystem
    {
        // CE-3039 — each perceiving unit's health as last seen, to publish the Hit edge. Two maps swapped each tick, so a
        // destroyed unit's entry drops out instead of accumulating.
        private System.Collections.Generic.Dictionary<Entity, float> _lastHealth = new();
        private System.Collections.Generic.Dictionary<Entity, float> _nextHealth = new();

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var ecb  = view.GetCommandBuffer();
            uint tick = view.Tick;

            // Iterate all entities that have TargetMemory: apply decay and optional boost.
            _nextHealth.Clear();
            var memQuery = view.Query().With<TargetMemory>().Build();
            foreach (var entity in memQuery)
            {
                ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(entity);

                // Local copy so we can mutate without violating the snapshot contract.
                TargetMemory mem = memRO;
                int countBefore = mem.Count;

                // Decay all existing threat scores.
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

                // Boost from ActiveSensorTracks (Brain cognitive buffer written by
                // SensorTrackStateIngressTranslator on the CGF node).
                if (view.HasComponent<ActiveSensorTracks>(entity))
                {
                    ref readonly var tracksRO = ref view.GetComponentRO<ActiveSensorTracks>(entity);
                    if (tracksRO.Count > 0)
                    {
                        // Continuous boost: 50 threat-score units per second per active track.
                        float continuousBoost = 50f * deltaTime;

                        for (int i = 0; i < tracksRO.Count; i++)
                        {
                            // Default to the cached entry position (ActiveSensorTracks is 2D; no
                            // cached altitude, so the flat fallback is Z = 0).
                            float posX = tracksRO.PositionsX[i];
                            float posY = tracksRO.PositionsY[i];
                            float posZ = 0f;

                            // If we have a live replica of the target, track its real-time 3D
                            // position — including the authoritative altitude (P3D-206).
                            var targetEntity = new Entity((ulong)tracksRO.EntityIds[i]);
                            if (view.IsAlive(targetEntity) && view.HasComponent<SimTransform>(targetEntity))
                            {
                                ref readonly var targetTf = ref view.GetComponentRO<SimTransform>(targetEntity);
                                posX = targetTf.Position.X;
                                posY = targetTf.Position.Y;
                                posZ = targetTf.Position.Z;
                            }

                            TargetMemory.AddOrUpdateTarget(
                                ref mem,
                                entityId:   tracksRO.EntityIds[i],
                                posX:       posX,
                                posY:       posY,
                                scoreBoost: continuousBoost,
                                tick:       tick,
                                // ⭐ CE-3060 — the kinds that actually hold the track (was: always Visual, so a thermal or heard
                                //   contact read as "in sight" — StandardInputs.HasLineOfSight).
                                modality:   tracksRO.Modalities[i] == 0 ? SensorModality.Visual : (SensorModality)tracksRO.Modalities[i],
                                posZ:       posZ);
                        }
                        changed = true;
                    }
                }

                // ⭐ CE-3046 — FORGET: a target that no longer exists, or one no sensor tracks whose score has faded.
                //   🔴 Nothing used to remove an entry, so dead and long-lost targets kept their slots forever.
                bool hasTracks = view.HasComponent<ActiveSensorTracks>(entity);
                for (int i = mem.Count - 1; i >= 0; i--)
                {
                    var target = new Entity((ulong)mem.EntityIds[i]);
                    bool dead  = !view.IsAlive(target);
                    bool faded = mem.ThreatScores[i] < PerceptionConstants.ForgetThreatScore
                              && !(hasTracks && IsTracked(in view.GetComponentRO<ActiveSensorTracks>(entity), mem.EntityIds[i]));
                    if (!dead && !faded) continue;
                    TargetMemory.Forget(ref mem, i);
                    changed = true;
                }

                if (changed)
                    ecb.SetComponent(entity, mem);

                // ⭐ CE-3039 — the memory's edges, from the system that holds the memory (design §7.3). AllClear is reachable
                //   since CE-3046 made the memory forget.
                if (countBefore == 0 && mem.Count > 0)
                    ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.FirstThreat });
                else if (countBefore > 0 && mem.Count == 0)
                    ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.AllClear });

                // ⭐ CE-3039 — HIT: the unit's health dropped (replicated to this Brain node by EntityDamage) — whoever fired,
                //   seen or not. The previous value is this system's own; the first sight of a unit only records it.
                if (view.HasComponent<Fdp.Toolkit.Combat.Components.Health>(entity))
                {
                    float hp = view.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(entity).Current;
                    if (_lastHealth.TryGetValue(entity, out float before) && hp < before)
                        ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.Hit });
                    _nextHealth[entity] = hp;
                }
            }
            (_lastHealth, _nextHealth) = (_nextHealth, _lastHealth);
        }

        private static unsafe bool IsTracked(in ActiveSensorTracks tracks, long id)
        {
            for (int i = 0; i < tracks.Count; i++)
                if (tracks.EntityIds[i] == id) return true;
            return false;
        }
    }
}