using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="UpdateEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="UpdateEntityDescriptorRequest"/> DDS samples.
    ///
    /// <para>
    /// Handles two component types:
    /// <list type="bullet">
    ///   <item><b><see cref="EditablePolyline"/>:</b> Converts entity-relative Cartesian offsets
    ///     to relative geodetic coordinates and writes an
    ///     <c>UpdateEntityDescriptorRequest(dtMapVisualOverlay)</c> so the SimHost (authority)
    ///     updates its managed copy of the overlay.</item>
    ///   <item><b><see cref="RoutePlan"/>:</b> No explicit write — version already bumped in ECS;
    ///     <c>MapRouteEgressTranslator.ScanAndPublish</c> picks up the change and publishes the
    ///     <c>MapRoute</c> DDS topic on the next frame.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class UpdateEntityCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "UpdateEntityDescriptorRequest";

        // Synthetic ordinal for event-driven translator (PollIngress only).
        private const long OrdinalValue = -1002L;

        private readonly IDdsWriter<UpdateEntityDescriptorRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform? _geoTransform;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public UpdateEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            NetworkEntityMap entityMap,
            IGeographicTransform? geoTransform,
            long localNodeId = 0)
            : this(new DdsWriterAdapter<UpdateEntityDescriptorRequest>(participant, DdsTopicName),
                   eventBus, entityMap, geoTransform, localNodeId)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal UpdateEntityCommandEgressTranslator(
            IDdsWriter<UpdateEntityDescriptorRequest> writer,
            FdpEventBus eventBus,
            NetworkEntityMap entityMap,
            IGeographicTransform? geoTransform,
            long localNodeId = 0)
        {
            _writer       = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus     = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _entityMap    = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform;
            _localNodeId  = localNodeId;
        }

        /// <summary>
        /// Consumes pending <see cref="UpdateEntityCommand"/> events from the event bus and
        /// writes <see cref="UpdateEntityDescriptorRequest"/> to DDS for overlay components.
        /// Route updates are handled by <c>MapRouteEgressTranslator.ScanAndPublish</c>.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var updateCmd in _eventBus.ConsumeManaged<UpdateEntityCommand>())
            {
                if (updateCmd.ComponentsToUpdate == null) continue;

                foreach (var component in updateCmd.ComponentsToUpdate)
                {
                    if (component is EditablePolyline polyline)
                    {
                        ProcessOverlayUpdate(updateCmd.NetworkId, updateCmd.RequestId, polyline, view);
                    }
                    // RoutePlan: version already bumped; MapRouteEgressTranslator.ScanAndPublish
                    // detects the change and publishes MapRoute DDS topic automatically.
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ProcessOverlayUpdate(
            long networkId, Guid requestId,
            EditablePolyline polyline, ISimulationView view)
        {
            if (_geoTransform == null)
            {
                FdpLog<UpdateEntityCommandEgressTranslator>.Warn(
                    "[Node-{0}] Cannot update overlay NetID={1}: geo-transform is null.", _localNodeId, networkId);
                return;
            }

            if (!_entityMap.TryGetEntity(networkId, out var entity))
            {
                FdpLog<UpdateEntityCommandEgressTranslator>.Warn(
                    "[Node-{0}] UpdateEntityCommand: entity for NetID={1} not found.", _localNodeId, networkId);
                return;
            }

            // Get entity anchor position.
            Vector3 anchor = Vector3.Zero;
            if (view.HasComponent<SimTransform>(entity))
                anchor = view.GetComponentRO<SimTransform>(entity).Position;

            // Convert relative Cartesian offsets to absolute ENU, then to geodetic.
            var geoPoints = new List<GeoPoint>(polyline.Points?.Count ?? 0);
            if (polyline.Points != null)
            {
                foreach (var relPt in polyline.Points)
                {
                    var absoluteEnu = new Vector3(anchor.X + relPt.X, anchor.Y + relPt.Y, anchor.Z);
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(absoluteEnu);
                    geoPoints.Add(new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt });
                }
            }

            var mapOverlay = new MapVisualOverlay
            {
                EntityId            = (int)networkId,
                PersistenceMode     = PersistenceMode.MODE_PERSISTENT,
                Points              = geoPoints,
                IsEditable          = true,
                IsClickable         = true,
                StylePresetName     = string.Empty,
                StyleOverrideJson   = string.Empty,
            };

            var request = new UpdateEntityDescriptorRequest
            {
                RequestId      = requestId == Guid.Empty ? Guid.NewGuid() : requestId,
                EntityId       = (int)networkId,
                DescriptorType = EDescriptorType.dtMapVisualOverlay,
                PartId         = 0,
                CurrentVersion = 0,
                Payload        = new EntityDescriptorUnion
                {
                    _d               = EDescriptorType.dtMapVisualOverlay,
                    MapVisualOverlay = mapOverlay,
                },
            };

            _writer.Write(request);

            FdpLog<UpdateEntityCommandEgressTranslator>.Debug(
                "[Node-{0}] UpdateEntityCommand \u2192 UpdateEntityDescriptorRequest(dtMapVisualOverlay) " +
                "NetID={1} points={2}", _localNodeId, networkId, geoPoints.Count);
        }
    }
}
