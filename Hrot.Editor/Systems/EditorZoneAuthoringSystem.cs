using System.Numerics;
using CarKinem.Road;
using Fdp.Kernel;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes zone authoring commands in the editor world.
/// <list type="bullet">
///   <item><see cref="SpawnZoneObstacleCommand"/> — creates a new obstacle entity with
///   <see cref="SimTransform"/>, <see cref="PhysicsCollider"/>, and <see cref="ZoneMembership"/>.</item>
///   <item><see cref="UpdateZoneConfigCommand"/> — loads a road-network blob from the
///   supplied JSON path and stores it as <see cref="ZoneEnvironmentData"/> singleton.</item>
/// </list>
/// </summary>
public sealed class EditorZoneAuthoringSystem : ComponentSystem
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        ProcessObstacles();
        ProcessZoneConfig();
    }

    private void ProcessObstacles()
    {
        foreach (var cmd in World.Bus.ConsumeManaged<SpawnZoneObstacleCommand>())
        {
            var entity = World.CreateEntity();

            World.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f),
            });

            World.AddComponent(entity, new PhysicsCollider
            {
                Radius         = cmd.Radius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });

            World.AddComponent(entity, new ZoneMembership { ZoneName = cmd.ZoneName });
        }
    }

    private void ProcessZoneConfig()
    {
        foreach (var cmd in World.Bus.ConsumeManaged<UpdateZoneConfigCommand>())
        {
            if (string.IsNullOrEmpty(cmd.RoadNetworkPath)) continue;

            var blob = RoadNetworkLoader.LoadFromJson(cmd.RoadNetworkPath);
            World.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob });
        }
    }
}
