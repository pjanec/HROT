using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Utilities;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that publishes <see cref="MapVisualOverlay"/> DDS samples
    /// from the <see cref="EditablePolyline"/> managed component.
    /// </summary>
    public class MapVisualOverlayEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MapVisualOverlay";
        private const long OrdinalValue = (long)EDescriptorType.dtMapVisualOverlay;

        private readonly DdsWriter<MapVisualOverlay> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly HashSet<long> _tracedNetIds = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        // Targets: EditablePolyline (117)
        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.EditablePolyline };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public MapVisualOverlayEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform)
        {
            _writer = new DdsWriter<MapVisualOverlay>(participant, DdsTopicName);
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        // ── Ingress (egress-only) ────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        // ── Egress ───────────────────────────────────────────────────────────

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .WithManaged<EditablePolyline>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var netId   = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var simTr   = ref view.GetComponentRO<SimTransform>(entity);
                var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);

                // Reference position in geodetic space (entity's SimTransform position).
                var (refLat, refLon, refAlt) = _geoTransform.ToGeodetic(simTr.Position);

                // Points in EditablePolyline are RELATIVE Cartesian offsets from SimTransform.
                // Convert back to RELATIVE geo offsets (deltaLat, deltaLon, deltaAlt).
                var geoPoints = new List<GeoPoint>(polyline.Points.Count);
                for (int i = 0; i < polyline.Points.Count; i++)
                {
                    var relCart = polyline.Points[i];
                    var absCart = new Vector3(simTr.Position.X + relCart.X, simTr.Position.Y + relCart.Y, simTr.Position.Z);
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(absCart);
                    geoPoints.Add(new GeoPoint
                    {
                        Latitude  = lat - refLat,
                        Longitude = lon - refLon,
                        Altitude  = alt - refAlt,
                    });
                }

                // Extract the authoritative style to send to the IGs
                string styleJson = string.Empty;
                if (view.HasComponent<MapOverlayStyle>(entity))
                {
                    ref readonly var style = ref view.GetComponentRO<MapOverlayStyle>(entity);
                    styleJson = System.Text.Json.JsonSerializer.Serialize(style);
                }

                _writer.Write(new MapVisualOverlay
                {
                    EntityId        = (int)netId.Value,
                    PersistenceMode = PersistenceMode.MODE_PERSISTENT,
                    Points          = geoPoints,
                    IsEditable      = true,
                    IsClickable     = true,
                    StyleOverrideJson = styleJson // FIX: Inject the preserved style
                });

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<MapVisualOverlayEgressTranslator>.Debug(
                        "[TRACE-SH] Egress: MapVisualOverlay for NetID={0} points={1}",
                        netId.Value, polyline.Points.Count);
                }
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _writer.DisposeInstance(new MapVisualOverlay { EntityId = (int)networkEntityId });
        }
    }
}
