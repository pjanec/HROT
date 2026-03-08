using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Bagira <c>MapVisualOverlay</c> DDS topic.
    ///
    /// Applies overlay geometry by creating or updating <see cref="EditablePolyline"/>.
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class MapVisualOverlayIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MapVisualOverlay";
        private const long OrdinalValue = (long)EDescriptorType.dtMapVisualOverlay;

        private readonly DdsReader<MapVisualOverlay>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform? _geoTransform;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        public MapVisualOverlayIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            IGeographicTransform? geoTransform,
            GhostCreationSystem ghostCreationSystem)
        {
            _reader = participant is not null ? new DdsReader<MapVisualOverlay>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform;
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                    continue;

                ProcessSample(sample.Data, cmd, view as EntityRepository);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not MapVisualOverlay overlay)
                return;

            // When descriptors are applied in order (GeoSpatial before MapVisualOverlay),
            // SimTransform is already set and we can reconstruct accurate Cartesian offsets.
            Vector3? entityPos = null;
            if (repo.IsAlive(entity) && repo.HasComponent<SimTransform>(entity))
            {
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(entity);
                entityPos = t.Position;
            }

            repo.SetManagedComponent(entity, BuildPolyline(overlay, entityPos));
            repo.SetComponent(entity, MapOverlayStyle.FromJson(overlay.StyleOverrideJson));
        }

        public void Dispose(long networkEntityId) { }

        internal void ProcessSample(in MapVisualOverlay data, IEntityCommandBuffer cmd, EntityRepository? repo)
        {
            long netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (repo == null)
                {
                    FdpLog<MapVisualOverlayIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            // Pass the entity's Cartesian position so BuildPolyline can correctly
            // reconstruct absolute Cartesian from relative geodetic offsets.
            Vector3? entityPos = null;
            if (repo != null && repo.IsAlive(entity) && repo.HasComponent<SimTransform>(entity))
            {
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(entity);
                entityPos = t.Position;
            }

            cmd.SetManagedComponent(entity, BuildPolyline(data, entityPos));
            cmd.SetComponent(entity, MapOverlayStyle.FromJson(data.StyleOverrideJson));
        }

        /// <param name="entityCartesianPos">
        /// The entity's world-space position from <see cref="SimTransform.Position"/>, used to
        /// reconstruct absolute geodetic coordinates from the stored relative offsets before
        /// converting back to relative Cartesian.  When <c>null</c> (entity SimTransform not
        /// yet available), falls back to the legacy behaviour that treats delta-geo as absolute
        /// geo — accurate only when the geo-transform origin matches the entity centroid.
        /// </param>
        private EditablePolyline BuildPolyline(in MapVisualOverlay overlay, Vector3? entityCartesianPos = null)
        {
            var polyline = new EditablePolyline();
            if (overlay.Points == null || overlay.Points.Count == 0)
                return polyline;

            polyline.Points = new List<Vector2>(overlay.Points.Count);
            for (int i = 0; i < overlay.Points.Count; i++)
            {
                var geo = overlay.Points[i];
                if (_geoTransform != null && entityCartesianPos.HasValue)
                {
                    // Correct path: reconstruct absolute Cartesian from (entityRefGeo + deltaGeo),
                    // then express as relative offset from the entity's SimTransform origin.
                    // This avoids the cos(refLat) scale error that arises when delta-geo values
                    // are passed directly to ToCartesian as if they were absolute coordinates.
                    var refGeo = _geoTransform.ToGeodetic(entityCartesianPos.Value);
                    var absCart = _geoTransform.ToCartesian(
                        refGeo.lat + geo.Latitude,
                        refGeo.lon + geo.Longitude,
                        refGeo.alt + geo.Altitude);
                    var relCart = absCart - entityCartesianPos.Value;
                    polyline.Points.Add(new Vector2(relCart.X, relCart.Y));
                }
                else if (_geoTransform != null)
                {
                    // Fallback (entity position unavailable): treat delta-geo as absolute geo
                    // and compute displacement from the geo-transform Cartesian origin.
                    // Legacy behaviour — accurate only when the transform origin ≈ entity centroid.
                    Vector3 origin = _geoTransform.ToCartesian(0.0, 0.0, 0.0);
                    var absCart = _geoTransform.ToCartesian(geo.Latitude, geo.Longitude, geo.Altitude);
                    var relCart = absCart - origin;
                    polyline.Points.Add(new Vector2(relCart.X, relCart.Y));
                }
                else
                {
                    // No geo transform: treat lat/lon as Y/X directly.
                    polyline.Points.Add(new Vector2((float)geo.Longitude, (float)geo.Latitude));
                }
            }

            return polyline;
        }
    }
}
