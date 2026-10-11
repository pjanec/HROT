using System;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Brain-tier simulation system that consumes EQS result payloads from two input paths
    /// and writes them into the entity's <see cref="EqsCognitiveBuffer"/> component.
    ///
    /// <para><b>Path A — Online (DDS-bridged managed event):</b> reads
    /// <see cref="EqsResultUpdateEvent"/> published by <c>EqsResultIngressTranslator</c>
    /// when running in a distributed Brain/Muscle topology over CycloneDDS.</para>
    ///
    /// <para><b>Path B — Offline (direct unmanaged event):</b> reads
    /// <see cref="EqsResultEvent"/> emitted by the local <see cref="EqsSolverSystem"/>
    /// when running in the offline editor (single shared world, no DDS).</para>
    ///
    /// <para>Both paths apply identical staleness, guard, and write logic so that behavior
    /// authored against the offline editor works unchanged in the distributed runtime.</para>
    ///
    /// <para><b>Critical constraints:</b>
    /// <list type="bullet">
    ///   <item>Epoch check is <c>evt.Epoch != sensor.Epoch</c> — NOT a tick comparison.</item>
    ///   <item>Buffer writes must go through <see cref="EqsCognitiveBuffer.GetSpanRW"/> to
    ///     bypass the C# 12 [InlineArray] ldobj defensive-copy trap (Design §8.1).</item>
    /// </list></para>
    /// </summary>
    // ⭐⭐ CE-165 — the second system carried by BOTH CgfLogicPack (Brain) and SimHostCoreLogicPack
    // (MuscleGround), so a node in both roles registers it twice unless the composition root deduplicates.
    // ⚠ Stated honestly: unlike UnitHierarchySystem, a second tick here has NOT been measured to corrupt —
    // it loops over buffers and writes results rather than accumulating. It is marked anyway because it is
    // a singleton BY DESIGN (one EQS result pump per node) and a duplicate registration is a composition
    // defect whatever the second tick happens to cost. If a legitimate need for two instances ever appears,
    // remove the attribute and say why — do not weaken the guard.
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EqsResultUpdateSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // ── Path A: Online managed events from DDS ingress translator ─────────
            foreach (var evt in repo.Bus.ReadManaged<EqsResultUpdateEvent>())
            {
                if (!repo.IsAlive(evt.Observer)) continue;
                if (!repo.HasComponent<EqsSensor>(evt.Observer)) continue;
                ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(evt.Observer);
                // CRITICAL: epoch mismatch means the result is stale — discard silently.
                // Compare version counter against version counter, NOT against tick.
                if (evt.Epoch != sensor.Epoch) continue;

                if (!repo.HasComponent<EqsCognitiveBuffer>(evt.Observer))
                    repo.AddComponent(evt.Observer, new EqsCognitiveBuffer());

                ref var buffer = ref repo.GetComponentRW<EqsCognitiveBuffer>(evt.Observer);
                var topBefore = TopOf(in buffer);
                buffer.Count         = Math.Min(evt.Results.Count, EqsResultPool.MaxTopK);
                // Ensure LastUpdateTick > 0 so IsReady returns true.
                buffer.LastUpdateTick          = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;
                buffer.LastUpdateTimeSeconds   = (float)view.Time;

                // Write through GetSpanRW() to bypass the [InlineArray] ldobj defensive-copy trap.
                var span = buffer.GetSpanRW();
                for (int i = 0; i < buffer.Count; i++)
                {
                    span[i] = new EqsResult
                    {
                        EntityId        = evt.Results[i].EntityId,
                        PositionX       = evt.Results[i].PositionX,
                        PositionY       = evt.Results[i].PositionY,
                        PositionZ       = evt.Results[i].PositionZ,
                        Score           = evt.Results[i].Score,
                        Flags           = (short)evt.Results[i].Flags,
                        FlagsMeaningful = (short)evt.Results[i].FlagsMeaningful,
                        Stance          = evt.Results[i].Stance,   // ⭐ CE-3135
                        Kind            = evt.Results[i].Kind,     // ⭐ CE-3158 G1
                    };
                }
                NotifyTopChanged(repo, evt.Observer, topBefore, in buffer);
            }

            // ── Path B: Offline unmanaged events from local solver ────────────────
            var unmanagedEvents = view.ReadEvents<EqsResultEvent>();
            if (unmanagedEvents.IsEmpty) return;
            if (!repo.HasSingletonUnmanaged<EqsResultPool>()) return;
            ref var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();

            // Build an inline entity lookup: scan all EqsSensor entities (NetworkIdentity
            // not required -- child-entity and local-only sensors are also handled here).
            var sensorQuery = view.Query()
                .With<EqsSensor>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            for (int i = 0; i < unmanagedEvents.Length; i++)
            {
                ref readonly var evt = ref unmanagedEvents[i];

                // Find the entity whose compound key matches (ParentNetworkId, LocalChildIndex).
                Entity observer = default;
                foreach (var candidate in sensorQuery)
                {
                    if (evt.ParentNetworkId == 0)
                    {
                        // Local-only sensor: matched by entity Index.
                        if (candidate.Index == evt.LocalChildIndex)
                        {
                            observer = candidate;
                            break;
                        }
                    }
                    else if (EqsSensorKey.Resolve(view, candidate, out long net, out int index, out _)
                                 is EqsSensorKeyKind.Child or EqsSensorKeyKind.Legacy
                             && net == evt.ParentNetworkId && index == evt.LocalChildIndex)
                    {
                        // ⭐ The ONE wire-key rule (EqsSensorKey): a child sensor by (parent's network id, part id) —
                        //   InstanceId 0 is a valid first child index — or a legacy sensor on the networked entity itself.
                        observer = candidate;
                        break;
                    }
                }
                if (observer.IsNull || !repo.IsAlive(observer)) continue;
                if (!repo.HasComponent<EqsSensor>(observer)) continue;
                ref readonly var sensor2 = ref repo.GetComponentRO<EqsSensor>(observer);
                // CRITICAL: epoch check -- discard stale results.
                if (evt.Epoch != sensor2.Epoch) continue;

                if (!repo.HasComponent<EqsCognitiveBuffer>(observer))
                    repo.AddComponent(observer, new EqsCognitiveBuffer());

                ref var buffer2 = ref repo.GetComponentRW<EqsCognitiveBuffer>(observer);
                var topBefore2 = TopOf(in buffer2);
                buffer2.Count                = Math.Min(evt.EntryCount, EqsResultPool.MaxTopK);
                // Ensure LastUpdateTick > 0 so IsReady returns true even at tick 0.
                buffer2.LastUpdateTick          = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;
                buffer2.LastUpdateTimeSeconds   = (float)view.Time;

                // Write through GetSpanRW() to bypass the [InlineArray] ldobj defensive-copy trap.
                var span2 = buffer2.GetSpanRW();
                for (int j = 0; j < buffer2.Count; j++)
                    span2[j] = pool.Results[evt.ResultHandle + j];
                NotifyTopChanged(repo, observer, topBefore2, in buffer2);
            }
        }

        // The best result as an identity: (entity, position) — a positional result moves, an entity result changes entity.
        private static (long Id, float X, float Y, bool Any) TopOf(in EqsCognitiveBuffer buffer)
        {
            if (buffer.Count == 0) return (0, 0f, 0f, false);
            var top = buffer.GetSpanRO()[0];
            return (top.EntityId, top.PositionX, top.PositionY, true);
        }

        /// <summary>
        /// ⭐ CE-3039 — a PERCEPTION sensor's best result changed ⇒ <see cref="SensorChangedEvent"/> TopChanged for its unit
        /// (design §7.3: the producer is the system that writes the buffer). A query sensor (cover, flank …) has no
        /// <see cref="SensorTag"/> and its blueprint reads it with <c>When EqsResult(TopChanged)</c> instead.
        /// </summary>
        private static void NotifyTopChanged(EntityRepository repo, Entity sensor, (long Id, float X, float Y, bool Any) before,
                                             in EqsCognitiveBuffer after)
        {
            if (!repo.IsComponentTypeRegistered<SensorTag>() || !repo.HasComponent<SensorTag>(sensor)) return;
            if (!repo.HasComponent<PartMetadata>(sensor)) return;
            var now = TopOf(in after);
            if (now == before) return;
            ((ISimulationView)repo).GetCommandBuffer().PublishEvent(new SensorChangedEvent
            {
                Unit   = repo.GetComponentRO<PartMetadata>(sensor).ParentEntity,
                Sensor = sensor,
                Target = now.Any && now.Id > 0 ? new Entity((ulong)now.Id) : Entity.Null,
                Kind   = repo.GetComponentRO<SensorTag>(sensor).Kind,
                What   = SensorChange.TopChanged,
            });
        }
    }
}
