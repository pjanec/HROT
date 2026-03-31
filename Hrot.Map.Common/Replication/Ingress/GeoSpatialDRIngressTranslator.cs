using System;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the merged <c>WorldPos</c> DDS topic (velocity plane).
    /// Converts polar velocity (<see cref="AngularVector"/>) fields into <see cref="NetworkVelocity"/>.
    /// </summary>
    public class GeoSpatialDRIngressTranslator : CycloneTranslator<WorldPos, WorldPos>
    {
        private const string DdsTopicName = "WorldPos";
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

        protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
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
            if (data is not WorldPos wp) return;

            float speedMs = (float)wp.Vel.Length;
            float azimRad = (float)wp.Vel.Azimuth * (MathF.PI / 180f);
            float elevRad = (float)wp.Vel.Elevation * (MathF.PI / 180f);

            var cartVel = new Vector3(
                speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),
                speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),
                speedMs * MathF.Sin(elevRad));

            repo.SetComponent(entity, new NetworkVelocity { Value = cartVel });
        }
    }
}
