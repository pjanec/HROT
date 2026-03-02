using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Bagira <c>GeoSpatial</c> DDS topic.
    ///
    /// Converts geodetic coordinates (lat/lon/alt) into <see cref="NetworkPosition"/>
    /// using the supplied <see cref="IGeographicTransform"/>.
    ///
    /// Entities not yet in <see cref="NetworkEntityMap"/> are silently skipped — they will
    /// be processed on the next tick once <c>NetworkSpawningSystem</c> has registered them.
    ///
    /// IG is a ghost-only node — <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class GeoSpatialIngressTranslator : CycloneTranslator<GeoSpatial, GeoSpatial>
    {
        private const string DdsTopicName = "GeoSpatial";
        private const long OrdinalValue = 10;

        private readonly IGeographicTransform _geoTransform;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public GeoSpatialIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            GhostCreationSystem ghostCreationSystem)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        protected override void Decode(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<GeoSpatialIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            var latitude = data.Pos.Latitude;
            var longitude = data.Pos.Longitude;
            FdpLog<GeoSpatialIngressTranslator>.Debug(
                "[TRACE-IG] Ingress: GeoSpatial Entity={0} Lat={1} Lon={2}", entity.Index, latitude, longitude);

            var cartesian = _geoTransform.ToCartesian(
                data.Pos.Latitude,
                data.Pos.Longitude,
                data.Pos.Altitude);

            var position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);

            cmd.SetComponent(entity, new NetworkPosition { Value = position });

            if (!view.HasComponent<SimTransform>(entity))
            {
                cmd.AddComponent(entity, new SimTransform
                {
                    Position = position,
                    Rotation = Quaternion.Identity
                });
            }
        }

        // ── Egress (IG is ghost-only — nothing to publish) ───────────────────

        public override void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not GeoSpatial geo) return;
            var cartesian = _geoTransform.ToCartesian(geo.Pos.Latitude, geo.Pos.Longitude, geo.Pos.Altitude);
            var position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);

            repo.SetComponent(entity, new NetworkPosition { Value = position });

            if (!repo.HasComponent<SimTransform>(entity))
            {
                repo.AddComponent(entity, new SimTransform
                {
                    Position = position,
                    Rotation = Quaternion.Identity
                });
            }
        }
    }
}
