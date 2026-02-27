using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator for the Bagira <c>GeoSpatial</c> DDS topic.
    ///
    /// Converts geodetic coordinates (lat/lon/alt) + heading/pitch/roll into an ECS
    /// <see cref="SimTransform"/> using the supplied <see cref="IGeographicTransform"/>.
    ///
    /// Entities not yet in <see cref="NetworkEntityMap"/> are silently skipped — they will
    /// be processed on the next tick once <c>NetworkSpawningSystem</c> has registered them.
    ///
    /// IG is a ghost-only node — <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class GeoSpatialTranslator : CycloneTranslator<GeoSpatial, GeoSpatial>
    {
        private const string DdsTopicName = "GeoSpatial";
        private const long   OrdinalValue = 10;

        private readonly IGeographicTransform _geoTransform;

        public GeoSpatialTranslator(
            DdsParticipant          participant,
            NetworkEntityMap        entityMap,
            IGeographicTransform    geoTransform)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        protected override void Decode(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
                return; // Entity not yet spawned — skip; will be retried next tick

            var latLon = string.Concat(data.Pos.Latitude, ",", data.Pos.Longitude);
            FdpLog<GeoSpatialTranslator>.Debug(
                "[TRACE-IG] Ingress: GeoSpatial Entity={0} LatLon=({1})", entity.Index, latLon);

            var cartesian = _geoTransform.ToCartesian(
                data.Pos.Latitude,
                data.Pos.Longitude,
                data.Pos.Altitude);

            // Preserve existing rotation if available; otherwise use heading from GeoSpatial.
            Quaternion rotation = Quaternion.Identity;
            if (view.HasComponent<SimTransform>(entity))
                rotation = view.GetComponentRO<SimTransform>(entity).Rotation;

            cmd.SetComponent(entity, new SimTransform
            {
                Position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z),
                Rotation = rotation
            });
        }

        // ── Egress (IG is ghost-only — nothing to publish) ───────────────────

        public override void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not GeoSpatial geo) return;
            var cartesian = _geoTransform.ToCartesian(geo.Pos.Latitude, geo.Pos.Longitude, geo.Pos.Altitude);
            repo.SetComponent(entity, new SimTransform
            {
                Position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z),
                Rotation = Quaternion.Identity
            });
        }
    }
}
