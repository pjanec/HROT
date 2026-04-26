using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes zone authoring commands in the editor world.
/// <list type="bullet">
///   <item><see cref="SpawnZoneObstacleCommand"/> — creates a new obstacle entity with
///   <see cref="SimTransform"/>, <see cref="PhysicsCollider"/>, and <see cref="ZoneMembership"/>.</item>
///   <item><see cref="UpdateZoneConfigCommand"/> — loads a road-network blob from the
///   supplied JSON path and stores it as <see cref="ZoneEnvironmentData"/> singleton.</item>
/// </list>
/// When a <see cref="ZoneManagerService"/> is provided, both commands also update the
/// service's active-zone tracking so that <c>ScenarioFileService.SaveScenario</c> persists
/// the correct zone DTO data.
/// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EditorZoneAuthoringSystem : IEcsModuleSystem
{
    private readonly ZoneManagerService? _zoneService;
    private readonly Dictionary<string, ZoneDefinitionDto> _dtos = new();

    /// <summary>
    /// Initialises the system with an optional <see cref="ZoneManagerService"/> for
    /// zone-DTO tracking during save.
    /// </summary>
    public EditorZoneAuthoringSystem(ZoneManagerService? zoneService = null)
    {
        _zoneService = zoneService;
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        ProcessObstacles(view);
        ProcessZoneConfig(view);
    }

    private void ProcessObstacles(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        foreach (var cmd in view.ReadManagedEvents<SpawnZoneObstacleCommand>())
        {
            var entity = repo.CreateEntity();

            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f),
            });

            repo.AddComponent(entity, new PhysicsCollider
            {
                Radius         = cmd.Radius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });

            repo.AddComponent(entity, new ZoneMembership { ZoneName = cmd.ZoneName });

            // Mirror to zone DTO tracking for save pipeline.
            if (_zoneService != null)
            {
                if (!_dtos.TryGetValue(cmd.ZoneName, out var dto))
                    _dtos[cmd.ZoneName] = dto = new ZoneDefinitionDto();

                dto.Obstacles ??= new List<ZoneObstacleDto>();
                dto.Obstacles.Add(new ZoneObstacleDto
                {
                    X      = cmd.Position.X,
                    Y      = cmd.Position.Y,
                    Radius = cmd.Radius,
                });

                _zoneService.SetActiveZones(_dtos);
            }
        }
    }

    private void ProcessZoneConfig(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        foreach (var cmd in view.ReadManagedEvents<UpdateZoneConfigCommand>())
        {
            if (string.IsNullOrEmpty(cmd.RoadNetworkPath)) continue;

            var blob = RoadNetworkLoader.LoadFromJson(cmd.RoadNetworkPath);
            repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob });

            // Mirror to zone DTO tracking for save pipeline.
            if (_zoneService != null)
            {
                if (!_dtos.TryGetValue(cmd.ZoneName, out var dto))
                    _dtos[cmd.ZoneName] = dto = new ZoneDefinitionDto();

                dto.RoadNetworkPath = cmd.RoadNetworkPath;
                _zoneService.SetActiveZones(_dtos);
            }
        }
    }
}
