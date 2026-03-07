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

            repo.SetManagedComponent(entity, BuildPolyline(overlay));
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

            cmd.SetManagedComponent(entity, BuildPolyline(data));
            cmd.SetComponent(entity, MapOverlayStyle.FromJson(data.StyleOverrideJson));
        }

        private EditablePolyline BuildPolyline(in MapVisualOverlay overlay)
        {
            var polyline = new EditablePolyline();
            if (overlay.Points == null || overlay.Points.Count == 0)
                return polyline;

            polyline.Points = new List<Vector2>(overlay.Points.Count);
            for (int i = 0; i < overlay.Points.Count; i++)
            {
                var geo = overlay.Points[i];
                if (_geoTransform != null)
                {
                    var cart = _geoTransform.ToCartesian(geo.Latitude, geo.Longitude, geo.Altitude);
                    polyline.Points.Add(new Vector2((float)cart.X, (float)cart.Y));
                }
                else
                {
                    polyline.Points.Add(new Vector2((float)geo.Longitude, (float)geo.Latitude));
                }
            }

            return polyline;
        }
    }
}
