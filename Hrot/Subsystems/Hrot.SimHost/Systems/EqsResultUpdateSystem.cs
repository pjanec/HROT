using System;
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
                buffer.Count         = Math.Min(evt.Results.Count, EqsResultPool.MaxTopK);
                // Ensure LastUpdateTick > 0 so IsReady returns true.
                buffer.LastUpdateTick = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;

                // Write through GetSpanRW() to bypass the [InlineArray] ldobj defensive-copy trap.
                var span = buffer.GetSpanRW();
                for (int i = 0; i < buffer.Count; i++)
                {
                    span[i] = new EqsResult
                    {
                        EntityId  = evt.Results[i].EntityId,
                        PositionX = evt.Results[i].PositionX,
                        PositionY = evt.Results[i].PositionY,
                        Score     = evt.Results[i].Score,
                        Flags     = (short)evt.Results[i].Flags,
                    };
                }
            }

            // ── Path B: Offline unmanaged events from local solver ────────────────
            var unmanagedEvents = view.ReadEvents<EqsResultEvent>();
            if (unmanagedEvents.IsEmpty) return;
            if (!repo.HasSingletonUnmanaged<EqsResultPool>()) return;
            ref var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();

            // Build an inline entity lookup: scan all EqsSensor + NetworkIdentity entities
            // to map SensorNetworkId -> entity.  O(n*m) but n is small for Phase 1.
            var sensorQuery = view.Query()
                .With<EqsSensor>()
                .With<NetworkIdentity>()
                .Build();

            for (int i = 0; i < unmanagedEvents.Length; i++)
            {
                ref readonly var evt = ref unmanagedEvents[i];

                // Find the Brain entity whose NetworkIdentity matches the event's SensorNetworkId.
                Entity observer = default;
                foreach (var candidate in sensorQuery)
                {
                    ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(candidate);
                    if (netId.Value == evt.SensorNetworkId)
                    {
                        observer = candidate;
                        break;
                    }
                }
                if (observer.IsNull || !repo.IsAlive(observer)) continue;
                if (!repo.HasComponent<EqsSensor>(observer)) continue;
                ref readonly var sensor2 = ref repo.GetComponentRO<EqsSensor>(observer);
                // CRITICAL: epoch check — discard stale results.
                if (evt.Epoch != sensor2.Epoch) continue;

                if (!repo.HasComponent<EqsCognitiveBuffer>(observer))
                    repo.AddComponent(observer, new EqsCognitiveBuffer());

                ref var buffer2 = ref repo.GetComponentRW<EqsCognitiveBuffer>(observer);
                buffer2.Count         = Math.Min(evt.EntryCount, EqsResultPool.MaxTopK);
                // Ensure LastUpdateTick > 0 so IsReady returns true even at tick 0.
                buffer2.LastUpdateTick = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;

                // Write through GetSpanRW() to bypass the [InlineArray] ldobj defensive-copy trap.
                var span2 = buffer2.GetSpanRW();
                for (int j = 0; j < buffer2.Count; j++)
                    span2[j] = pool.Results[evt.ResultHandle + j];
            }
        }
    }
}
