using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes embark and disembark commands in the editor world.
/// Manages <see cref="PassengerBuffer"/> and <see cref="IsEmbarkedTag"/> components
/// to reflect cargo state changes requested via <see cref="EmbarkEntityCommand"/>
/// and <see cref="DisembarkEntityCommand"/>.
/// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EditorCargoSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        ProcessEmbark(view);
        ProcessDisembark(view);
    }

    private void ProcessEmbark(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        var cmds = view.ReadEvents<EmbarkEntityCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!view.IsAlive(cmd.Passenger) || !view.IsAlive(cmd.Vehicle)) continue;
            if (!view.HasComponent<PassengerBuffer>(cmd.Vehicle)) continue;

            ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(cmd.Vehicle);
            if (buffer.Count >= PassengerBuffer.Capacity) continue;

            buffer.Passengers[buffer.Count++] = cmd.Passenger;

            // Strip movement and combat capability while embarked.
            if (view.HasComponent<ActorCapabilityState>(cmd.Passenger))
            {
                ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(cmd.Passenger);
                caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
            }

            repo.AddComponent(cmd.Passenger, new IsEmbarkedTag { VehicleEntity = cmd.Vehicle });
        }
    }

    private void ProcessDisembark(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        var cmds = view.ReadEvents<DisembarkEntityCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!view.IsAlive(cmd.Passenger)) continue;
            if (!view.HasComponent<IsEmbarkedTag>(cmd.Passenger)) continue;

            ref readonly var tag = ref repo.GetComponent<IsEmbarkedTag>(cmd.Passenger);
            Entity vehicle = tag.VehicleEntity;

            // Remove from vehicle's passenger buffer.
            if (view.IsAlive(vehicle) && view.HasComponent<PassengerBuffer>(vehicle))
            {
                ref var buffer = ref repo.GetComponentRW<PassengerBuffer>(vehicle);
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
            if (view.HasComponent<ActorCapabilityState>(cmd.Passenger))
            {
                ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(cmd.Passenger);
                caps.Capabilities |= ActorCapabilities.CanMove | ActorCapabilities.CanShoot;
            }

            repo.RemoveComponent<IsEmbarkedTag>(cmd.Passenger);
        }
    }
}
