using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.BDC.Common;
using Hrot.BDC.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC world position translator.
    /// Egress: writes BDC_WorldPos for locally-owned entities.
    /// Ingress: updates SimTransform on ghost entities from incoming BDC_WorldPos samples.
    /// </summary>
    internal sealed class BdcWorldPosTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<BdcWorldPos>? _writer;
        private readonly DdsReader<BdcWorldPos>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly long _localNodeId;

        public string TopicName => "BDC_WorldPos";
        // BDC WorldPos ordinal
        public long DescriptorOrdinal => (long)BdcDescriptorType.WorldPos;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.SimTransform };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public BdcWorldPosTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
        {
            _entityMap    = entityMap;
            _geoTransform = geoTransform;
            _localNodeId  = localNodeId;
            _writer       = new DdsWriter<BdcWorldPos>(participant, "BDC_WorldPos");
            _reader       = new DdsReader<BdcWorldPos>(participant);
        }

        /// <summary>
        /// ENU linear velocity to the wire's azimuth/elevation/length form. Mirrors the NED stack's
        /// <c>EnuToAngularVector</c> so a receiver decodes both wires with the same arithmetic.
        /// </summary>
        private static BdcAngularVector ToAngularVector(Vector3 enu)
        {
            float length = enu.Length();
            if (length <= 1e-6f)
                return new BdcAngularVector { Azimuth = 0, Elevation = 0, Length = 0 };

            // Azimuth measured clockwise from north (+Y), elevation above the horizontal plane.
            float azimuthDeg   = MathF.Atan2(enu.X, enu.Y) * (180f / MathF.PI);
            float elevationDeg = MathF.Asin(Math.Clamp(enu.Z / length, -1f, 1f)) * (180f / MathF.PI);

            return new BdcAngularVector
            {
                Azimuth   = azimuthDeg,
                Elevation = elevationDeg,
                Length    = length,
            };
        }

        /// <summary>Inverse of <see cref="ToAngularVector"/>: wire form back to ENU linear velocity.</summary>
        private static Vector3 FromAngularVector(BdcAngularVector v)
        {
            float speed   = v.Length;
            float azimRad = v.Azimuth   * (MathF.PI / 180f);
            float elevRad = v.Elevation * (MathF.PI / 180f);

            return new Vector3(
                speed * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                speed * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                speed * MathF.Sin(elevRad));
        }

        /// <summary>
        /// Decodes the sample's simulation-time stamp, complaining loudly if it carries none.
        /// The NED stack's translator has the same guard, for the same reason — see
        /// <c>docs/DESIGN_Dead_Reckoning.md</c> rule R6.
        /// </summary>
        private double DecodeSimStampOrComplain(DateTime wireStamp, long entityId)
        {
            if (SimStampCodec.TryDecode(wireStamp, out double simStamp))
                return simStamp;

            FdpLog<BdcWorldPosTranslator>.Warn(
                "[BDC Node-{0}] BDC_WorldPos for entity {1} arrived with NO simulation-time stamp. " +
                "Dead reckoning cannot extrapolate it and will hold the raw sample. " +
                "The publisher is not stamping — fix it there (docs/DESIGN_Dead_Reckoning.md R6).",
                _localNodeId, entityId);

            return 0.0;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            // Sampled once per scan so every entity in this frame carries the same stamp.
            double simNowSeconds = Fdp.Toolkit.Time.SimClock.Of(view).TotalTime;

            var query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.Active)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);

                var (lat, lon, alt) = _geoTransform.ToGeodetic(simTf.Position);
                float heading = SimTransformBridgeSystem.RotationToHeadingDeg(simTf.Rotation);
                SimTransformBridgeSystem.RotationToPitchRollDeg(simTf.Rotation, out float pitch, out float roll);

                // Velocity is what dead reckoning extrapolates ALONG; publishing zeros made every
                // receiver hold the last sample still until the next one arrived, which is the
                // stutter DR exists to remove (CE-211). Encoded the same way the NED stack encodes
                // it — azimuth/elevation/length in degrees and m/s — so the two wires agree.
                var vel = new BdcAngularVector { Azimuth = 0, Elevation = 0, Length = 0 };
                if (view.HasComponent<SimVelocity>(entity))
                {
                    ref readonly var simVel = ref view.GetComponentRO<SimVelocity>(entity);
                    vel = ToAngularVector(simVel.Linear);
                }

                _writer!.Write(new BdcWorldPos
                {
                    EntityId = (int)netId.Value,
                    // Cluster-synced SIMULATION time, never wall time — see SimStampCodec and
                    // docs/DESIGN_Dead_Reckoning.md rule R4.
                    Time     = SimStampCodec.Encode(simNowSeconds),
                    Pos      = new BdcGeoPoint
                    {
                        Latitude  = lat,
                        Longitude = lon,
                        Altitude  = alt,
                    },
                    Ori = new BdcEulerOri
                    {
                        Heading = heading,
                        Pitch   = pitch,
                        Roll    = roll,
                    },
                    Vel = vel,
                });

                SentSampleCount++;
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                    continue;

                if (!sample.IsValid)
                    continue;

                ReceivedSampleCount++;
                var msg = sample.Data;

                if (!_entityMap.TryGetEntity(msg.EntityId, out var entity))
                    continue;

                // Guard: skip locally-owned entities to avoid loopback overwrite
                bool isLocallyOwned = view.HasComponent<NetworkAuthority>(entity)
                                      && view.GetComponentRO<NetworkAuthority>(entity).HasAuthority;
                if (isLocallyOwned)
                    continue;

                var cartesian = _geoTransform.ToCartesian(
                    msg.Pos.Latitude, msg.Pos.Longitude, msg.Pos.Altitude);

                var position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);
                var rotation = SimTransformBridgeSystem.HeadingDegToRotation(msg.Ori.Heading);

                // The sample and its stamp — the anchor dead reckoning extrapolates FROM. Until
                // CE-211 this translator wrote SimTransform only, so on a BDC node the DR system
                // registered at BdcReplicationModule:87 matched zero entities: its query requires
                // NetworkTransform + NetworkVelocity and nothing here supplied either. The
                // registration was real and the mechanism was inert.
                cmd.SetComponent(entity, new NetworkTransform
                {
                    LastPosition = position,
                    LastRotation = rotation,
                    SimStamp     = DecodeSimStampOrComplain(msg.Time, msg.EntityId),
                });
                cmd.SetComponent(entity, new NetworkVelocity { Value = FromAngularVector(msg.Vel) });

                cmd.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });

                FdpLog<BdcWorldPosTranslator>.Debug(
                    "[BDC Node-{0}] Ingress: BDC_WorldPos EntityId={1}", _localNodeId, msg.EntityId);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not BdcWorldPos msg) return;

            var cartesian = _geoTransform.ToCartesian(
                msg.Pos.Latitude, msg.Pos.Longitude, msg.Pos.Altitude);

            var position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);
            var rotation = SimTransformBridgeSystem.HeadingDegToRotation(msg.Ori.Heading);

            repo.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });
        }

        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new BdcWorldPos { EntityId = (int)networkEntityId });
        }
    }
}
