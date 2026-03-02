using System;
using System.Numerics;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Bagira <c>GeoSpatialDR</c> DDS topic.
    /// Converts polar velocity (DAL3) into <see cref="NetworkVelocity"/>.
    /// </summary>
    public class GeoSpatialDRIngressTranslator : CycloneTranslator<GeoSpatialDR, GeoSpatialDR>
    {
        private const string DdsTopicName = "GeoSpatialDR";
        private const long OrdinalValue = 11;

        private readonly GhostCreationSystem _ghostCreationSystem;

        public GeoSpatialDRIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            GhostCreationSystem ghostCreationSystem)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _ = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        protected override void Decode(in GeoSpatialDR data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<GeoSpatialDRIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            float speedMs = (float)data.Vel.Length;
            float azimRad = (float)data.Vel.Azimuth * (MathF.PI / 180f);
            float elevRad = (float)data.Vel.Elevation * (MathF.PI / 180f);

            var cartVel = new Vector3(
                speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                speedMs * MathF.Sin(elevRad));

            cmd.SetComponent(entity, new NetworkVelocity { Value = cartVel });
        }

        public override void ScanAndPublish(ISimulationView view) { }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not GeoSpatialDR dr) return;

            float speedMs = (float)dr.Vel.Length;
            float azimRad = (float)dr.Vel.Azimuth * (MathF.PI / 180f);
            float elevRad = (float)dr.Vel.Elevation * (MathF.PI / 180f);

            var cartVel = new Vector3(
                speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                speedMs * MathF.Sin(elevRad));

            repo.SetComponent(entity, new NetworkVelocity { Value = cartVel });
        }
    }
}
