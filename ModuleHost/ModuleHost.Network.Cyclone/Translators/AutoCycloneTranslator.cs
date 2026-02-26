using System;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Utilities;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Extensions;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Abstractions;

namespace ModuleHost.Network.Cyclone.Translators
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

        public string TopicName { get; }
        public long DescriptorOrdinal { get; }

        public AutoCycloneTranslator(
            DdsParticipant participant, 
            string topicName, 
            int ordinal, 
            NetworkEntityMap entityMap)
        {
            if (!UnsafeLayout<T>.IsValid)
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} must have an EntityId field (long, ulong, int, or uint) for AutoCycloneTranslator. " +
                    $"Use [DdsTopic] attribute on ECS components to make them dual-purpose (ECS + DDS).");

            TopicName = topicName;
            DescriptorOrdinal = ordinal;
            _entityMap = entityMap;

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

            if (_entityMap.TryGetEntity(netId, out Entity entity))
            {
                if (view is EntityRepository repo && repo.HasAuthority<T>(entity))
                {
                    return;
                }


                // Ghost Logic
                if (view.HasManagedComponent<BinaryGhostStore>(entity))
                {
                    // Stash data for ghost
                    var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref data, 1));
                    InternalStashGhostData(entity, bytes, cmd, view);
                }
                else
                {
                    cmd.SetComponent(entity, data);
                }
            }
        }

        // Helper to append data to ghost store
        private void InternalStashGhostData(Entity entity, ReadOnlySpan<byte> data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // Zero-alloc attempt: Try to get managed component directly
            // If view is EntityRepository or supports mutable access
            if (view is EntityRepository repo)
            {
                if (repo.HasManagedComponent<BinaryGhostStore>(entity))
                {
                    var store = ((ISimulationView)repo).GetManagedComponentRO<BinaryGhostStore>(entity);
                    store.StashedData[DescriptorOrdinal] = data.ToArray();
                }
            }
            // Fallback for generic views (assuming GetComponent works for managed types)
            else if (view.HasManagedComponent<BinaryGhostStore>(entity))
            {
                // This might throw if view is read-only for managed types, but standard FDP View usually returns the object ref
                var store = view.GetManagedComponentRO<BinaryGhostStore>(entity);
                store.StashedData[DescriptorOrdinal] = data.ToArray();
            }
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
