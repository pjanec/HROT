using System;
using System.Runtime.InteropServices; // Required for MemoryMarshal
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Network.Cyclone.Abstractions;

namespace Fdp.ModuleHost.Network.Cyclone.Translators
{
    /// <summary>
    /// Base class for high-performance translators using typed DDS readers/writers.
    /// Eliminates boxing and reflection from hot paths.
    /// </summary>
    /// <typeparam name="TDds">DDS topic struct type</typeparam>
    /// <typeparam name="TView">DDS view type (ref struct from code generator)</typeparam>
    public abstract unsafe class CycloneTranslator<TDds, TView> : IDescriptorTranslator, INetworkReplayTarget
        where TDds : unmanaged 
        where TView : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;

        public string TopicName { get; }
        public long DescriptorOrdinal { get; }

        protected CycloneTranslator(
            DdsParticipant? participant, 
            string topicName, 
            long ordinal,
            NetworkEntityMap entityMap)
        {
            TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
            DescriptorOrdinal = ordinal;
            EntityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));

            // participant may be null in unit-test mode — Reader/Writer become no-ops.
            Reader = participant is not null ? new DdsReader<TDds>(participant) : null!;
            Writer = participant is not null ? new DdsWriter<TDds>(participant) : null!;
        }

        /// <summary>
        /// High-performance dispose that patches keys without reflection.
        /// Default implementation disposes instance 0 (Root).
        /// </summary>
        public virtual void Dispose(long networkEntityId)
        {
            DisposeInstance(networkEntityId, 0);
        }

        /// <summary>
        /// Helper to dispose specific instance.
        /// </summary>
        protected void DisposeInstance(long entityId, long instanceId)
        {
            // 1. Stack Allocation (Zero GC, Instant)
            TDds keySample = default;

            // 2. Patch EntityId
            if (UnsafeLayout<TDds>.IsValid)
            {
                UnsafeLayout<TDds>.WriteId(&keySample, entityId);
            }

            // 3. Patch InstanceId (If the topic supports it)
            if (MultiInstanceLayout<TDds>.IsValid)
            {
                MultiInstanceLayout<TDds>.WriteInstanceId(&keySample, instanceId);
            }

            // 4. Call Cyclone Native Dispose
            Writer.DisposeInstance(keySample);
        }

        /// <summary>
        /// Ingress: Poll DDS and decode samples into ECS commands.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (Reader is null) return; // test mode — no DDS participant supplied

            using var loan = Reader.Take();
            
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                
                // Delegate to specific decode logic
                Decode(sample.Data, cmd, view);
            }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // 1. Zero-Copy Cast: Bytes -> Structs
            // This is safe because TDds is unmanaged
            var samples = MemoryMarshal.Cast<byte, TDds>(rawData);

            // 2. Iterate and Decode
            foreach (ref readonly var sample in samples)
            {
                // We reuse the EXACT same decoding logic as the live network
                Decode(sample, cmd, view);
            }
        }

        /// <summary>
        /// Egress: Scan ECS and publish samples to DDS.
        /// </summary>
        public abstract void ScanAndPublish(ISimulationView view);

        /// <summary>
        /// Decode single DDS sample into ECS command(s).
        /// Override this for custom ingress logic.
        /// </summary>
        protected abstract void Decode(in TDds data, IEntityCommandBuffer cmd, ISimulationView view);

        public abstract void ApplyToEntity(Entity entity, object data, EntityRepository repo);

        /// <summary>
        /// Publishes a sample to the DDS writer. 
        /// Override to hook publication in tests.
        /// </summary>
        protected virtual void Publish(in TDds sample)
        {
            Writer.Write(sample);
        }
    }
}
