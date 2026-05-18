using System.Numerics;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Executor for the <c>EjectPassengers</c> interaction action
    /// (<see cref="BehaviorConstants.ActionIdEjectPassengers"/> = 3).
    /// Registered with <see cref="Systems.InteractionDispatcherSystem"/> by the host application.
    ///
    /// <para>Runs on the <b>vehicle</b> entity.  Iterates the vehicle's
    /// <see cref="PassengerBuffer"/>, restores capabilities on every live passenger, removes
    /// their <see cref="IsEmbarkedTag"/>, scatters them to offset positions beside the vehicle,
    /// then clears the buffer.</para>
    ///
    /// <para><b>Dead passenger guard:</b> if a passenger entity is no longer alive (killed
    /// while embarked), the slot is skipped without error.</para>
    ///
    /// <para><b>Slot-offset formula:</b>
    /// <c>offset = new Vector3((i - buffer.Count / 2f) * 1.5f, -4f, 0f)</c> —
    /// places passengers in a row along the vehicle's side (negative-Y = side in ENU).
    /// For 2 passengers: offsets are −1.5 m and 0.0 m on X.
    /// For 4 passengers: offsets are −3.0 m, −1.5 m, 0.0 m, +1.5 m on X.</para>
    /// </summary>
    public class EjectPassengersExecutor : IActionExecutor<InteractionChannel>
    {
        /// <inheritdoc/>
        public void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref var buffer     = ref world.GetComponentRW<PassengerBuffer>(entity);
            Vector3 vehiclePos = world.GetComponent<SimTransform>(entity).Position;

            for (int i = 0; i < buffer.Count; i++)
            {
                Entity passenger = buffer.Passengers[i];

                // Dead-passenger guard — skip silently.
                if (!world.IsAlive(passenger))
                    continue;

                // Scatter to side of vehicle.
                var offset       = new Vector3((i - buffer.Count / 2f) * 1.5f, -4f, 0f);
                ref var tf       = ref world.GetComponentRW<SimTransform>(passenger);
                tf.Position      = vehiclePos + offset;

                // Restore locomotion and weapon capabilities.
                if (world.HasComponent<ActorCapabilityState>(passenger))
                {
                    ref var caps = ref world.GetComponentRW<ActorCapabilityState>(passenger);
                    caps.Capabilities |= ActorCapabilities.CanMove | ActorCapabilities.CanShoot;
                }

                // Remove the embarked tag.
                if (world.HasComponent<IsEmbarkedTag>(passenger))
                    world.RemoveComponent<IsEmbarkedTag>(passenger);
            }

            // Clear the passenger buffer.
            buffer.Count = 0;

            channel.Status = NodeStatus.Success;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
