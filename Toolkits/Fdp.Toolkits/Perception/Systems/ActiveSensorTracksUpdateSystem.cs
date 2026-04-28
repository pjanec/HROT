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
    ///   <item><b>Networkless Editor:</b> <c>AutonomousPerceptionModule</c> bridges the event
    ///     directly from the Muscle tier to the global world bus.  This system consumes it
    ///     without any DDS involvement, making <see cref="ActiveSensorTracks"/> available to
    ///     <see cref="ThreatEvaluationSystem"/> in the same frame.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Read-modify-write contract:</b>
    /// Reads (or bootstraps) <see cref="ActiveSensorTracks"/> from the snapshot, modifies a
    /// local copy, then writes via <c>ecb.SetComponent</c> / <c>ecb.AddComponent</c>.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class ActiveSensorTracksUpdateSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<SensorTrackStateEvent>();
            if (events.IsEmpty) return;

            var ecb = view.GetCommandBuffer();

            foreach (ref readonly var evt in events)
            {
                if (!view.IsAlive(evt.Observer)) continue;

                long localTargetId = (long)evt.Target.PackedValue;

                bool hasComponent = view.HasComponent<ActiveSensorTracks>(evt.Observer);
                ActiveSensorTracks tracks = hasComponent
                    ? view.GetComponentRO<ActiveSensorTracks>(evt.Observer)
                    : new ActiveSensorTracks();

                if (evt.State == SensorTrackStatus.Acquired)
                {
                    // Update position if already tracked, or add a new slot.
                    bool found = false;
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks.EntityIds[i] == localTargetId)
                        {
                            tracks.PositionsX[i] = evt.PositionX;
                            tracks.PositionsY[i] = evt.PositionY;
                            found = true;
                            break;
                        }
                    }
                    if (!found && tracks.Count < PerceptionConstants.MaxTrackedTargets)
                    {
                        tracks.EntityIds[tracks.Count]  = localTargetId;
                        tracks.PositionsX[tracks.Count] = evt.PositionX;
                        tracks.PositionsY[tracks.Count] = evt.PositionY;
                        tracks.Count++;
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
                        }
                        tracks.Count--;
                        break;
                    }
                }

                if (hasComponent)
                    ecb.SetComponent(evt.Observer, tracks);
                else
                    ecb.AddComponent(evt.Observer, tracks);
            }
        }
    }
}
