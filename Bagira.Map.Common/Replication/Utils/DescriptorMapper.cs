using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common.Components;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Patching;
using Fdp.Modules.Geographic;

namespace Bagira.Map.Common.Replication.Utils
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
        /// <param name="geoTransform">
        /// Optional geographic transform. When provided, a <see cref="SimTransform"/> is
        /// generated for <c>dtGeoSpatial</c> descriptors so the entity starts at the correct
        /// Cartesian position. When <c>null</c>, no <c>SimTransform</c> is produced.
        /// VehicleState is NOT added here — it is the responsibility of the TKB template.
        /// </param>
        public static List<object> MapToComponents(
            List<EntityDescriptorUnion>? descriptors,
            IGeographicTransform? geoTransform)
        {
            var result = new List<object>();

            if (descriptors == null)
                return result;

            // Pre-pass: extract the GeoSpatial centroid so dtMapVisualOverlay can convert
            // relative-geo offsets to relative-Cartesian using the entity position as reference.
            Vector3? geoCentroid = null;
            if (geoTransform != null)
            {
                foreach (var d in descriptors)
                {
                    if (d._d == EDescriptorType.dtGeoSpatial)
                    {
                        var pos = d.GeoSpatial.Pos;
                        geoCentroid = geoTransform.ToCartesian(pos.Latitude, pos.Longitude, pos.Altitude);
                        break;
                    }
                }
            }

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        break;

                    case EDescriptorType.dtEntityInfo:
                        result.Add(new IG.Components.EntityInfo
                        {
                            Name = d.EntityInfo.Name,
                            ForceId = (ForceId)(int)d.EntityInfo.ForceIdentifier,
                            CommanderId = d.EntityInfo.CommanderId
                        });
                        break;

                    case EDescriptorType.dtGeoSpatial:
                        // Produce a SimTransform for spatial placement if a geo transform is available.
                        // NOTE: VehicleState is intentionally NOT added here.
                        if (geoTransform != null)
                        {
                            var pos = d.GeoSpatial.Pos;
                            var cart = geoTransform.ToCartesian(pos.Latitude, pos.Longitude, pos.Altitude);

                            var cartPos = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);
                            // Heading is degrees CW from North.
                            float headingRad = d.GeoSpatial.Rot.Heading * (MathF.PI / 180f);
                            // Yaw=-90 rotates North=(0,1) to East=(1,0) per right-handed convention.
                            var rot = Quaternion.CreateFromYawPitchRoll(-headingRad, 0, 0);

                            result.Add(new SimTransform
                            {
                                Position = cartPos,
                                Rotation = rot
                            });
                        }
                        break;

                    case EDescriptorType.dtGeoSpatialDR:
                        result.Add(new SimVelocity
                        {
                            Linear = Dal3ToEnu(d.GeoSpatialDR.Vel),
                            Angular = new Vector3(
                                d.GeoSpatialDR.RotVel.Roll * (MathF.PI / 180f),
                                d.GeoSpatialDR.RotVel.Pitch * (MathF.PI / 180f),
                                d.GeoSpatialDR.RotVel.Heading * (MathF.PI / 180f))
                        });
                        break;

                    case EDescriptorType.dtMapRoute:
                        // Build a RoutePlan component from the incoming waypoints.
                        // Silently skip if geoTransform is null or Points is null/empty.
                        if (geoTransform != null && d.MapRoute.Points != null)
                        {
                            var routePlan = new RoutePlan { IsLoop = d.MapRoute.IsLoop };
                            routePlan.Mutate(wps =>
                            {
                                foreach (var wp in d.MapRoute.Points)
                                {
                                    var cart = geoTransform.ToCartesian(
                                        wp.Position.Latitude,
                                        wp.Position.Longitude,
                                        wp.Position.Altitude);
                                    wps.Add(new RouteWaypoint
                                    {
                                        Position      = cart,
                                        TargetSpeed   = (float)wp.SpeedMetersPerSec,
                                        ExtensionJson = string.IsNullOrEmpty(wp.ExtensionJson) ? null : wp.ExtensionJson,
                                    });
                                }
                            });
                            result.Add(routePlan);
                        }
                        break;

                    case EDescriptorType.dtMapVisualOverlay:
                        var polyline = new EditablePolyline();
                        if (d.MapVisualOverlay.Points != null)
                        {
                            // Points on the wire are RELATIVE geo offsets (deltaLat, deltaLon, deltaAlt)
                            // measured from the entity's GeoSpatial centroid.
                            // Convert to relative Cartesian:
                            //   absCart = ToCartesian(centLat + dLat, centLon + dLon, centAlt + dAlt)
                            //   relCart = absCart - centroidCart
                            polyline.Points = new List<Vector2>(d.MapVisualOverlay.Points.Count);

                            if (geoTransform != null && geoCentroid.HasValue)
                            {
                                // Normal path: use GeoSpatial centroid as reference.
                                var (refLat, refLon, refAlt) = geoTransform.ToGeodetic(geoCentroid.Value);
                                foreach (var geoPt in d.MapVisualOverlay.Points)
                                {
                                    var absCart = geoTransform.ToCartesian(
                                        refLat + geoPt.Latitude,
                                        refLon + geoPt.Longitude,
                                        refAlt + geoPt.Altitude);
                                    var relCart = absCart - geoCentroid.Value;
                                    polyline.Points.Add(new Vector2(relCart.X, relCart.Y));
                                }
                            }
                            else if (geoTransform != null)
                            {
                                // Fallback: no paired GeoSpatial descriptor; treat delta-geo as
                                // offsets from the world origin (legacy behaviour).
                                var origin = geoTransform.ToCartesian(0.0, 0.0, 0.0);
                                foreach (var geoPt in d.MapVisualOverlay.Points)
                                {
                                    var absCart = geoTransform.ToCartesian(geoPt.Latitude, geoPt.Longitude, geoPt.Altitude);
                                    var relCart = absCart - origin;
                                    polyline.Points.Add(new Vector2(relCart.X, relCart.Y));
                                }
                            }
                            else
                            {
                                foreach (var geoPt in d.MapVisualOverlay.Points)
                                    polyline.Points.Add(new Vector2((float)geoPt.Longitude, (float)geoPt.Latitude));
                            }
                        }

                        result.Add(polyline);
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
        /// <c>dtGeoSpatial</c> (position via <see cref="ApplyGeoSpatialDescriptor"/>).
        /// </summary>
        /// <remarks>
        /// This overload is the Phase 6 "unified routing" path (ATTR-S6T1, ATTR-S6T2).
        /// The existing <see cref="MapToComponents(List{EntityDescriptorUnion},IGeographicTransform)"/>
        /// overload is retained for backward compatibility.
        /// </remarks>
        /// <param name="descriptors">The descriptor list from the incoming DDS request.</param>
        /// <param name="geoTransform">
        /// Optional geographic transform. Required for <c>dtGeoSpatial</c> coordinate conversion.
        /// </param>
        /// <param name="compiler">
        /// Shared <see cref="JsonAttributeCompiler"/> used to apply Name and Affiliation via the
        /// same routing table as the JSON attribute patch path.
        /// When <c>null</c> or no compiler is provided, falls back to direct field assignment.
        /// </param>
        public static List<object> MapToComponents(
            List<EntityDescriptorUnion>? descriptors,
            IGeographicTransform? geoTransform,
            JsonAttributeCompiler? compiler)
        {
            if (compiler == null)
                return MapToComponents(descriptors, geoTransform);

            var result = new List<object>();

            if (descriptors == null)
                return result;

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        break;

                    case EDescriptorType.dtEntityInfo:
                    {
                        // Use the shared compiler routing table for Name and Affiliation so that
                        // the same delegates that process JSON patches also handle descriptor data.
                        var ctx = new ListPatchContext(result);

                        // Build a minimal JSON string with proper string escaping.
                        string escapedName = JsonSerializer.Serialize(d.EntityInfo.Name ?? string.Empty);
                        string affStr      = ForceIdentifierToAffiliationString(d.EntityInfo.ForceIdentifier);
                        compiler.Compile(
                            $"{{\"Name\":{escapedName},\"Affiliation\":\"{affStr}\"}}",
                            ctx);

                        // CommanderId is not in the JSON schema; set directly.
                        ref var ei = ref ctx.GetUnmanagedComponent<IG.Components.EntityInfo>();
                        ei.CommanderId = d.EntityInfo.CommanderId;

                        result = ctx.FlushComponents();
                        break;
                    }

                    case EDescriptorType.dtGeoSpatial:
                        if (geoTransform != null)
                        {
                            var ctx = new ListPatchContext(result);
                            ApplyGeoSpatialDescriptor(ctx, d.GeoSpatial, geoTransform);
                            result = ctx.FlushComponents();
                        }
                        break;

                    case EDescriptorType.dtGeoSpatialDR:
                        result.Add(new SimVelocity
                        {
                            Linear = Dal3ToEnu(d.GeoSpatialDR.Vel),
                            Angular = new Vector3(
                                d.GeoSpatialDR.RotVel.Roll    * (MathF.PI / 180f),
                                d.GeoSpatialDR.RotVel.Pitch   * (MathF.PI / 180f),
                                d.GeoSpatialDR.RotVel.Heading * (MathF.PI / 180f))
                        });
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
        /// Applies the coordinate conversion from a <c>dtGeoSpatial</c> descriptor to a
        /// <see cref="SimTransform"/> via the provided <see cref="ListPatchContext"/>.
        /// Sets <c>Position</c> only (no rotation) so the result aligns with the JSON path
        /// delegates registered for <c>GeoPosition.Latitude/Longitude/Altitude</c>.
        /// </summary>
        /// <remarks>
        /// TODO ATTR-BATCH-03: If new JSON path delegates are added for dtGeoSpatial (e.g. "Heading"),
        /// this method MUST be updated to maintain convergence between the descriptor-union route
        /// and the JSON-string route. Enforced currently by DescriptorMapper_GeoSpatial_SharedDelegate_ProducesSameResultAsDirectPath.
        /// </remarks>
        /// <param name="ctx">The patch context to apply the transform into.</param>
        /// <param name="geo">The geodetic position source.</param>
        /// <param name="geoTransform">Transform used to convert geodetic → Cartesian.</param>
        public static void ApplyGeoSpatialDescriptor(
            ListPatchContext ctx,
            GeoSpatial geo,
            IGeographicTransform geoTransform)
        {
            var pos  = geo.Pos;
            var cart = geoTransform.ToCartesian(pos.Latitude, pos.Longitude, pos.Altitude);

            ref SimTransform st = ref ctx.GetUnmanagedComponent<SimTransform>();
            st.Position = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);
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

        /// <summary>
        /// Converts a compass heading (degrees, clockwise from North) to a normalised 2-D forward
        /// vector in the local XY plane (X = East, Y = North).
        /// </summary>
        private static Vector2 HeadingToVector(float headingDeg)
        {
            float rad = headingDeg * (MathF.PI / 180f);
            // North = +Y, East = +X
            return new Vector2(MathF.Sin(rad), MathF.Cos(rad));
        }

        private static Vector3 Dal3ToEnu(in DAL3 dal3)
        {
            float elevRad = dal3.Elevation * (MathF.PI / 180f);
            float horizontal = dal3.Length * MathF.Cos(elevRad);
            var dir = HeadingToVector(dal3.Azimuth);
            float up = dal3.Length * MathF.Sin(elevRad);
            return new Vector3(dir.X * horizontal, dir.Y * horizontal, up);
        }
    }
}
