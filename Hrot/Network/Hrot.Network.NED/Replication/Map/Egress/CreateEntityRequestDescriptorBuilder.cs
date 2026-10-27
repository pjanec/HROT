using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Utils;
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
            IGeographicTransform? geoTransform,   // ⭐ AQ107 B: no longer used — the wire is local ENU, copied. Kept so the callers keep their shape
            long preAllocatedNetworkId = 0)
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d           = EDescriptorType.dtEntityMaster,
                    // ⭐ CE-4024 — a pre-allocated network id rides the master's EntityId (0 = the processor allocates), as replication
                    //   carries every entity's id. 📌 Measured 2026-10-18: it was dropped, so a SUMO pedestrian converted on the traffic node
                    //   came back from CGF under a NEW id (AQ93 §15 K5 / R-4029 "keeps its network id" held only in the editor).
                    EntityMaster = new EntityMaster { TkbType = tkbType,
                                                   EntityId = preAllocatedNetworkId > 0 ? preAllocatedNetworkId : 0 },
                },
                new EntityDescriptorUnion
                {
                    _d       = EDescriptorType.dtWorldPos,
                    // ⭐ AQ107 B — the anchor is local ENU, copied (no anchor ⇒ the local origin). No rotation: the all-zero
                    //   orientation is the identity.
                    WorldPos = new WorldPos
                    {
                        Position = EnuWire.ToWire(anchor ?? Vector3.Zero),
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
                    // ⭐ CE-3228 — a cloud rides the forwarded request (Entity Creation §3.4d; the wire form carries what the local one does)
                    else if (component is Fdp.Toolkit.Combat.AreaEffectCloud cloud)
                        descriptors.Add(new EntityDescriptorUnion
                        {
                            _d              = EDescriptorType.dtAreaEffectCloud,
                            AreaEffectCloud = Hrot.Map.Common.Replication.Egress.EntityAreaEffectCloudEgressTranslator.ToSample(0, cloud),
                        });
                }

                if (polyline != null)
                    descriptors.Add(BuildOverlayDescriptor(polyline, style, anchor));

                if (route != null)
                    descriptors.Add(BuildRouteDescriptor(route));
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
        /// and optional <see cref="MapOverlayStyle"/>. ⭐ AQ107 B — the entity-relative XY points become ABSOLUTE local ENU positions
        /// (the anchor + the offset; Z is the anchor's) — the wire's one meaning for a position. No anchor ⇒ the entity sits at the
        /// local origin and the points are already absolute.
        /// </summary>
        private static EntityDescriptorUnion BuildOverlayDescriptor(
            EditablePolyline polyline,
            MapOverlayStyle? style,
            Vector3? anchor)
        {
            var wirePoints = new List<Vec3d>(polyline.Points?.Count ?? 0);
            var origin = anchor ?? Vector3.Zero;

            if (polyline.Points != null)
            {
                foreach (var relPt in polyline.Points)
                    wirePoints.Add(EnuWire.ToWire(new Vector3(origin.X + relPt.X, origin.Y + relPt.Y, origin.Z)));
            }

            string styleJson = style.HasValue ? style.Value.ToJson() : string.Empty;

            return new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtMapVisualOverlay,
                MapVisualOverlay = new MapVisualOverlay
                {
                    PersistenceMode   = PersistenceMode.MODE_PERSISTENT,
                    Points            = wirePoints,
                    IsEditable        = true,
                    IsClickable       = true,
                    StyleOverrideJson = styleJson,
                },
            };
        }

        /// <summary>
        /// Builds a <c>dtMapRoute</c> descriptor from a <see cref="RoutePlan"/>.
        /// ⭐ AQ107 B — the waypoint positions (local ENU) go onto the wire as they are.
        /// </summary>
        private static EntityDescriptorUnion BuildRouteDescriptor(RoutePlan route)
        {
            var waypoints = new List<Waypoint>(route.Waypoints.Count);

            foreach (var wp in route.Waypoints)
            {
                waypoints.Add(new Waypoint
                {
                    Position = EnuWire.ToWire(wp.Position), SpeedMetersPerSec = wp.TargetSpeed,
                    Kind = (byte)wp.Kind, HeightAgl = wp.HeightAgl, Hold = (byte)wp.Hold,   // ⭐ Air Ops B1
                    HoldSeconds = wp.HoldSeconds, HoldUnit = wp.HoldUnit,
                });
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
