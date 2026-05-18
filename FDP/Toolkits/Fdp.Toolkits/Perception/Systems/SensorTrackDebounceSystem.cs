using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Muscle-tier sensor debounce system. Replaces <see cref="ThreatEvaluationSystem"/>
    /// on the SimHost node.
    ///
    /// <para><b>Responsibilities:</b>
    /// <list type="number">
    ///   <item>Consumes <see cref="TargetVisibleEvent"/>s from the module-private scoped bus
    ///     and records sightings in <see cref="SensorContactList"/> (raw, cognitively-neutral).</item>
    ///   <item>Evaluates hysteresis: a contact transitions from
    ///     <see cref="SensorContactState.Pending"/> / <see cref="SensorContactState.Lost"/>
    ///     to <see cref="SensorContactState.Acquired"/> when seen in the current tick,
    ///     and from <see cref="SensorContactState.Acquired"/> to <see cref="SensorContactState.Lost"/>
    ///     when the occlusion age exceeds <see cref="TrackLostThresholdTicks"/>.</item>
    ///   <item>Publishes a <see cref="SensorTrackStateEvent"/> to the command buffer whenever
    ///     a contact transitions to <see cref="SensorContactState.Acquired"/> or
    ///     <see cref="SensorContactState.Lost"/>.  Inside <c>AutonomousPerceptionModule</c>
    ///     these events land on the module-private scoped bus and are then forwarded to the
    ///     global world bus so that <see cref="ActiveSensorTracksUpdateSystem"/> and the
    ///     DDS egress translator can consume them.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Read-modify-write contract:</b>
    /// Reads <see cref="SensorContactList"/> from the SoD snapshot, modifies a local copy,
    /// then writes via <c>ecb.SetComponent</c> (or <c>ecb.AddComponent</c> on first encounter).
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Manual)]
    public class SensorTrackDebounceSystem : IEcsModuleSystem
    {
        // 20 ticks at 10 Hz perception rate = 2 seconds of occlusion tolerance.
        private const uint TrackLostThresholdTicks = 20;

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var ecb = view.GetCommandBuffer();
            uint currentTick = view.Tick;

            var visibleEvents = view.ReadEvents<TargetVisibleEvent>();

            // ── Pass 1: update entities that already have SensorContactList ───────
            var query = view.Query().With<SensorContactList>().Build();
            foreach (var entity in query)
            {
                ref readonly var listRO = ref view.GetComponentRO<SensorContactList>(entity);
                SensorContactList list = listRO;
                bool changed = false;

                // Apply all sightings for this observer.
                foreach (ref readonly var evt in visibleEvents)
                {
                    if (evt.Observer != entity) continue;
                    if (!view.IsAlive(evt.Target)) continue;

                    long targetId = (long)evt.Target.PackedValue;
                    SensorContactList.UpdateSighting(ref list, targetId, currentTick);
                    changed = true;
                }

                // Evaluate hysteresis transitions.
                for (int i = 0; i < list.Count; i++)
                {
                    var currentState = (SensorContactState)list.State[i];
                    uint age = currentTick - list.LastSeenTick[i];

                    if (currentState == SensorContactState.Pending ||
                        currentState == SensorContactState.Lost)
                    {
                        if (age == 0)
                        {
                            list.State[i] = (byte)SensorContactState.Acquired;
                            changed = true;

                            var targetEntity = new Entity((ulong)list.EntityIds[i]);
                            float posX = 0f, posY = 0f;
                            if (view.IsAlive(targetEntity) &&
                                view.HasComponent<SimTransform>(targetEntity))
                            {
                                ref readonly var tf = ref view.GetComponentRO<SimTransform>(targetEntity);
                                posX = tf.Position.X;
                                posY = tf.Position.Y;
                            }
                            ecb.PublishEvent(new SensorTrackStateEvent
                            {
                                Observer  = entity,
                                Target    = targetEntity,
                                State     = SensorTrackStatus.Acquired,
                                PositionX = posX,
                                PositionY = posY,
                            });
                        }
                    }
                    else if (currentState == SensorContactState.Acquired)
                    {
                        if (age > TrackLostThresholdTicks)
                        {
                            list.State[i] = (byte)SensorContactState.Lost;
                            changed = true;

                            var targetEntity = new Entity((ulong)list.EntityIds[i]);
                            ecb.PublishEvent(new SensorTrackStateEvent
                            {
                                Observer  = entity,
                                Target    = targetEntity,
                                State     = SensorTrackStatus.Lost,
                                PositionX = 0f,
                                PositionY = 0f,
                            });
                        }
                    }
                }

                if (changed)
                    ecb.SetComponent(entity, list);
            }

            // ── Pass 2: bootstrap SensorContactList for newly-seen observers ─────
            foreach (ref readonly var evt in visibleEvents)
            {
                if (!view.IsAlive(evt.Observer) || !view.IsAlive(evt.Target)) continue;
                if (view.HasComponent<SensorContactList>(evt.Observer)) continue; // handled in Pass 1

                long targetId = (long)evt.Target.PackedValue;
                var list = new SensorContactList();
                SensorContactList.UpdateSighting(ref list, targetId, currentTick);
                // age is 0, so immediately transition to Acquired.
                list.State[0] = (byte)SensorContactState.Acquired;

                ecb.AddComponent(evt.Observer, list);

                // Emit Acquired event for the bootstrapped contact.
                float posX = 0f, posY = 0f;
                if (view.HasComponent<SimTransform>(evt.Target))
                {
                    ref readonly var tf = ref view.GetComponentRO<SimTransform>(evt.Target);
                    posX = tf.Position.X;
                    posY = tf.Position.Y;
                }
                ecb.PublishEvent(new SensorTrackStateEvent
                {
                    Observer  = evt.Observer,
                    Target    = evt.Target,
                    State     = SensorTrackStatus.Acquired,
                    PositionX = posX,
                    PositionY = posY,
                });
            }
        }
    }
}
