using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using CarKinem.Core;
using FDP.Kernel.Logging;
using Fdp.Modules.Geographic;

namespace Bagira.SimHost.Util
{
    /// <summary>
    /// Converts a <c>List&lt;EntityDescriptorUnion&gt;</c> from a DDS <c>CreateEntityRequest</c>
    /// into a <c>List&lt;object&gt;</c> suitable for <c>SpawnEntityCommand.InitialComponents</c>.
    ///
    /// Design note: this class lives in SimHost (not in FDP.Toolkit.NetworkSpawning) because the
    /// toolkit deliberately has no dependency on <c>Bagira.DDS.DataModel</c>.  SimHost is
    /// responsible for bridging DDS-specific types to the generic object components that
    /// <c>EntityComponentReflector</c> applies to the ECS world.
    /// </summary>
    public static class DescriptorMapper
    {
        // Marker type for FdpLog (static classes cannot be used as generic type arguments)
        private sealed class Log { }
        /// <summary>
        /// Searches <paramref name="descriptors"/> for an <c>EntityMaster</c> entry and returns
        /// its <c>TkbType</c>.
        /// </summary>
        /// <returns>The TkbType found, or <c>0</c> if no EntityMaster descriptor is present.</returns>
        public static long ExtractTkbType(List<EntityDescriptorUnion>? descriptors)
        {
            if (descriptors == null)
                return 0;

            foreach (var d in descriptors)
                if (d._d == EDescriptorType.dtEntityMaster)
                    return d.EntityMaster.TkbType;

            return 0;
        }

        /// <summary>
        /// Converts each descriptor in <paramref name="descriptors"/> to one or more ECS component
        /// instances to be placed in <c>SpawnEntityCommand.InitialComponents</c>.
        /// </summary>
        /// <param name="descriptors">The descriptor list from the incoming DDS request.</param>
        /// <param name="geoTransform">
        /// Optional geographic transform. When provided, a <see cref="VehicleState"/> override
        /// is generated for <c>dtGeoSpatial</c> descriptors so CarKiem physics starts at the
        /// correct Cartesian position.  When <c>null</c>, the raw <c>GeoSpatial</c> component is
        /// still added but no <c>VehicleState</c> is produced.
        /// </param>
        public static List<object> MapToComponents(
            List<EntityDescriptorUnion>? descriptors,
            IGeographicTransform?        geoTransform)
        {
            var result = new List<object>();

            if (descriptors == null)
                return result;

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        // Use the DDS model type directly — it is already decorated with [FdpDescriptor]
                        // so AutoCycloneTranslator replicates it without any manual translator stubs.
                        result.Add(d.EntityMaster);
                        break;

                    case EDescriptorType.dtEntityInfo:
                        result.Add(d.EntityInfo);
                        break;

                    case EDescriptorType.dtGeoSpatial:
                        // Raw DDS component — replicated via AutoCycloneTranslator.
                        result.Add(d.GeoSpatial);

                        // Also produce a VehicleState for CarKinem physics if a geo transform is available.
                        if (geoTransform != null)
                        {
                            var pos = d.GeoSpatial.Pos;
                            var cart = geoTransform.ToCartesian(pos.Latitude, pos.Longitude, pos.Altitude);

                            result.Add(new VehicleState
                            {
                                Position   = new Vector2(cart.X, cart.Y),
                                Forward    = HeadingToVector(d.GeoSpatial.Rot.Heading),
                                Speed      = 0f,
                                SteerAngle = 0f,
                            });
                        }
                        break;

                    case EDescriptorType.dtGeoSpatialDR:
                        result.Add(d.GeoSpatialDR);
                        break;

                    default:
                        FdpLog<Log>.Warn(
                            $"[DescriptorMapper] Unhandled descriptor type: {d._d} — skipping.");
                        break;
                }
            }

            return result;
        }

        // ─── Private helpers ─────────────────────────────────────────────────

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
    }
}
