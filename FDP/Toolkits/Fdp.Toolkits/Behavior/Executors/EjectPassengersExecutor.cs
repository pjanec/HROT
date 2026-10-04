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
        /// <summary>Hull radius assumed when the vehicle has no collider.</summary>
        public const float DefaultHullRadius = 2f;

        /// <summary>Gap between the hull and the dismounted column.</summary>
        public const float DismountClearance = 1.5f;

        /// <summary>Spacing of the dismounted column along the vehicle.</summary>
        public const float DismountSpacing = 1.5f;

        /// <inheritdoc/>
        public void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref var buffer     = ref world.GetComponentRW<PassengerBuffer>(entity);
            var vehicleTf      = world.GetComponent<SimTransform>(entity);
            Vector3 vehiclePos = vehicleTf.Position;

            // ⭐ CE-321 — dismount BESIDE the hull, relative to the vehicle's heading: a column along its RIGHT side, clear of
            //   its collider. 🔴 It used to drop passengers at a fixed WORLD offset (−4 m in Y), 0.5 m outside a 3.5 m APC
            //   collider and behind it whatever its heading — with the hull between them and a threat ahead, so their fire
            //   killed their own APC. Forward = Transform(UnitX, rotation) (X east, Y north, the vision convention).
            var fwd3    = Vector3.Transform(Vector3.UnitX, vehicleTf.Rotation);
            var forward = new Vector2(fwd3.X, fwd3.Y);
            forward     = forward.LengthSquared() > 1e-6f ? Vector2.Normalize(forward) : Vector2.UnitX;
            var right   = new Vector2(forward.Y, -forward.X);
            float hull  = world.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
                          && world.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(entity)
                ? world.GetComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(entity).Radius
                : DefaultHullRadius;
            float side  = hull + DismountClearance;

            for (int i = 0; i < buffer.Count; i++)
            {
                Entity passenger = buffer.Passengers[i];

                // Dead-passenger guard — skip silently.
                if (!world.IsAlive(passenger))
                    continue;

                // A column beside the vehicle, 1.5 m apart along its length.
                var along        = forward * ((i - (buffer.Count - 1) / 2f) * DismountSpacing);
                var lateral      = right * side;
                var offset       = new Vector3(along.X + lateral.X, along.Y + lateral.Y, 0f);
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
