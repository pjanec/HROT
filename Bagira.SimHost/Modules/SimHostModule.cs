using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Translators;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Services;
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

        public List<CreateEntityRequest> TakeRequests()
        {
            var list = new List<CreateEntityRequest>();
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    list.Add(sample.Data);
            return list;
        }
    }

    // ─── DDS-backed adapter: writes CreateEntityAck responses ────────────────────────

    internal sealed class DdsCreateEntityAckSink : ICreateEntityAckSink
    {
        private readonly DdsWriter<CreateEntityAck> _writer;

        public DdsCreateEntityAckSink(DdsParticipant participant)
            => _writer = new DdsWriter<CreateEntityAck>(participant);

        public void WriteAck(CreateEntityAck ack) => _writer.Write(ack);
    }

    // ─── Module ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hosts <see cref="CreateEntityRequestSystem"/> together with a
    /// <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>.
    /// The CreateEntityRequest handler translates DDS requests into
    /// <c>SpawnEntityCommand</c> events; NetworkSpawningSystem processes them each tick.
    ///
    /// Also creates and exposes a <see cref="GeoSpatialEgressTranslator"/> for
    /// publishing GeoSpatial/GeoSpatialDR DDS topics from ECS GeoTransform/GeoVelocity
    /// components written by <c>SimTransformBridgeSystem</c> in the Geographic toolkit.
    /// </summary>
    public class SimHostModule : IModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly CreateEntityRequestSystem    _requestSystem;
        private readonly NetworkSpawningSystem        _spawnSystem;
        private readonly GeoSpatialEgressTranslator?  _geoEgressTranslator;
        private readonly EntityMissionTranslator      _missionIngressTranslator;
        private readonly EntityMissionEgressTranslator _missionEgressTranslator;

        public SimHostModule(
            DdsParticipant     participant,
            ITkbDatabase       tkbDb,
            INetworkIdAllocator idAllocator,
            int                localNodeId,
            NetworkSpawningSystem spawnSystem,
            NetworkEntityMap   entityMap,
            IGeographicTransform? geoTransform = null)
        {
            var requestSource = new DdsCreateEntityRequestSource(participant);
            var ackSink       = new DdsCreateEntityAckSink(participant);

            _requestSystem = new CreateEntityRequestSystem(
                requestSource,
                ackSink,
                tkbDb,
                idAllocator,
                localNodeId,
                geoTransform);

            _spawnSystem = spawnSystem;

            // Create GeoSpatial egress translator when geographic transform is available
            if (geoTransform != null)
            {
                _geoEgressTranslator = new GeoSpatialEgressTranslator(participant, entityMap);
            }

            // Mission translators are always active regardless of geographic transform.
            _missionIngressTranslator = new EntityMissionTranslator(participant, entityMap);
            _missionEgressTranslator  = new EntityMissionEgressTranslator(participant, entityMap);
        }

        /// <summary>
        /// Gets the GeoSpatial egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public GeoSpatialEgressTranslator? GeoEgressTranslator => _geoEgressTranslator;

        /// <summary>
        /// Gets the EntityMission ingress translator (DDS → ECS).
        /// Always non-null; created unconditionally in the constructor.
        /// </summary>
        public EntityMissionTranslator MissionIngressTranslator => _missionIngressTranslator;

        /// <summary>
        /// Gets the EntityMission egress translator (ECS → DDS).
        /// Always non-null; created unconditionally in the constructor.
        /// </summary>
        public EntityMissionEgressTranslator MissionEgressTranslator => _missionEgressTranslator;

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(_requestSystem);
            registry.RegisterSystem(_spawnSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
