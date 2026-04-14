using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using Fdp.Interfaces;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Reads <see cref="SimTransform"/> + <see cref="SimVelocity"/> ECS components,
    /// converts them to geodetic coordinates on-the-fly via <see cref="IGeographicTransform"/>,
    /// and publishes the merged <see cref="WorldPos"/> DDS topic.
    /// </summary>
    public class GeoSpatialEgressTranslator : CycloneTranslator<WorldPos, WorldPos>
    {
        private readonly IGeographicTransform _geoTransform;
        private readonly HashSet<long> _tracedNetIds = new();
        private readonly long _localNodeId;

        private const long WorldPosOrdinal = (long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos;

        /// <summary>
        /// ECS component IDs that authority-gate this descriptor.
        /// SimTransform (0), NetworkTransform (52), NetworkVelocity (53).
        /// </summary>
        private static readonly IReadOnlyList<int> _targetIds = new int[]
        {
            GlobalComponentIds.SimTransform,
            GlobalComponentIds.NetworkTransform,
            GlobalComponentIds.NetworkVelocity,
        };

        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public GeoSpatialEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
            : base(participant, "WorldPos", ordinal: WorldPosOrdinal, entityMap)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _localNodeId = localNodeId;
        }

        /// <summary>
        /// Inbound decode is not used for authority nodes.
        /// </summary>
        protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
        {
        }

        /// <summary>
        /// Tombstones the <see cref="WorldPos"/> topic instance to prevent descriptor leaks.
        /// </summary>
        public override void Dispose(long networkEntityId)
        {
            base.Dispose(networkEntityId);
        }

        /// <summary>
        /// Scans all locally-owned entities with <see cref="SimTransform"/> and publishes
        /// <see cref="WorldPos"/> (position/orientation) and <see cref="GeoSpatialDR"/>
        /// (velocity/acceleration) to DDS, converting Cartesian to geodetic on the fly.
        ///
        /// <para>
        /// Change-detection is performed by comparing the live <see cref="SimTransform"/> against
        /// the <see cref="NetworkTransform"/> shadow component, which stores the position and
        /// rotation that were last sent to the network.  A packet is sent when:
        /// <list type="bullet">
        ///   <item>The entity has moved more than 1 cmÂ˛ (Position threshold).</item>
        ///   <item>The entity has rotated by more than ~0.5Â° (Quaternion dot threshold).</item>
        ///   <item>A salted 600-tick heartbeat fires (UDP loss recovery).</item>
        /// </list>
        /// This bypass of <c>SmartEgressUtil</c> keeps the hot path entirely in unmanaged
        /// memory and avoids the Dictionary/HashSet lookups of <see cref="EgressPublicationState"/>.
        /// </para>
        /// </summary>
        public override void ScanAndPublish(ISimulationView view)
        {
            // GetComponentRW requires EntityRepository (concrete write access).
            // ScanAndPublish is only called from the egress system which always
            // supplies the live world; bail out safely if this ever changes.
            if (view is not EntityRepository repo) return;

            // Only entities that have a NetworkTransform shadow can participate in
            // change-detection egress.  Entities spawned through NedTkbBuilder always
            // receive this component; older/test entities without it are skipped.
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkTransform>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            const float PositionThresholdSq = 0.0001f; // 1 cmÂ˛  â€” avoids spurious sends from float noise
            const float RotationDotThreshold = 0.9999f; // ~0.5Â° arc â€” Quaternion.Dot == 1 when identical
            const uint  HeartbeatInterval   = 600;      // 10 s at 60 Hz for UDP loss recovery

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Authority check: only publish if this node owns geospatial data for this entity.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);
                ref var          netTf = ref repo.GetComponentRW<NetworkTransform>(entity);

                // Shadow comparison â€” entirely in unmanaged memory, no heap allocations.
                bool hasMoved   = Vector3.DistanceSquared(simTf.Position, netTf.LastPosition) > PositionThresholdSq;
                bool hasRotated = Math.Abs(Quaternion.Dot(simTf.Rotation, netTf.LastRotation)) < RotationDotThreshold;

                // Salted heartbeat: stagger entities by index so they don't all fire on tick 0.
                uint salt      = (uint)(entity.Index % HeartbeatInterval);
                bool heartbeat = ((view.Tick + salt) % HeartbeatInterval) == 0;

                if (!hasMoved && !hasRotated && !heartbeat)
                    continue;

                // Update shadow before publishing so the next tick comparison is against the
                // just-sent values, not stale pre-move values.
                netTf.LastPosition = simTf.Position;
                netTf.LastRotation = simTf.Rotation;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // Direct conversion: SimTransform (Cartesian) â†’ WorldPos (Geodetic)
                var (lat, lon, alt) = _geoTransform.ToGeodetic(simTf.Position);
                float heading = SimTransformBridgeSystem.RotationToHeadingDeg(simTf.Rotation);
                SimTransformBridgeSystem.RotationToPitchRollDeg(simTf.Rotation, out float pitch, out float roll);

                AngularVector vel = default;
                AngularVector acc = default;
                EulerRate rotVel = default;

                if (view.HasComponent<SimVelocity>(entity))
                {
                    ref readonly var simVel = ref view.GetComponentRO<SimVelocity>(entity);
                    vel = EnuToAngularVector(simVel.Linear, heading);
                    acc = new AngularVector { Azimuth = heading, Elevation = 0f, Length = 0f };
                    rotVel = new EulerRate
                    {
                        // Angular velocity: rad/s to deg/s
                        // simVel.Angular: X=roll-rate, Y=pitch-rate, Z=yaw-rate
                        Heading = simVel.Angular.Z * (180f / MathF.PI),
                        Pitch   = simVel.Angular.Y * (180f / MathF.PI),
                        Roll    = simVel.Angular.X * (180f / MathF.PI),
                    };
                }

                Publish(new WorldPos
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
                    Pos = new GeoPoint
                    {
                        Latitude  = lat,
                        Longitude = lon,
                        Altitude  = alt,
                    },
                    Ori = new EulerOri
                    {
                        Heading = heading,
                        Pitch   = pitch,
                        Roll    = roll,
                    },
                    Vel    = vel,
                    Acc    = acc,
                    RotVel = rotVel,
                });

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<GeoSpatialEgressTranslator>.Debug(
                        "[Node-{0}] Egress: Writing WorldPos for NetID={1} pos=({2},{3})", _localNodeId, netId.Value, lat, lon);
                }
            }
        }

        /// <inheritdoc/>
        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
        }

        /// <summary>
        /// Converts an ENU vector (X=East, Y=North, Z=Up) to <see cref="AngularVector"/>
        /// (Azimuth=compass heading, Elevation=pitch, Length=magnitude).
        /// </summary>
        private static AngularVector EnuToAngularVector(Vector3 enu, float fallbackAzimuth)
        {
            float length = enu.Length();
            if (length < 1e-4f)
                return new AngularVector { Azimuth = fallbackAzimuth, Elevation = 0f, Length = 0f };

            float azimuth = SimTransformBridgeSystem.VelocityToAzimuthDeg(enu, fallbackAzimuth);
            float elevation = MathF.Asin(Math.Clamp(enu.Z / length, -1f, 1f)) * (180f / MathF.PI);

            return new AngularVector
            {
                Azimuth   = azimuth,
                Elevation = elevation,
                Length    = length,
            };
        }
    }
}
