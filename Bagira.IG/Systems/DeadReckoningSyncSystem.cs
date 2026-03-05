using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Systems;

/// <summary>
/// Projects network position forward using network velocity and blends the render
/// transform toward the projected target for ghost entities.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class DeadReckoningSyncSystem : IModuleSystem
{
    private const float SmoothingRate = 10.0f;

    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query()
            .With<SimTransform>()
            .With<NetworkTransform>()
            .With<NetworkVelocity>()
            .With<NetworkAuthority>()
            .Build();

        var cmd = view.GetCommandBuffer();

        foreach (var entity in query)
        {
            ref readonly var authority = ref view.GetComponentRO<NetworkAuthority>(entity);
            if (authority.HasAuthority)
                continue;

            ref readonly var netTf  = ref view.GetComponentRO<NetworkTransform>(entity);
            ref readonly var netVel = ref view.GetComponentRO<NetworkVelocity>(entity);
            ref readonly var simTf  = ref view.GetComponentRO<SimTransform>(entity);

            var projectedNetPos = netTf.LastPosition + (netVel.Value * deltaTime);
            cmd.SetComponent(entity, new NetworkTransform { LastPosition = projectedNetPos, LastRotation = netTf.LastRotation });

            var blendedPos = Vector3.Lerp(simTf.Position, projectedNetPos, deltaTime * SmoothingRate);
            cmd.SetComponent(entity, new SimTransform
            {
                Position = blendedPos,
                Rotation = simTf.Rotation
            });

            cmd.SetComponent(entity, new SimVelocity { Linear = netVel.Value });
        }
    }
}
