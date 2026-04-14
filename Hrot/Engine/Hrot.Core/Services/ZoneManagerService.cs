using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;
using Fdp.Kernel;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common.Scenario;

namespace Hrot.Map.Common.Services;

/// <summary>
/// Default implementation of <see cref="IZoneManagerService"/>.
///
/// <para>
/// Acts as the ACL translation pivot: bridges application-layer DTOs
/// (<see cref="ZoneDefinitionDto"/>) to ECS state
/// (<c>ZoneEnvironmentData</c> singleton and <c>PhysicsCollider</c> entities).
/// </para>
/// </summary>
public sealed class ZoneManagerService : IZoneManagerService
{
    private Dictionary<string, ZoneDefinitionDto> _activeZones = new();

    /// <inheritdoc />
    public void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones)
    {
        if (repo   == null) throw new ArgumentNullException(nameof(repo));
        if (zones  == null) throw new ArgumentNullException(nameof(zones));

        // Ensure the component types are registered so AddComponent succeeds even in
        // unit-test repositories that have not gone through a full composition root.
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<PhysicsCollider>();

        foreach (var (_, zone) in zones)
        {
            // ── Road network ──────────────────────────────────────────────────
            if (zone.RoadNetworkPath != null)
            {
                // Memory safety: dispose the existing blob before overwriting.
                // Use ref var to operate on the stored struct directly (not a defensive copy).
                if (repo.HasSingleton<ZoneEnvironmentData>())
                {
                    ref var existingZed  = ref repo.GetSingleton<ZoneEnvironmentData>();
                    ref var existingRoad = ref existingZed.RoadNetwork;
                    existingRoad.Dispose();
                }

                var blob = RoadNetworkLoader.LoadFromJson(zone.RoadNetworkPath);
                repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob });
            }

            // ── Obstacles ─────────────────────────────────────────────────────
            if (zone.Obstacles == null) continue;

            foreach (var obs in zone.Obstacles)
            {
                var entity = repo.CreateEntity();
                repo.AddComponent(entity, new SimTransform
                {
                    Position = new Vector3(obs.X, obs.Y, 0f),
                });
                repo.AddComponent(entity, new PhysicsCollider
                {
                    Radius         = obs.Radius,
                    CollisionLayer = PhysicsConstants.EntityCollisionLayer,
                });
            }
        }

        _activeZones = new Dictionary<string, ZoneDefinitionDto>(zones);
    }

    /// <summary>
    /// Updates the active-zones tracking dictionary directly, without spawning ECS entities.
    /// Used by <c>EditorZoneAuthoringSystem</c> to mirror authoring commands into the save path.
    /// </summary>
    public void SetActiveZones(Dictionary<string, ZoneDefinitionDto> zones)
        => _activeZones = new Dictionary<string, ZoneDefinitionDto>(zones);

    /// <inheritdoc />
    public Dictionary<string, ZoneDefinitionDto> GetActiveZones() => _activeZones;
}
