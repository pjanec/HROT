using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// CE-4214 - the ONE way a host builds the waypoint editor panel, so the pick can never lack its GUID lookup: the host passes its world and
/// entity map (lazily - a host builds its windows before its node context exists) and the factory supplies <see cref="WaypointEditorPanel.EntityGuidOf"/>.
/// </summary>
public static class WaypointEditorPanelFactory
{
    public static WaypointEditorPanel Create(Func<EntityRepository?> world, Func<NetworkEntityMap?> entityMap)
        => new(() => RouteWaypointGizmo.Current) { EntityGuidOf = id => GuidOf(world(), entityMap(), id) };

    /// <summary>The <see cref="EntityGuid"/> of the entity with network id <paramref name="networkId"/>, or null (no map, not alive, no key).</summary>
    public static Guid? GuidOf(EntityRepository? world, NetworkEntityMap? map, int networkId)
        => world != null && map != null && map.TryGetEntity(networkId, out var e) && world.IsAlive(e) && world.HasComponent<EntityGuid>(e)
            ? world.GetComponentRO<EntityGuid>(e).Value : null;
}
