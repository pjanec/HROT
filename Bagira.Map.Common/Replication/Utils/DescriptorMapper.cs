using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;

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
                    disType = d.EntityMaster.DisType;
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

            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        break;

                    case EDescriptorType.dtEntityInfo:
                        result.Add(new IgEntityData
                        {
                            Name = d.EntityInfo.Name,
                            ForceId = (ForceId)(int)d.EntityInfo.ForceIdentifier,
                            CommanderId = d.EntityInfo.CommanderId
                        });
                        break;

                    case EDescriptorType.dtGeoSpatial:
                        // Produce a SimTransform for spatial placement if a geo transform is available.
                        // NOTE: VehicleState is intentionally NOT added here. It is only valid for
                        // wheeled entities and must be added exclusively by the TKB template to avoid
                        // breaking LinearKinematicsSystem for infantry and aircraft.
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

                        result.Add(new GeoTransform
                        {
                            Latitude = d.GeoSpatial.Pos.Latitude,
                            Longitude = d.GeoSpatial.Pos.Longitude,
                            Altitude = (float)d.GeoSpatial.Pos.Altitude,
                            HeadingDeg = d.GeoSpatial.Rot.Heading,
                            PitchDeg = d.GeoSpatial.Rot.Pitch,
                            RollDeg = d.GeoSpatial.Rot.Roll
                        });
                        break;

                    case EDescriptorType.dtGeoSpatialDR:
                        result.Add(new GeoVelocity
                        {
                            Linear = Dal3ToEnu(d.GeoSpatialDR.Vel),
                            Accel = Dal3ToEnu(d.GeoSpatialDR.Acc),
                            Angular = new Vector3(
                                d.GeoSpatialDR.RotVel.Roll * (MathF.PI / 180f),
                                d.GeoSpatialDR.RotVel.Pitch * (MathF.PI / 180f),
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
