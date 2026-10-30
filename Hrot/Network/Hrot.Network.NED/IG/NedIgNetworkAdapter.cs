using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common.Commands;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// NED/DDS implementation of <see cref="IIgNetworkAdapter"/>.
    /// Owns all DDS writers and readers used by the IG application.
    /// Created by <see cref="Hrot.Network.NED.NedNetworkFactory.CreateIgNetworkAdapter"/>.
    /// </summary>
    public sealed class NedIgNetworkAdapter : IIgNetworkAdapter
    {
        private readonly DdsParticipant                          _participant;
        private readonly DdsWriter<MapClickEvent>              _clickWriter;
        private readonly DdsWriter<SelectionChangedEvent>      _selectionWriter;
        private readonly ICommandGateway                       _commandGateway;
        private readonly int                                   _mapId;
        private readonly DdsWriterGizmoAdapter<GizmoInteractionBatch>  _gizmoInteractionWriter;
        private readonly DdsReaderGizmoAdapter<DebugPrimitivesBatch>   _debugPrimitivesReader;
        private bool _disposed;

        /// <inheritdoc/>
        public ICommandGateway CommandGateway => _commandGateway;

        /// <inheritdoc/>
        public IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter => _gizmoInteractionWriter;

        /// <inheritdoc/>
        public IDdsReader<DebugPrimitivesBatch>? DebugPrimitivesReader => _debugPrimitivesReader;

        /// <summary>
        /// Creates all DDS writers and readers for the IG.
        /// </summary>
        /// <param name="participant">Active DDS participant; must not be null.</param>
        /// <param name="nodeId">Node ID of this IG instance (used as MapId).</param>
        public NedIgNetworkAdapter(DdsParticipant participant, long nodeId = 0)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            _participant       = participant;
            _mapId             = (int)nodeId;
            _clickWriter       = new DdsWriter<MapClickEvent>(participant);
            _selectionWriter   = new DdsWriter<SelectionChangedEvent>(participant);
            _commandGateway    = new NedCommandGateway(participant, nodeId);
            _gizmoInteractionWriter = new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant);
            _debugPrimitivesReader  = new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(participant);
        }

        /// <inheritdoc/>
        public void WriteMapClick(MapClickEventDto dto)
        {
            var hitStack = new List<MapObjectRef>();
            foreach (int entityId in dto.HitEntityIds)
            {
                hitStack.Add(new MapObjectRef { EntityId = entityId });
            }

            _clickWriter.Write(new MapClickEvent
            {
                MapId                = _mapId,
                Position             = new Vec3d { X = dto.X, Y = dto.Y, Z = dto.Z },   // CE-4073: the neutral DTO is local ENU metres too — a copy
                InteractionContextId = dto.InteractionContextId,
                HitStack             = hitStack,
            });
        }

        /// <inheritdoc/>
        public void WriteSelectionChanged(SelectionChangedEventDto dto)
        {
            _selectionWriter.Write(new SelectionChangedEvent
            {
                MapId             = dto.MapId,
                SelectedEntityIds = dto.SelectedEntityIds.Select(id => (long)id).ToList(),   // AQ107 E: neutral int32 → wire int64
            });
        }



        /// <inheritdoc/>
        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson)
        {
            try
            {
                using var writer = new DdsWriter<IGCapabilitiesAnnounce>(_participant);
                writer.Write(new IGCapabilitiesAnnounce
                {
                    MapId                    = mapId,
                    LayerTreeJson            = layerTreeJson,
                    ConfigurationSchemasJson = configSchemasJson,
                    OverlayStyleSchemaJson   = string.Empty,
                    TkbManifestJson          = string.Empty,
                });
            }
            catch (Exception ex)
            {
                FdpLog<NedIgNetworkAdapter>.Warn("[Node-{0}] Failed to publish IGCapabilitiesAnnounce: {1}", mapId, ex.Message);
            }
        }





        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clickWriter.Dispose();
            _selectionWriter.Dispose();
            _commandGateway.Dispose();
            _gizmoInteractionWriter.Dispose();
            _debugPrimitivesReader.Dispose();
        }
    }
}
