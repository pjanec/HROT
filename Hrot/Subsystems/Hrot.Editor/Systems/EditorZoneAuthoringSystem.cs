using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Hrot.Map.Common.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that turns an obstacle-placement click into an ENTITY.
///
/// <para>⭐⭐ <b>What F1 removed from it, and why the rest stayed.</b> This system had three jobs; two are
/// retired and one is not:</para>
/// <list type="number">
///   <item>⛔ <b>mirroring into <c>ZoneDefinitionDto</c>s for the save pipeline</b> — RETIRED. The DTO and
///     the embedded <c>Zones</c> section are gone; the obstacle entity IS the definition, and it is saved
///     by the ordinary gate like any other entity.</item>
///   <item>⛔ <b>stamping <c>ZoneMembership</c></b> — RETIRED. Zones are entities with polygons now, so
///     "which zone is this obstacle in" is geometry, not a stored name that can drift from it.</item>
///   <item>⛔ <b><c>UpdateZoneConfigCommand</c>: loading a road network from a path into
///     <c>ZoneEnvironmentData</c></b> — RETIRED, and it was a live hazard. It wrote the road-network
///     singleton DIRECTLY, bypassing <c>RoadNetworkHolder</c>, so a background solver holding a lease
///     could not see the new graph and the old one was never retired. The road network is declared by the
///     TERRAIN asset and published through the holder by the terrain loader (§2.1d, §6).</item>
/// </list>
///
/// <para>⭐ <b>Job 1 stayed because it is a SURFACE, not a duplicate mechanism.</b> Placing an obstacle on
/// the map is a live authoring affordance whose replacement (§9, the zones view) is still OPEN in the
/// design. Deleting the consumer would have left <c>EditorZoneAdapter</c> publishing a command nothing
/// reads — a click that silently does nothing. "No rush removals": route, do not delete.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1c (obstacles), §5.1, §6 (retirement), §9 (still open).
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class EditorZoneAuthoringSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
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
        }
    }
}
