using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Brain-tier system that consumes <see cref="SensorTrackStateEvent"/> from the global
    /// world bus and updates the <see cref="ActiveSensorTracks"/> cognitive buffer on observer
    /// entities accordingly.
    ///
    /// <para>
    /// This system replaces the component-mutation logic that was previously embedded in
    /// <c>SensorTrackStateIngressTranslator</c>.  By moving the mutation into a standard ECS
    /// system it runs correctly in every deployment mode:
    /// <list type="bullet">
    ///   <item><b>Distributed cluster:</b> <c>SensorTrackStateIngressTranslator</c> receives a
    ///     DDS <c>SensorTrackState</c> sample and publishes a <see cref="SensorTrackStateEvent"/>
    ///     onto the local bus.  This system then consumes that event.</item>
    ///   <item><b>Networkless Editor:</b> the EQS solver's memory stage publishes the event on the
    ///     same world bus.  This system consumes it without any DDS involvement, making
    ///     <see cref="ActiveSensorTracks"/> available to <see cref="ThreatEvaluationSystem"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Read-modify-write contract:</b>
    /// Reads (or bootstraps) <see cref="ActiveSensorTracks"/> from the snapshot ONCE per observer per frame, applies every
    /// event of that observer to the one local copy, then writes it once via <c>ecb.SetComponent</c> / <c>ecb.AddComponent</c>.
    /// </para>
    /// <para>⭐⭐ <c>CE-3073</c> — it used to read the snapshot per EVENT and write the whole component per event. The snapshot
    /// does not see the command buffer's queued writes, so with several contacts acquired in one frame each event started
    /// from the frame-start list and the LAST overwrite won: a rifleman facing three visible enemies remembered ONE
    /// (measured live on <c>ua-threat-ranking</c>, <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §2.2). The memory stage
    /// publishes only on a change, so the lost contacts never came back.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class ActiveSensorTracksUpdateSystem : IEcsModuleSystem
    {
        // ⭐ CE-3073 — this frame's working copy per observer, and the order observers were first seen (reused, no per-frame
        //   allocation once warm).
        private readonly Dictionary<Entity, (ActiveSensorTracks Tracks, bool Had)> _working = new();
        private readonly List<Entity> _order = new();

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<SensorTrackStateEvent>();
            if (events.IsEmpty) return;

            var ecb = view.GetCommandBuffer();
            _working.Clear();
            _order.Clear();

            foreach (ref readonly var evt in events)
            {
                if (!view.IsAlive(evt.Observer)) continue;

                long localTargetId = (long)evt.Target.PackedValue;

                if (!_working.TryGetValue(evt.Observer, out var entry))
                {
                    bool had = view.HasComponent<ActiveSensorTracks>(evt.Observer);
                    entry = (had ? view.GetComponentRO<ActiveSensorTracks>(evt.Observer) : new ActiveSensorTracks(), had);
                    _order.Add(evt.Observer);
                }
                ActiveSensorTracks tracks = entry.Tracks;

                if (evt.State == SensorTrackStatus.Acquired)
                {
                    byte kinds = evt.Modality == 0 ? (byte)SensorModality.Visual : (byte)evt.Modality;   // CE-3060
                    // Update position if already tracked, or add a new slot.
                    bool found = false;
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks.EntityIds[i] == localTargetId)
                        {
                            tracks.PositionsX[i] = evt.PositionX;
                            tracks.PositionsY[i] = evt.PositionY;
                            tracks.Modalities[i] = kinds;
                            found = true;
                            break;
                        }
                    }
                    if (!found && tracks.Count < PerceptionConstants.MaxTrackedTargets)
                    {
                        tracks.EntityIds[tracks.Count]  = localTargetId;
                        tracks.PositionsX[tracks.Count] = evt.PositionX;
                        tracks.PositionsY[tracks.Count] = evt.PositionY;
                        tracks.Modalities[tracks.Count] = kinds;
                        tracks.Count++;
                        // ⭐ CE-3039 — the edge, from the system that holds the track set (design §7.3).
                        ecb.PublishEvent(new SensorChangedEvent { Unit = evt.Observer, Target = evt.Target, What = SensorChange.Acquired });
                    }
                }
                else // SensorTrackStatus.Lost
                {
                    // Compact-remove: swap the target slot with the last entry, then shrink Count.
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks.EntityIds[i] != localTargetId) continue;
                        int last = tracks.Count - 1;
                        if (i < last)
                        {
                            tracks.EntityIds[i]  = tracks.EntityIds[last];
                            tracks.PositionsX[i] = tracks.PositionsX[last];
                            tracks.PositionsY[i] = tracks.PositionsY[last];
                            tracks.Modalities[i] = tracks.Modalities[last];
                        }
                        tracks.Count--;
                        ecb.PublishEvent(new SensorChangedEvent { Unit = evt.Observer, Target = evt.Target, What = SensorChange.Lost });   // CE-3039
                        break;
                    }
                }

                _working[evt.Observer] = (tracks, entry.Had);
            }

            foreach (var observer in _order)
            {
                var (tracks, had) = _working[observer];
                if (had)
                    ecb.SetComponent(observer, tracks);
                else
                    ecb.AddComponent(observer, tracks);
            }
        }
    }
}
