using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Parameters written into <see cref="InteractionChannel.Params"/> when requesting an embark.
    /// Must be &lt;= <see cref="BehaviorConstants.ActionParamsByteSize"/> bytes.
    /// </summary>
    public struct EmbarkParams
    {
        /// <summary>The vehicle entity the soldier wishes to board.</summary>
        public Entity VehicleEntity;

        /// <summary>
        /// Maximum distance (metres) from the soldier to the vehicle for boarding to succeed.
        /// Default: 3.0 m.
        /// </summary>
        public float MaxBoardingRange;
    }

    /// <summary>
    /// Executor for the <c>EmbarkVehicle</c> interaction action (kind = 1).
    /// Registered with <see cref="Systems.InteractionDispatcherSystem"/> by the host application.
    ///
    /// <para><b>Execute contract:</b></para>
    /// <list type="number">
    ///   <item>Read <see cref="EmbarkParams"/> from <c>channel.Params</c>.</item>
    ///   <item>Guard — vehicle not alive → <c>Failure</c>.</item>
    ///   <item>Distance check via <see cref="SimTransform.Position"/> — too far → <c>Running</c>
    ///         (locomotion is assumed to be closing the distance).</item>
    ///   <item>Capacity check — <see cref="PassengerBuffer"/> full → <c>Failure</c>.</item>
    ///   <item>Add soldier to <see cref="PassengerBuffer"/>.</item>
    ///   <item>Strip <see cref="ActorCapabilities.CanMove"/> and <see cref="ActorCapabilities.CanShoot"/>.</item>
    ///   <item>Add <see cref="IsEmbarkedTag"/> to the soldier entity.</item>
    ///   <item><c>channel.Status = Success</c>.</item>
    /// </list>
    /// </summary>
    public class EmbarkExecutor : IActionExecutor<InteractionChannel>
    {
        /// <inheritdoc/>
        public void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world) { }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref readonly var p = ref Unsafe.As<byte, EmbarkParams>(ref channel.Params[0]);

            // 1. Guard: vehicle must still be alive.
            if (!world.IsAlive(p.VehicleEntity))
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // 2. Distance check via SimTransform (Phase 0 Adaptation).
            Vector3 soldierPos  = world.GetComponent<SimTransform>(entity).Position;
            Vector3 vehiclePos  = world.GetComponent<SimTransform>(p.VehicleEntity).Position;
            float   distance    = Vector3.Distance(soldierPos, vehiclePos);

            if (distance > p.MaxBoardingRange)
            {
                // Still approaching — locomotion should be driving the entity toward the vehicle.
                channel.Status = NodeStatus.Running;
                return;
            }

            // 3. Capacity check.
            ref var buffer = ref world.GetComponentRW<PassengerBuffer>(p.VehicleEntity);
            if (buffer.Count >= PassengerBuffer.Capacity)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // 4. Add soldier to passenger roster.
            buffer.Passengers[buffer.Count] = entity;
            buffer.Count++;

            // 5. Strip mobility and weapon capabilities.
            ref var caps = ref world.GetComponentRW<ActorCapabilityState>(entity);
            caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

            // 6. Tag the soldier as embarked.
            world.AddComponent(entity, new IsEmbarkedTag { VehicleEntity = p.VehicleEntity });

            channel.Status = NodeStatus.Success;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
