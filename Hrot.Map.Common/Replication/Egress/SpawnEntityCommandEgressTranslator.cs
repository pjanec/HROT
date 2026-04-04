using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="SpawnEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="CreateEntityRequest"/> DDS samples.
    ///
    /// <para>
    /// Two paths are supported:
    /// <list type="bullet">
    ///   <item><b>Side-channel path:</b> If a pre-built <see cref="CreateEntityRequest"/> is
    ///     available via the <c>tryGetPrebuilt</c> delegate (keyed by
    ///     <see cref="SpawnEntityCommand.RequestId"/>), that request is written directly to DDS
    ///     without re-serialisation, preserving all descriptors built in the area/route
    ///     authoring pipelines (dtMapVisualOverlay, dtMapRoute, etc.).</item>
    ///   <item><b>Standard path:</b> Otherwise the command fields are serialised into a new
    ///     <see cref="CreateEntityRequest"/> containing <c>dtEntityMaster</c> (TKB type) and
    ///     <c>dtWorldPos</c> (geographic position from canvas Cartesian via
    ///     <see cref="IGeographicTransform"/>).</item>
    /// </list>
    /// </para>
    /// </summary>
    public class SpawnEntityCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "CreateEntityRequest";

        // Synthetic ordinal — this translator is event-driven (PollIngress only); ScanAndPublish is empty.
        private const long OrdinalValue = -1001L;

        private readonly IDdsWriter<CreateEntityRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly IGeographicTransform? _geoTransform;

        /// <summary>
        /// Optional side-channel delegate provided by <see cref="IgApplication"/> that looks up
        /// and removes a pre-built <see cref="CreateEntityRequest"/> keyed by the command's
        /// <see cref="SpawnEntityCommand.RequestId"/>. When present, this takes priority over the
        /// standard field-based construction path.
        /// </summary>
        private readonly Func<Guid, CreateEntityRequest?>? _tryGetPrebuilt;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public SpawnEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform,
            Func<Guid, CreateEntityRequest?>? tryGetPrebuilt = null)
            : this(new DdsWriterAdapter<CreateEntityRequest>(participant, DdsTopicName), eventBus, geoTransform, tryGetPrebuilt)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal SpawnEntityCommandEgressTranslator(
            IDdsWriter<CreateEntityRequest> writer,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform,
            Func<Guid, CreateEntityRequest?>? tryGetPrebuilt = null)
        {
            _writer          = writer       ?? throw new ArgumentNullException(nameof(writer));
            _eventBus        = eventBus     ?? throw new ArgumentNullException(nameof(eventBus));
            _geoTransform    = geoTransform;
            _tryGetPrebuilt  = tryGetPrebuilt;
        }

        /// <summary>
        /// Consumes pending <see cref="SpawnEntityCommand"/> events from the event bus and
        /// writes each as a <see cref="CreateEntityRequest"/> to DDS.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var spawnCmd in _eventBus.ConsumeManaged<SpawnEntityCommand>())
            {
                // Side-channel path: check if a fully-built CreateEntityRequest was stored
                // by MapCommandController.OnAreaEntityCreated for this request ID.
                if (_tryGetPrebuilt != null)
                {
                    var prebuilt = _tryGetPrebuilt(spawnCmd.RequestId);
                    if (prebuilt.HasValue)
                    {
                        _writer.Write(prebuilt.Value);
                        FdpLog<SpawnEntityCommandEgressTranslator>.Debug(
                            "[Egress] SpawnCmd side-channel → CreateEntityRequest req={0}", prebuilt.Value.RequestId);
                        continue;
                    }
                }

                // Standard path: serialise fields to CreateEntityRequest.
                {
                    var request = BuildCreateEntityRequest(spawnCmd);
                    _writer.Write(request);
                    FdpLog<SpawnEntityCommandEgressTranslator>.Debug(
                        "[Egress] SpawnCmd → CreateEntityRequest req={0} tkbType={1}",
                        request.RequestId, spawnCmd.TkbType);
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }

        // ── Private helpers ───────────────────────────────────────────────────

        private CreateEntityRequest BuildCreateEntityRequest(SpawnEntityCommand cmd)
        {
            double lat, lon, alt;

            if (_geoTransform != null && cmd.InitialTransform.HasValue)
            {
                (lat, lon, alt) = _geoTransform.ToGeodetic(cmd.InitialTransform.Value.Position);
            }
            else if (cmd.InitialTransform.HasValue)
            {
                // Offline / test mode: treat canvas XY as lat/lon directly.
                var pos = cmd.InitialTransform.Value.Position;
                lat = pos.Y;
                lon = pos.X;
                alt = 0.0;
            }
            else
            {
                lat = lon = alt = 0.0;
            }

            return new CreateEntityRequest
            {
                RequestId  = cmd.RequestId == Guid.Empty ? Guid.NewGuid() : cmd.RequestId,
                Owner      = default,
                Flags      = 0,
                InitialAttributesJson = cmd.InitialAttributesJson,
                InitialDescriptors    = new List<EntityDescriptorUnion>
                {
                    new EntityDescriptorUnion
                    {
                        _d           = EDescriptorType.dtEntityMaster,
                        EntityMaster = new EntityMaster { TkbType = cmd.TkbType },
                    },
                    new EntityDescriptorUnion
                    {
                        _d       = EDescriptorType.dtWorldPos,
                        WorldPos = new WorldPos
                        {
                            Pos = new GeoPoint
                            {
                                Latitude  = lat,
                                Longitude = lon,
                                Altitude  = alt,
                            },
                        },
                    },
                },
            };
        }
    }
}
