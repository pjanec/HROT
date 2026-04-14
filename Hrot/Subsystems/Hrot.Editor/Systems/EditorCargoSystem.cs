using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes embark and disembark commands in the editor world.
/// Manages <see cref="PassengerBuffer"/> and <see cref="IsEmbarkedTag"/> components
/// to reflect cargo state changes requested via <see cref="EmbarkEntityCommand"/>
/// and <see cref="DisembarkEntityCommand"/>.
/// </summary>
public sealed class EditorCargoSystem : ComponentSystem
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        ProcessEmbark();
        ProcessDisembark();
    }

    private void ProcessEmbark()
    {
        var cmds = World.Bus.Consume<EmbarkEntityCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!World.IsAlive(cmd.Passenger) || !World.IsAlive(cmd.Vehicle)) continue;
            if (!World.HasComponent<PassengerBuffer>(cmd.Vehicle)) continue;

            ref var buffer = ref World.GetComponentRW<PassengerBuffer>(cmd.Vehicle);
            if (buffer.Count >= PassengerBuffer.Capacity) continue;

            buffer.Passengers[buffer.Count++] = cmd.Passenger;

            // Strip movement and combat capability while embarked.
            if (World.HasComponent<ActorCapabilityState>(cmd.Passenger))
            {
                ref var caps = ref World.GetComponentRW<ActorCapabilityState>(cmd.Passenger);
                caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
            }

            World.AddComponent(cmd.Passenger, new IsEmbarkedTag { VehicleEntity = cmd.Vehicle });
        }
    }

    private void ProcessDisembark()
    {
        var cmds = World.Bus.Consume<DisembarkEntityCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!World.IsAlive(cmd.Passenger)) continue;
            if (!World.HasComponent<IsEmbarkedTag>(cmd.Passenger)) continue;

            ref readonly var tag = ref World.GetComponent<IsEmbarkedTag>(cmd.Passenger);
            Entity vehicle = tag.VehicleEntity;

            // Remove from vehicle's passenger buffer.
            if (World.IsAlive(vehicle) && World.HasComponent<PassengerBuffer>(vehicle))
            {
                ref var buffer = ref World.GetComponentRW<PassengerBuffer>(vehicle);
                for (int s = 0; s < buffer.Count; s++)
                {
                    if (buffer.Passengers[s] == cmd.Passenger)
                    {
                        // Shift remaining slots down and decrement count.
                        buffer.Passengers[s] = buffer.Passengers[buffer.Count - 1];
                        buffer.Count--;
                        break;
                    }
                }
            }

            // Restore movement and combat capability.
            if (World.HasComponent<ActorCapabilityState>(cmd.Passenger))
            {
                ref var caps = ref World.GetComponentRW<ActorCapabilityState>(cmd.Passenger);
                caps.Capabilities |= ActorCapabilities.CanMove | ActorCapabilities.CanShoot;
            }

            World.RemoveComponent<IsEmbarkedTag>(cmd.Passenger);
        }
    }
}
