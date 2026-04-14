using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Utilities;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Abstractions;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Zero-boilerplate translator for simple 1:1 mappings.
    /// Requires: DDS type == ECS type, only EntityId needs patching.
    /// </summary>
    public unsafe class AutoCycloneTranslator<T> : IDescriptorTranslator, INetworkReplayTarget
        where T : unmanaged
    {
        private readonly DdsReader<T> _reader;
        private readonly DdsWriter<T> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly HashSet<long> _tracedNetIds = new();

        public string TopicName { get; }
        public long DescriptorOrdinal { get; }

        public AutoCycloneTranslator(
            DdsParticipant participant, 
            string topicName, 
            int ordinal, 
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem)
        {
            if (!UnsafeLayout<T>.IsValid)
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} must have an EntityId field (long, ulong, int, or uint) for AutoCycloneTranslator. " +
                    $"Use [DdsTopic] attribute on ECS components to make them dual-purpose (ECS + DDS).");

            TopicName = topicName;
            DescriptorOrdinal = ordinal;
            _entityMap = entityMap;
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));

            _reader = new DdsReader<T>(participant);
            _writer = new DdsWriter<T>(participant);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();
            
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                T data = sample.Data;
                ProcessSample(data, cmd, view);
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var samples = MemoryMarshal.Cast<byte, T>(rawData);
            foreach (ref readonly var sample in samples)
            {
                ProcessSample(sample, cmd, view);
            }
        }

        private void ProcessSample(T data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = UnsafeLayout<T>.ReadId(&data);

            if (!_entityMap.TryGetEntity(netId, out Entity entity))
            {
                // Entity not yet known — create a ghost so the promotion pipeline
                // can drive it into the Constructing state once all mandatory
                // descriptors have arrived.
                var repo = view as EntityRepository;
                if (repo == null) return;
                entity = _ghostCreationSystem.CreateGhost(repo, netId, view.Tick);
            }
            else
            {
                // Skip if this node has authority over the component (we are the source).
                if (view is EntityRepository ownedRepo && ownedRepo.HasAuthority<T>(entity))
                    return;
            }

            cmd.SetComponent(entity, data);
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<T>()
                .With<NetworkIdentity>()
                .WithOwned<T>()
                .Build();

            foreach (var entity in query)
            {
                ref readonly var component = ref view.GetComponentRO<T>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                T copy = component;
                UnsafeLayout<T>.WriteId(&copy, netId.Value);

                if (_tracedNetIds.Add(netId.Value))
                {
                    FdpLog<AutoCycloneTranslator<T>>.Debug(
                        "[TRACE-EGRESS] Publishing {0} for NetID={1}", TopicName, netId.Value);
                }

                _writer.Write(copy);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            T keySample = default;
            UnsafeLayout<T>.WriteId(&keySample, networkEntityId);
            _writer.DisposeInstance(keySample);
        }
    }
}
