using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Fdp.ModuleHost.Abstractions;
using NodeId = Fdp.Core.NodeId;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="UpdateEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="UpdateEntityDescriptorRequest"/> DDS samples.
    ///
    /// <para>
    /// Handles two component types:
    /// <list type="bullet">
    ///   <item><b><see cref="EditablePolyline"/>:</b> Converts entity-relative offsets
    ///     to absolute local-ENU positions (AQ107 B) and writes an
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

        // Synthetic ordinal for event-driven translator (PollIngress only).
        private const long OrdinalValue = -1002L;

        private readonly IDdsWriter<UpdateEntityDescriptorRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<UpdateEntityDescriptorRequest>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public UpdateEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            NetworkEntityMap entityMap,
            int localNodeId = 0)
            : this(new DdsWriterAdapter<UpdateEntityDescriptorRequest>(participant),
                   eventBus, entityMap, localNodeId)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal UpdateEntityCommandEgressTranslator(
            IDdsWriter<UpdateEntityDescriptorRequest> writer,
            FdpEventBus eventBus,
            NetworkEntityMap entityMap,
            int localNodeId = 0)
        {
            _writer       = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus     = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _entityMap    = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId  = (Fdp.Core.NodeId)(localNodeId);
        }

        /// <summary>
        /// Consumes pending <see cref="UpdateEntityCommand"/> events from the event bus and
        /// writes <see cref="UpdateEntityDescriptorRequest"/> to DDS for overlay components.
        /// Route updates are handled by <c>MapRouteEgressTranslator.ScanAndPublish</c>.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var updateCmd in _eventBus.ReadManaged<UpdateEntityCommand>())
            {
                if (updateCmd.ComponentsToUpdate == null) continue;

                foreach (var component in updateCmd.ComponentsToUpdate)
                {
                    if (component is EditablePolyline polyline)
                    {
                        ProcessOverlayUpdate(updateCmd.NetworkId, updateCmd.RequestId, polyline, view);
                    }
                    else if (component is RoutePlan plan)
                    {
                        // ⭐ CE-4284 — on the route's OWNER the edit is already the published value (MapRouteEgressTranslator sees the new
                        //   version). ⛔ On any other node it never left: MapRouteEgress publishes only with authority, so the edit stayed local
                        //   and the owner's next sample overwrote it. ⇒ a non-owner sends it to the owner, as an area's points already go.
                        ProcessRouteUpdate(updateCmd.NetworkId, updateCmd.RequestId, plan, view);
                    }
                    else if (component is Fdp.Toolkit.Weather.WeatherConditions weather)
                    {
                        // ⭐ CE-4346 (X-3) — the same for the weather: its wind levels and cloud layers are lists, so it travels whole (Q8).
                        ProcessWeatherUpdate(updateCmd.NetworkId, updateCmd.RequestId, weather, view);
                    }
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(int networkEntityId) { }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ProcessRouteUpdate(int networkId, Guid requestId, RoutePlan plan, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return;
            long routeKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)EDescriptorType.dtMapRoute, 0);
            if (view.HasAuthority(entity, routeKey)) return;   // the owner publishes it itself

            _writer.Write(new UpdateEntityDescriptorRequest
            {
                RequestId      = requestId == Guid.Empty ? Guid.NewGuid() : requestId,
                EntityId       = networkId,
                DescriptorType = EDescriptorType.dtMapRoute,
                PartId         = 0,
                CurrentVersion = 0,
                Payload        = new EntityDescriptorUnion
                {
                    _d       = EDescriptorType.dtMapRoute,
                    MapRoute = Hrot.Map.Common.Replication.MapRouteWire.ToSample(networkId, plan),   // the one encoding
                },
            });
            SentSampleCount++;
        }

        /// <summary>⭐ CE-4346 (X-3) — a non-owner's weather edit, sent to the owner whole (the dtWeather topic's encoding).</summary>
        private void ProcessWeatherUpdate(int networkId, Guid requestId, Fdp.Toolkit.Weather.WeatherConditions weather, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return;
            long weatherKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)EDescriptorType.dtWeather, 0);
            if (view.HasAuthority(entity, weatherKey)) return;   // the owner publishes it itself

            _writer.Write(new UpdateEntityDescriptorRequest
            {
                RequestId      = requestId == Guid.Empty ? Guid.NewGuid() : requestId,
                EntityId       = networkId,
                DescriptorType = EDescriptorType.dtWeather,
                PartId         = 0,
                CurrentVersion = 0,
                Payload        = new EntityDescriptorUnion
                {
                    _d      = EDescriptorType.dtWeather,
                    Weather = Hrot.Map.Common.Replication.Egress.EntityWeatherEgressTranslator.ToSample(networkId, weather),   // the one encoding
                },
            });
            SentSampleCount++;
        }

        private void ProcessOverlayUpdate(
            int networkId, Guid requestId,
            EditablePolyline polyline, ISimulationView view)
        {
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

            // ⭐ AQ107 B — relative offsets → ABSOLUTE local ENU positions (the wire's one meaning for a position): a copy plus the anchor.
            var wirePoints = new List<Vec3d>(polyline.Points?.Count ?? 0);
            if (polyline.Points != null)
            {
                foreach (var relPt in polyline.Points)
                    wirePoints.Add(Hrot.Map.Common.Replication.Utils.EnuWire.ToWire(new Vector3(anchor.X + relPt.X, anchor.Y + relPt.Y, anchor.Z)));
            }

            var mapOverlay = new MapVisualOverlay
            {
                EntityId            = networkId,
                PersistenceMode     = PersistenceMode.MODE_PERSISTENT,
                Points              = wirePoints,
                IsEditable          = true,
                IsClickable         = true,
                StylePresetName     = string.Empty,
                StyleOverrideJson   = string.Empty,
            };

            var request = new UpdateEntityDescriptorRequest
            {
                RequestId      = requestId == Guid.Empty ? Guid.NewGuid() : requestId,
                EntityId       = networkId,
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
            SentSampleCount++;

            FdpLog<UpdateEntityCommandEgressTranslator>.Debug(
                "[Node-{0}] UpdateEntityCommand \u2192 UpdateEntityDescriptorRequest(dtMapVisualOverlay) " +
                "NetID={1} points={2}", _localNodeId, networkId, wirePoints.Count);
        }
    }
}
