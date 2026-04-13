using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common.Commands;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

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
        private readonly DdsWriter<MapCommandAck>              _ackWriter;
        private readonly DdsWriter<ContextMenuRequest>         _contextMenuWriter;
        private readonly DdsReader<MapInteractionConfig>       _configReader;
        private readonly DdsReader<MapCommandRequest>          _commandReader;
        private readonly DdsReader<CreateUpdateDeleteEntityAck> _ackReader;
        private readonly ICommandGateway                       _commandGateway;
        private readonly int                                   _mapId;
        private bool _disposed;

        /// <inheritdoc/>
        public ICommandGateway CommandGateway => _commandGateway;

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
            _clickWriter       = new DdsWriter<MapClickEvent>(participant, "MapClickEvent");
            _selectionWriter   = new DdsWriter<SelectionChangedEvent>(participant, "SelectionChangedEvent");
            _ackWriter         = new DdsWriter<MapCommandAck>(participant, "MapCommandAck");
            _contextMenuWriter = new DdsWriter<ContextMenuRequest>(participant, "ContextMenuRequest");
            _configReader      = new DdsReader<MapInteractionConfig>(participant);
            _commandReader     = new DdsReader<MapCommandRequest>(participant, "MapCommandRequest");
            _ackReader         = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
            _commandGateway    = new NedCommandGateway(participant, nodeId);
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
                Position             = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude, Altitude = dto.Altitude },
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
                SelectedEntityIds = new List<int>(dto.SelectedEntityIds),
            });
        }

        /// <inheritdoc/>
        public void WriteMapCommandAck(MapCommandAckDto dto)
        {
            _ackWriter.Write(new MapCommandAck
            {
                RequestId  = dto.RequestId,
                StatusCode = dto.StatusCode,
                DataJson   = dto.DataJson ?? string.Empty,
            });
        }

        /// <inheritdoc/>
        public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection)
        {
            _contextMenuWriter.Write(new ContextMenuRequest
            {
                RequestId    = requestId,
                MapId        = mapId,
                ForSelection = new List<int>(forSelection),
            });
        }

        /// <inheritdoc/>
        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson)
        {
            try
            {
                using var writer = new DdsWriter<IGCapabilitiesAnnounce>(_participant, "IGCapabilitiesAnnounce");
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
        public MapConfigDto? PollMapConfig()
        {
            using var loan = _configReader.Take(1);
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new MapConfigDto
                {
                    ActiveContextId = d.ActiveContextId,
                    ConfigJson      = d.ConfigurationJson ?? string.Empty,
                };
            }
            return null;
        }

        /// <inheritdoc/>
        public MapCommandDto? PollMapCommand()
        {
            using var loan = _commandReader.Take(1);
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new MapCommandDto
                {
                    RequestId       = d.RequestId,
                    TargetMapId     = d.MapId,
                    CommandType     = d.Type.ToString(),
                    CommandArgsJson = d.CommandArgsJson ?? string.Empty,
                };
            }
            return null;
        }

        /// <inheritdoc/>
        public EntityLifecycleAckDto? PollEntityLifecycleAck()
        {
            using var loan = _ackReader.Take(1);
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new EntityLifecycleAckDto
                {
                    RequestId  = d.RequestId,
                    EntityId   = d.EntityId,
                    StatusCode = d.StatusCode,
                };
            }
            return null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clickWriter.Dispose();
            _selectionWriter.Dispose();
            _ackWriter.Dispose();
            _contextMenuWriter.Dispose();
            _configReader.Dispose();
            _commandReader.Dispose();
            _ackReader.Dispose();
            _commandGateway.Dispose();
        }
    }
}
