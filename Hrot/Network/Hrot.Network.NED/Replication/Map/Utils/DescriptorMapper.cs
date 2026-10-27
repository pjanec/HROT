using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Modules.Geographic;
using Fdp.Core.Serialization;

namespace Hrot.Map.Common.Replication.Utils
{
    /// <summary>
    /// Converts a <c>List&lt;EntityDescriptorUnion&gt;</c> from a DDS <c>CreateEntityRequest</c>
    /// into a <c>List&lt;object&gt;</c> suitable for <c>SpawnEntityCommand.InitialComponents</c>.
    /// </summary>
    public static class DescriptorMapper
    {
        // Marker type for FdpLog (static classes cannot be used as generic type arguments)
        private sealed class Log { }

        /// <summary>
        /// Searches <paramref name="descriptors"/> for an <c>EntityMaster</c> entry and returns
        /// its <c>TkbType</c> and <c>DisType</c>.
        /// </summary>
        /// <returns>The TkbType found, or <c>0</c> if no EntityMaster descriptor is present.</returns>
        public static long ExtractTkbType(List<EntityDescriptorUnion>? descriptors, out ulong disType)
        {
            disType = 0;
            if (descriptors == null)
                return 0;

            foreach (var d in descriptors)
            {
                if (d._d == EDescriptorType.dtEntityMaster)
                {
                    var dt = d.EntityMaster.DisType;
                    disType
                        = ((ulong)dt.Kind        << 56)
                        | ((ulong)dt.Domain      << 48)
                        | ((ulong)dt.Country     << 32)
                        | ((ulong)dt.Category    << 24)
                        | ((ulong)dt.Subcategory << 16)
                        | ((ulong)dt.Specific    <<  8)
                        |  (ulong)dt.Extra;
                    return d.EntityMaster.TkbType;
                }
            }

            return 0;
        }

