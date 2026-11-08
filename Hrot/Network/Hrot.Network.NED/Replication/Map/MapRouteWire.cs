using System.Collections.Generic;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Replication.Utils;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Dds;

namespace Hrot.Map.Common.Replication;

/// <summary>
/// ⭐ CE-4284 — the ONE encoding of a <see cref="RoutePlan"/> as a <see cref="MapRoute"/> sample and back. Three users: the owner's publish
/// (<c>MapRouteEgressTranslator</c>), every node's apply (<c>MapRouteIngressTranslator</c>), and a non-owner's edit sent to the owner as an
/// <c>UpdateEntityDescriptorRequest(dtMapRoute)</c> (<c>UpdateEntityCommandEgressTranslator</c> → <c>UpdateEntityDescriptorRequestSystem</c>).
/// ⛔ Never encode a route anywhere else: the request and the topic must carry the same fields, or an edit made on a non-owner loses some.
/// </summary>
public static class MapRouteWire
{
    /// <summary>The sample for <paramref name="plan"/> of entity <paramref name="netId"/>. AQ107 B — positions are local ENU, copied.</summary>
    public static MapRoute ToSample(int netId, RoutePlan plan)
    {
        var waypoints = new List<Waypoint>(plan.Waypoints.Count);
        for (int i = 0; i < plan.Waypoints.Count; i++)
        {
            var wp = plan.Waypoints[i];
            waypoints.Add(new Waypoint
            {
                Position          = EnuWire.ToWire(wp.Position),
                Name              = string.Empty,
                SpeedMetersPerSec = wp.TargetSpeed,
                ExtensionJson     = wp.ExtensionJson ?? string.Empty,
                Kind              = (byte)wp.Kind,        // ⭐ Air Ops B1 — appended
                HeightAgl         = wp.HeightAgl,
                Hold              = (byte)wp.Hold,
                HoldSeconds       = wp.HoldSeconds,
                HoldUnit          = wp.HoldUnit.Ref.NetworkId,   // S9': the wire stays int
            });
        }
        return new MapRoute { EntityId = netId, Points = waypoints, IsLoop = plan.IsLoop, ExtensionJson = string.Empty };
    }

    /// <summary><paramref name="current"/> with the sample's loop flag and waypoints (a NEW record — CE-4046: <see cref="RoutePlan"/> is immutable).</summary>
    public static RoutePlan Apply(RoutePlan current, in MapRoute data)
    {
        var points = data.Points;
        return (current with { IsLoop = data.IsLoop }).WithWaypoints(wps =>
        {
            wps.Clear();
            if (points == null) return;
            foreach (var wp in points)
            {
                wps.Add(new RouteWaypoint
                {
                    Position      = EnuWire.FromWire(wp.Position),
                    TargetSpeed   = (float)wp.SpeedMetersPerSec,
                    ExtensionJson = string.IsNullOrEmpty(wp.ExtensionJson) ? null : wp.ExtensionJson,
                    Kind          = (Fdp.Toolkit.Navigation.RouteWaypointKind)wp.Kind,   // ⭐ Air Ops B1
                    HeightAgl     = wp.HeightAgl,
                    Hold          = (Fdp.Toolkit.Navigation.RouteHold)wp.Hold,
                    HoldSeconds   = wp.HoldSeconds,
                    HoldUnit      = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(wp.HoldUnit)),   // S9'
                });
            }
        });
    }
}
