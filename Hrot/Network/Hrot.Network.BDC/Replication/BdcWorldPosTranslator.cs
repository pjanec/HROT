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
        public long DescriptorOrdinal => 1002;

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

        public void ScanAndPublish(ISimulationView view)
        {
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

                _writer!.Write(new BdcWorldPos
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
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
                    Vel = new BdcAngularVector
                    {
                        Azimuth   = 0,
                        Elevation = 0,
                        Length    = 0,
                    },
                });
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