        /// <summary>
        /// Converts each descriptor in <paramref name="descriptors"/> to one or more ECS component
        /// instances to be placed in <c>SpawnEntityCommand.InitialComponents</c>.
        /// </summary>
        /// <param name="descriptors">The descriptor list from the incoming DDS request.</param>
        /// <remarks>⭐ AQ107 B / CE-4073: every position on the wire is local ENU metres, so a <c>dtWorldPos</c> descriptor
        /// always yields a <see cref="SimTransform"/> by a plain copy (<see cref="WorldPosCodec"/>) — there is no geographic
        /// transform to pass. VehicleState is NOT added here — it is the responsibility of the TKB template.</remarks>
        public static List<object> MapToComponents(
            List<EntityDescriptorUnion>? descriptors)
        {
            var result = new List<object>();

            if (descriptors == null)
                return result;

            // Pre-pass: extract the entity's own position (the WorldPos descriptor) so dtMapVisualOverlay's ABSOLUTE local points
            // can be made entity-relative — the polyline the ECS holds is relative to SimTransform.Position.
            Vector3? centroid = null;
            foreach (var d in descriptors)
            {
                if (d._d == EDescriptorType.dtWorldPos)
                {
                    centroid = WorldPosCodec.Position(d.WorldPos);
                    break;
                }
            }

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        break;

                    case EDescriptorType.dtAreaEffectCloud:   // ⭐ CE-3228 — a forwarded request carries its cloud
                        result.Add(Hrot.Map.Common.Replication.Ingress.EntityAreaEffectCloudIngressTranslator.FromSample(in d.AreaEffectCloud));
                        break;

                    case EDescriptorType.dtEntityInfo:
                        result.Add(new Fdp.Core.EntityInfo
                        {
                            Name = d.EntityInfo.Name,
                            ForceId = ForceIdMapping.FromWire(d.EntityInfo.ForceIdentifier),
                        });
                        break;

                    case EDescriptorType.dtWorldPos:
                        // ⭐ AQ107 B / D — a plain copy of the local-ENU pose: position, the quaternion decoded from its vector part
                        //   (⛔ no Euler conversion — the three Euler routes this arm once had are gone with the Euler wire), velocity.
                        result.Add(new SimTransform
                        {
                            Position = WorldPosCodec.Position(d.WorldPos),
                            Rotation = WorldPosCodec.Rotation(d.WorldPos),
                        });

                        // Apply velocity if present (non-zero linear or angular).
                        if (WorldPosCodec.HasVelocity(d.WorldPos))
                        {
                            result.Add(new SimVelocity
                            {
                                Linear  = WorldPosCodec.LinearVelocity(d.WorldPos),
                                Angular = WorldPosCodec.AngularVelocity(d.WorldPos),
                            });
                        }
                        break;

                    case EDescriptorType.dtMapRoute:
                        // Build a RoutePlan component from the incoming waypoints.
                        // Silently skip if Points is null/empty.
                        if (d.MapRoute.Points != null)
                        {
                            var routePlan = new RoutePlan { IsLoop = d.MapRoute.IsLoop }.WithWaypoints(wps =>
                            {
                                foreach (var wp in d.MapRoute.Points)
                                {
                                    wps.Add(new RouteWaypoint
                                    {
                                        Position      = EnuWire.FromWire(wp.Position),   // ⭐ AQ107 B — local ENU, copied
                                        TargetSpeed   = (float)wp.SpeedMetersPerSec,
                                        ExtensionJson = string.IsNullOrEmpty(wp.ExtensionJson) ? null : wp.ExtensionJson,
                                        Kind          = (Fdp.Toolkit.Navigation.RouteWaypointKind)wp.Kind,   // ⭐ Air Ops B1
                                        HeightAgl     = wp.HeightAgl,
                                        Hold          = (Fdp.Toolkit.Navigation.RouteHold)wp.Hold,
                                        HoldSeconds   = wp.HoldSeconds,
                                        HoldUnit      = wp.HoldUnit,
                                    });
                                }
                            });
                            result.Add(routePlan);
                        }
                        break;

                    case EDescriptorType.dtMapVisualOverlay:
                        var polylinePoints = new List<Vector2>();   // CE-4046 — EditablePolyline is an immutable record
                        if (d.MapVisualOverlay.Points != null)
                        {
                            // ⭐ AQ107 B — Points on the wire are ABSOLUTE local ENU metres. The ECS polyline is relative to the entity's
                            //   own position: subtract the WorldPos descriptor's position (no WorldPos in the request ⇒ the entity sits at
                            //   the local origin, so the points ARE the offsets).
                            var reference = centroid ?? Vector3.Zero;
                            polylinePoints.Capacity = d.MapVisualOverlay.Points.Count;
                            foreach (var pt in d.MapVisualOverlay.Points)
                            {
                                var abs = EnuWire.FromWire(pt);
                                polylinePoints.Add(new Vector2(abs.X - reference.X, abs.Y - reference.Y));
                            }
                        }

                        result.Add(new EditablePolyline { Points = polylinePoints });
                        break;

                    default:
                        FdpLog<Log>.Warn(
                            "[DescriptorMapper] Unhandled descriptor type: {0} — skipping.", d._d);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Converts each descriptor in <paramref name="descriptors"/> to ECS component instances,
        /// using the shared <paramref name="compiler"/> routing delegates for
        /// <c>dtEntityInfo</c> (Name, Affiliation) and
        /// <c>dtWorldPos</c> (pose via <see cref="ApplyGeoSpatialDescriptor"/>).
        /// </summary>
        /// <remarks>
        /// This overload is the Phase 6 "unified routing" path (ATTR-S6T1, ATTR-S6T2).
        /// The existing <see cref="MapToComponents(List{EntityDescriptorUnion})"/>
        /// overload is retained for backward compatibility.
        /// </remarks>
        /// <param name="descriptors">The descriptor list from the incoming DDS request.</param>
        /// <param name="compiler">
        /// Shared <see cref="JsonAttributeCompiler"/> used to apply Name and Affiliation via the
        /// same routing table as the JSON attribute patch path.
        /// When <c>null</c> or no compiler is provided, falls back to direct field assignment.
        /// </param>
        public static List<object> MapToComponents(
            List<EntityDescriptorUnion>? descriptors,
            JsonAttributeCompiler? compiler)
        {
            if (compiler == null)
                return MapToComponents(descriptors);

            var result = new List<object>();

            if (descriptors == null)
                return result;

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        break;

                    case EDescriptorType.dtAreaEffectCloud:   // ⭐ CE-3228 — a forwarded request carries its cloud
                        result.Add(Hrot.Map.Common.Replication.Ingress.EntityAreaEffectCloudIngressTranslator.FromSample(in d.AreaEffectCloud));
                        break;

                    case EDescriptorType.dtEntityInfo:
                    {
                        // Use the shared compiler routing table for Name and Affiliation so that
                        // the same delegates that process JSON patches also handle descriptor data.
                        var ctx = new ListPatchContext(result);

                        // Build a minimal JSON string with proper string escaping.
                        string escapedName = JsonSerializer.Serialize(d.EntityInfo.Name ?? string.Empty, FdpJsonOptionsRegistry.DefaultRelaxed);
                        string affStr      = ForceIdentifierToAffiliationString(d.EntityInfo.ForceIdentifier);
                        compiler.Compile(
                            $"{{\"Name\":{escapedName},\"Affiliation\":\"{affStr}\"}}",
                            ctx);

                        // CommanderId is not in the JSON schema; handled via UnitSubordinate component.
                        result = ctx.FlushComponents();
                        break;
                    }

                    case EDescriptorType.dtWorldPos:
                    {
                        var ctx = new ListPatchContext(result);
                        ApplyGeoSpatialDescriptor(ctx, d.WorldPos);
                        result = ctx.FlushComponents();
                        if (WorldPosCodec.HasVelocity(d.WorldPos))
                        {
                            result.Add(new SimVelocity
                            {
                                Linear  = WorldPosCodec.LinearVelocity(d.WorldPos),
                                Angular = WorldPosCodec.AngularVelocity(d.WorldPos),
                            });
                        }
                        break;
                    }

                    default:
                        FdpLog<Log>.Warn(
                            "[DescriptorMapper] Unhandled descriptor type: {0} — skipping.", d._d);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Applies a <c>dtWorldPos</c> descriptor's pose to a <see cref="SimTransform"/> via the provided
        /// <see cref="ListPatchContext"/>: <c>Position</c> (local ENU, copied) and <c>Rotation</c> (the quaternion decoded from its
        /// vector part) — ⭐ AQ107 B / D, <see cref="WorldPosCodec"/>.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b><c>Q59-C1</c> — ATTR-BATCH-03's TODO HAS BEEN DISCHARGED.</b> It said: *"If new JSON path
        /// delegates are added for dtWorldPos (e.g. "Heading"), this method MUST be updated to maintain
        /// convergence between the descriptor-union route and the JSON-string route."*
        /// ⇒ <c>AX-018</c> registered exactly that delegate, so this method sets <c>Rotation</c> as well as <c>Position</c>.
        /// ⭐ Both routes end in the same <c>SimTransform</c>: the JSON route converts its lat/lon/heading attributes, this route copies
        /// the wire's local-ENU pose.
        /// </remarks>
        /// <param name="ctx">The patch context to apply the transform into.</param>
        /// <param name="geo">The pose sample (local ENU).</param>
        public static void ApplyGeoSpatialDescriptor(ListPatchContext ctx, WorldPos geo)
        {
            ref SimTransform st = ref ctx.GetUnmanagedComponent<SimTransform>();
            st.Position = WorldPosCodec.Position(in geo);
            st.Rotation = WorldPosCodec.Rotation(in geo);
        }

        /// <summary>
        /// Converts a wire-level <see cref="eForceIdentifier"/> to the JSON affiliation string
        /// expected by the <c>"Affiliation"</c> route in the shared
        /// <see cref="JsonAttributeCompiler"/>.
        /// </summary>
        private static string ForceIdentifierToAffiliationString(eForceIdentifier id) =>
            id switch
            {
                eForceIdentifier.FORCE_FRIENDLY => "FORCE_FRIENDLY",
                eForceIdentifier.FORCE_OPPOSING => "FORCE_OPPOSING",
                eForceIdentifier.FORCE_NEUTRAL  => "FORCE_NEUTRAL",
                _                               => "FORCE_UNKNOWN",
            };
    }
}
