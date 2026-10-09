using System;
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
    private readonly Func<Hrot.Common.EntityCreation.EntityCreation?> _creation;

    /// <param name="creation">⭐ <c>CE-3141</c> — the node's creation pack (read late: the editor builds it after this module).
    /// ⛔ Null drops the click LOUDLY — a placement that silently does nothing is the defect this replaced.</param>
    public EditorZoneAuthoringSystem(Func<Hrot.Common.EntityCreation.EntityCreation?>? creation = null)
        => _creation = creation ?? (() => null);

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐ <c>CE-3141</c> / <c>CE-3136</c> P-7a O4 (R-242) — the click now REQUESTS a typed static obstacle, a
    /// <c>Concrete block</c>, through the creation pack: it gets a TKB type (so a save reloads it — it used to be a bare
    /// <c>SimTransform</c> + <c>PhysicsCollider</c> that the load path rejected with <c>TkbType = 0</c>), it replicates, and it is
    /// baked into every node's terrain like any obstacle. The radius slider becomes the block's footprint (a square of side 2r,
    /// 1 m high — the area the old circle covered). 📄 docs/DESIGN_Peek_And_Fire.md §9.2 O4.
    /// </remarks>
    public void Execute(ISimulationView view, float deltaTime)
    {
        foreach (var cmd in view.ReadManagedEvents<SpawnZoneObstacleCommand>())
        {
            var creation = _creation();
            if (creation == null)
            {
                Fdp.Core.Logging.FdpLog<EditorZoneAuthoringSystem>.Warn(
                    $"[Editor] Obstacle placement at ({cmd.Position.X:0.#}, {cmd.Position.Y:0.#}) DROPPED — this host has no entity-creation pack.");
                continue;
            }

            float side = MathF.Max(2f * cmd.Radius, 0.5f);
            creation.RequestEntityCreation(
                Hrot.Map.Common.TkbEntityTypes.Obstacle_ConcreteBlock,
                transform: new SimTransform { Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f), Rotation = Quaternion.Identity },
                initialComponents: new object[] { new Fdp.Toolkit.Terrain.ObstacleShape { Length = side, Width = side, Height = 1f } });
        }
    }
}
