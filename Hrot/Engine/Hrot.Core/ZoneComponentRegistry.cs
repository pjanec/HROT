using Fdp.Core;

namespace Hrot.Map.Common;

/// <summary>
/// Registers the zone-authoring COMMANDS.
///
/// <para>⛔ <b>It no longer registers a component (F1, 2026-09-17).</b> Its one component,
/// <c>ZoneMembership</c>, recorded which named zone an obstacle belonged to. Zones and obstacles are both
/// ordinary entities now, so membership is geometry — does the zone polygon cover the obstacle? — rather
/// than a stored name that can drift from the shapes. Its component id (171) is burned, not reused.</para>
///
/// <para>⭐ The two COMMANDS stay: obstacle placement is a live authoring surface whose replacement (§9,
/// the zones view) is still OPEN. ⚠ <c>UpdateZoneConfigCommand</c> is registered but NO LONGER CONSUMED —
/// its road-network-from-a-path behaviour is superseded by the terrain asset (§2.1d). It is kept
/// registered rather than deleted because <c>EditorZoneAdapter</c> still publishes it and a publish to an
/// unregistered event type is worse than a publish nobody reads.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6 (retirement), §9 (still open).
/// </summary>
public static class ZoneComponentRegistry
{
    /// <summary>Registers the zone authoring command events.</summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedEvent<Hrot.Map.Common.Events.SpawnZoneObstacleCommand>();
        world.RegisterManagedEvent<Hrot.Map.Common.Events.UpdateZoneConfigCommand>();
    }
}
