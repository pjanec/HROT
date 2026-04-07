using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.Common.Systems;

/// <summary>
/// Projects network position forward using network velocity and blends the render
/// transform toward the projected target for ghost entities.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class DeadReckoningSyncSystem : IEcsModuleSystem
{
    private const float SmoothingRate = 10.0f;

    /// <summary>
    /// When <c>true</c> (default), dead-reckoning runs on all entities without local
    /// authority — suitable for pure ImageGenerator nodes where every entity is a remote replica.
    /// When <c>false</c>, only entities in <c>EntityLifecycle.Ghost</c> state are smoothed,
    /// preventing DR from fighting <c>GroundKinematicsModule</c> on locally-owned entities in
    /// combined Muscle+IG roles.
    /// </summary>
    public bool DriveFromNetwork { get; }

    /// <summary>Parameterless constructor — backward-compatible; <see cref="DriveFromNetwork"/> defaults to <c>true</c>.</summary>
    public DeadReckoningSyncSystem() : this(driveFromNetwork: true) { }

    /// <summary>Explicit constructor for combined-role nodes (e.g. MuscleGround | ImageGenerator).</summary>
    /// <param name="driveFromNetwork">
    /// Pass <c>false</c> when the node also owns entities locally, so DR only processes ghost entities.
    /// </param>
    public DeadReckoningSyncSystem(bool driveFromNetwork)
    {
        DriveFromNetwork = driveFromNetwork;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        var queryBuilder = view.Query()
            .With<SimTransform>()
            .With<NetworkTransform>()
            .With<NetworkVelocity>()
            .With<NetworkAuthority>();

        if (!DriveFromNetwork)
            queryBuilder = queryBuilder.WithLifecycle(EntityLifecycle.Ghost);

        var query = queryBuilder.Build();

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
