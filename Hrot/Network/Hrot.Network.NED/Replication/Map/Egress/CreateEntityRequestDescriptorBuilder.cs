using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐⭐ <b>Builds the DDS <see cref="CreateEntityRequest"/> wire sample from the neutral shape of an
    /// entity-creation intent</b> — request id, TKB type, an optional anchor transform, the initial ECS
    /// components, and the attribute JSON.
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b — host (f) IG adoption.</para>
    ///
    /// <para>⭐ <b>Why this class exists.</b> The descriptor construction below was private to
    /// <c>SpawnEntityCommandEgressTranslator</c>, which reads the node-local <c>SpawnEntityCommand</c>
    /// ORDER — one level too low, and the cause of the double-spawn hazard. Retiring that translator must
    /// not lose the geometry it knew how to encode (<c>R-137</c>: a unification may not lose capability),
    /// so the knowledge moves here first and both the old translator and the new
    /// <c>NedEntityCreationRequestEgress</c> call it. The translator's existing tests therefore stand as
    /// the equivalence proof for the extraction.</para>
    ///
    /// <para>⚠ <b>The anchor.</b> <c>SpawnEntityCommand</c> carries an explicit
    /// <c>InitialTransform</c>; <c>EntityCreationRequest</c> does not — it conveys position as a
    /// <c>SimTransform</c> inside <c>InitialComponents</c>, which is the same convention
    /// <c>CreateEntityRequestSystem</c> already reads when it materialises an order. Callers pass whichever
    /// they hold; <see cref="ResolveAnchor"/> applies that convention.</para>
    /// </summary>
    public static class CreateEntityRequestDescriptorBuilder
    {
        /// <summary>
        /// Returns the anchor position for a creation intent: the explicit <paramref name="explicitTransform"/>
        /// when the caller has one, otherwise the first <see cref="SimTransform"/> found in
        /// <paramref name="initialComponents"/>. Mirrors <c>CreateEntityRequestSystem</c>'s own extraction.
        /// </summary>
        public static Vector3? ResolveAnchor(
            SimTransform? explicitTransform,
            IReadOnlyList<object>? initialComponents)
        {
            if (explicitTransform.HasValue)
                return explicitTransform.Value.Position;

            if (initialComponents != null)
            {
                foreach (var component in initialComponents)
                {
                    if (component is SimTransform st)
                        return st.Position;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the wire sample. <paramref name="requestId"/> is preserved when non-empty so the
        /// two-phase ACK can be correlated by the originating node.
        /// </summary>
        public static CreateEntityRequest Build(
            Guid requestId,
            long tkbType,
            string? initialAttributesJson,
            Vector3? anchor,
            IReadOnlyList<object>? initialComponents,
            IGeographicTransform? geoTransform)
        {
            double lat, lon, alt;

            if (geoTransform != null && anchor.HasValue)
            {
                (lat, lon, alt) = geoTransform.ToGeodetic(anchor.Value);
            }
            else if (anchor.HasValue)
            {
                // Offline / test mode: treat canvas XY as lat/lon directly.
                var pos = anchor.Value;
                lat = pos.Y;
                lon = pos.X;
                alt = 0.0;
            }
            else
            {
                lat = lon = alt = 0.0;
            }

            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d           = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster { TkbType = tkbType },
                },
                new EntityDescriptorUnion
                {
                    _d       = EDescriptorType.dtWorldPos,
                    WorldPos = new WorldPos
                    {
                        Pos = new GeoPoint
                        {
                            Latitude  = lat,
                            Longitude = lon,
                            Altitude  = alt,
                        },
                    },
                },
            };

            // Extract geometry descriptors from the initial components.
            if (initialComponents != null)
            {
                EditablePolyline? polyline = null;
                MapOverlayStyle?  style    = null;
                RoutePlan?        route    = null;

                foreach (var component in initialComponents)
                {
                    if      (component is EditablePolyline ep) polyline = ep;
                    else if (component is MapOverlayStyle  ms) style    = ms;
                    else if (component is RoutePlan        rp) route    = rp;
                }

                if (polyline != null)
                    descriptors.Add(BuildOverlayDescriptor(polyline, style, anchor, geoTransform));

                if (route != null)
                    descriptors.Add(BuildRouteDescriptor(route, geoTransform));
            }

            return new CreateEntityRequest
            {
                RequestId             = requestId == Guid.Empty ? Guid.NewGuid() : requestId,
                Owner                 = default,
                Flags                 = 0,
                InitialAttributesJson = initialAttributesJson,
                InitialDescriptors    = descriptors,
            };
        }

        /// <summary>
        /// Builds a <c>dtMapVisualOverlay</c> descriptor from an <see cref="EditablePolyline"/>
        /// and optional <see cref="MapOverlayStyle"/>. Entity-relative Cartesian XY points are
        /// converted to relative geodetic offsets from the entity anchor.
        /// </summary>
        private static EntityDescriptorUnion BuildOverlayDescriptor(
            EditablePolyline polyline,
            MapOverlayStyle? style,
            Vector3? anchor,
            IGeographicTransform? geoTransform)
        {
            var geoPoints = new List<GeoPoint>(polyline.Points?.Count ?? 0);

            if (polyline.Points != null)
            {
                foreach (var relPt in polyline.Points)
                {
                    GeoPoint deltaGeo;
                    if (geoTransform != null && anchor.HasValue)
                    {
                        // Convert entity-relative Cartesian XY to relative geodetic offset.
                        var absCart   = new Vector3(anchor.Value.X + relPt.X, anchor.Value.Y + relPt.Y, anchor.Value.Z);
                        var (absLat, absLon, absAlt) = geoTransform.ToGeodetic(absCart);
                        var (refLat, refLon, refAlt) = geoTransform.ToGeodetic(anchor.Value);
                        deltaGeo = new GeoPoint
                        {
                            Latitude  = absLat - refLat,
                            Longitude = absLon - refLon,
                            Altitude  = absAlt - refAlt,
                        };
                    }
                    else
                    {
                        // No geo-transform or no anchor: treat XY as lat/lon offsets directly.
                        deltaGeo = new GeoPoint { Latitude = relPt.Y, Longitude = relPt.X, Altitude = 0.0 };
                    }
                    geoPoints.Add(deltaGeo);
                }
            }

            string styleJson = style.HasValue ? style.Value.ToJson() : string.Empty;

            return new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtMapVisualOverlay,
                MapVisualOverlay = new MapVisualOverlay
                {
                    PersistenceMode   = PersistenceMode.MODE_PERSISTENT,
                    Points            = geoPoints,
                    IsEditable        = true,
                    IsClickable       = true,
                    StyleOverrideJson = styleJson,
                },
            };
        }

        /// <summary>
        /// Builds a <c>dtMapRoute</c> descriptor from a <see cref="RoutePlan"/>.
        /// Cartesian waypoint positions are converted to geodetic coordinates.
        /// </summary>
        private static EntityDescriptorUnion BuildRouteDescriptor(
            RoutePlan route,
            IGeographicTransform? geoTransform)
        {
            var waypoints = new List<Waypoint>(route.Waypoints.Count);

            foreach (var wp in route.Waypoints)
            {
                GeoPoint pos;
                if (geoTransform != null)
                {
                    var (wLat, wLon, wAlt) = geoTransform.ToGeodetic(wp.Position);
                    pos = new GeoPoint { Latitude = wLat, Longitude = wLon, Altitude = wAlt };
                }
                else
                {
                    // No geo-transform: treat Cartesian XYZ as lon/lat/alt.
                    pos = new GeoPoint { Latitude = wp.Position.Y, Longitude = wp.Position.X, Altitude = wp.Position.Z };
                }
                waypoints.Add(new Waypoint { Position = pos, SpeedMetersPerSec = wp.TargetSpeed });
            }

            return new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtMapRoute,
                MapRoute = new MapRoute
                {
                    Points = waypoints,
                    IsLoop = route.IsLoop,
                },
            };
        }
    }
}
