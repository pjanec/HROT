using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using FDP.Toolkit.Replication.Patching;
using Bagira.SimHost.Systems;
using Bagira.Map.Common.Replication.Egress;
using Bagira.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Modules
{
    // ─── DDS-backed adapter: polls the DDS reader for incoming CreateEntityRequest ─────

    internal sealed class DdsCreateEntityRequestSource : ICreateEntityRequestSource
    {
        private readonly DdsReader<CreateEntityRequest> _reader;

        public DdsCreateEntityRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<CreateEntityRequest>(participant);

        public void ProcessRequests(Action<CreateEntityRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }
    }

    // ─── DDS-backed adapter: writes CreateUpdateDeleteEntityAck responses ───────────────────────

    internal sealed class DdsCreateUpdateDeleteEntityAckSink : ICreateUpdateDeleteEntityAckSink
    {
        private readonly DdsWriter<CreateUpdateDeleteEntityAck> _writer;

        public DdsCreateUpdateDeleteEntityAckSink(DdsParticipant participant)
            => _writer = new DdsWriter<CreateUpdateDeleteEntityAck>(participant);

        public void WriteAck(CreateUpdateDeleteEntityAck ack) => _writer.Write(ack);
    }
    // ─── DDS-backed adapter: polls the DDS reader for incoming DeleteEntityRequest ────────

    internal sealed class DdsDeleteEntityRequestSource : IDeleteEntityRequestSource
    {
        private readonly DdsReader<DeleteEntityRequest> _reader;

        public DdsDeleteEntityRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<DeleteEntityRequest>(participant);

        public void ProcessRequests(Action<DeleteEntityRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }
    }
    // ─── Module ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hosts <see cref="CreateEntityRequestSystem"/> together with a
    /// <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>.
    /// The CreateEntityRequest handler translates DDS requests into
    /// <c>SpawnEntityCommand</c> events; NetworkSpawningSystem processes them each tick.
    ///
    /// Also creates and exposes a <see cref="GeoSpatialEgressTranslator"/> for
    /// publishing GeoSpatial/GeoSpatialDR DDS topics by converting ECS SimTransform/SimVelocity
    /// to geodetic coordinates on-the-fly via IGeographicTransform.
    /// </summary>
    public class SimHostModule : IEcsModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly CreateEntityRequestSystem    _requestSystem;
        private readonly NetworkSpawningSystem        _spawnSystem;
        private readonly SstRequestFinalizationSystem _finalizationSystem;
        private readonly DeleteEntityRequestSystem    _deleteSystem;
        private readonly GeoSpatialEgressTranslator? _geoEgressTranslator;
        private readonly MapVisualOverlayEgressTranslator? _mapOverlayEgressTranslator;
        private readonly MapRouteEgressTranslator? _mapRouteEgressTranslator;
        private readonly EntityMissionIngressTranslator _missionIngressTranslator;
        private readonly EntityMissionEgressTranslator _missionEgressTranslator;

        public SimHostModule(
            DdsParticipant     participant,
            ITkbDatabase       tkbDb,
            INetworkIdAllocator idAllocator,
            int                localNodeId,
            NetworkSpawningSystem spawnSystem,
            NetworkEntityMap entityMap,
            DoctrineRegistry doctrineRegistry,
            GhostCreationSystem ghostCreationSystem,
            IGeographicTransform? geoTransform = null,
            JsonAttributeCompiler jsonAttributeCompiler = null!,
            BinaryInterpreter? binaryInterpreter = null)
        {
            var requestSource    = new DdsCreateEntityRequestSource(participant);
            var ackSink           = new DdsCreateUpdateDeleteEntityAckSink(participant);
            var deleteSource      = new DdsDeleteEntityRequestSource(participant);

            _finalizationSystem = new SstRequestFinalizationSystem(ackSink, entityMap);

            _requestSystem = new CreateEntityRequestSystem(
                requestSource,
                ackSink,
                tkbDb,
                idAllocator,
                localNodeId,
                geoTransform,
                jsonAttributeCompiler,
                binaryInterpreter,
                _finalizationSystem);

            _deleteSystem = new DeleteEntityRequestSystem(
                deleteSource,
                ackSink,
                entityMap,
                _finalizationSystem);

            _spawnSystem = spawnSystem;

            // Create GeoSpatial egress translator when geographic transform is available
            if (geoTransform != null)
            {
                _geoEgressTranslator = new GeoSpatialEgressTranslator(participant, entityMap, geoTransform);
                _mapOverlayEgressTranslator = new MapVisualOverlayEgressTranslator(participant, entityMap, geoTransform);
                _mapRouteEgressTranslator   = new MapRouteEgressTranslator(participant, entityMap, geoTransform);
            }

            // Mission translators are always active regardless of geographic transform.
            _missionIngressTranslator = new EntityMissionIngressTranslator(participant, entityMap, doctrineRegistry, ghostCreationSystem);
            _missionEgressTranslator  = new EntityMissionEgressTranslator(participant, entityMap);
        }

        /// <summary>
        /// Gets the GeoSpatial egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public GeoSpatialEgressTranslator? GeoEgressTranslator => _geoEgressTranslator;

        /// <summary>
        /// Gets the MapVisualOverlay egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapVisualOverlayEgressTranslator? MapOverlayEgressTranslator => _mapOverlayEgressTranslator;

        /// <summary>
        /// Gets the MapRoute egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapRouteEgressTranslator? MapRouteEgressTranslator => _mapRouteEgressTranslator;

        /// <summary>
        /// Gets the EntityMission ingress translator (DDS → ECS).
        /// Always non-null; created unconditionally in the constructor.
        /// </summary>
        public EntityMissionIngressTranslator MissionIngressTranslator => _missionIngressTranslator;

        /// <summary>
        /// Gets the EntityMission egress translator (ECS → DDS).
        /// Always non-null; created unconditionally in the constructor.
        /// </summary>
        public EntityMissionEgressTranslator MissionEgressTranslator => _missionEgressTranslator;

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(_requestSystem);
            registry.RegisterSystem(_spawnSystem);
            registry.RegisterSystem(_deleteSystem);
            registry.RegisterSystem(_finalizationSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
