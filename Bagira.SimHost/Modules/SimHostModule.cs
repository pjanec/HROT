using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.SimHost.Systems;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Systems;
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
    /// </summary>
    public class SimHostModule : IModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly CreateEntityRequestSystem _requestSystem;
        private readonly NetworkSpawningSystem     _spawnSystem;

        public SimHostModule(
            DdsParticipant     participant,
            ITkbDatabase       tkbDb,
            INetworkIdAllocator idAllocator,
            int                localNodeId,
            NetworkSpawningSystem spawnSystem,
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
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(_requestSystem);
            registry.RegisterSystem(_spawnSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
