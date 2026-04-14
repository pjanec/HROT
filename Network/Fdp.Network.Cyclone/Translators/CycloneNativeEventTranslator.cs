using System;
using System.Runtime.InteropServices; // For MemoryMarshal
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using FDP.Toolkit.Replication.Services;
using Fdp.Network.Cyclone.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Specialized translator for Unmanaged (Struct) events.
    /// Zero-Allocation, High-Performance.
    /// </summary>
    /// <typeparam name="TEcs">Internal ECS event (unmanaged struct)</typeparam>
    /// <typeparam name="TDds">DDS network event (struct)</typeparam>
    public abstract class CycloneNativeEventTranslator<TEcs, TDds> : IDescriptorTranslator, INetworkReplayTarget
        where TEcs : unmanaged // <--- Supports Structs
        where TDds : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;

        public string TopicName { get; }
        public long DescriptorOrdinal { get; } // Usually not used for events, but required by interface

        protected CycloneNativeEventTranslator(
            DdsParticipant participant, 
            string topicName, 
            NetworkEntityMap entityMap)
        {
            TopicName = topicName;
            // Arbitrary ordinal or hash, events are usually looked up by Type, 
            // but ReplaySystem needs an ordinal to find the translator.
            DescriptorOrdinal = topicName.GetHashCode(); 
            
            EntityMap = entityMap;
            Reader = new DdsReader<TDds>(participant);
            Writer = new DdsWriter<TDds>(participant);
        }

        // =================================================================
        // INGRESS: Network -> ECS (Zero Alloc)
        // =================================================================
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = Reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                // Decode directly from DDS sample to ECS struct
                if (TryDecode(sample.Data, out TEcs ecsEvent))
                {
                    // Call the Unmanaged Publish (Fast path) via CommandBuffer
                    // This is safer than casting View to Repo, as CMD is designed for structural/event changes
                    cmd.PublishEvent(ecsEvent);
                }
            }
        }

        // =================================================================
        // EGRESS: ECS -> Network (Zero Alloc)
        // =================================================================
        public void ScanAndPublish(ISimulationView view)
        {
            // Get Span of events (Zero Copy)
            var events = view.ConsumeEvents<TEcs>();

            foreach (ref readonly var evt in events)
            {
                if (TryEncode(evt, out TDds ddsEvent))
                {
                    Writer.Write(ddsEvent);
                }
            }
        }

        // =================================================================
        // REPLAY INJECTION (Zero Alloc)
        // =================================================================
        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // Replay injection logic
            // 1. Cast bytes to DDS Structs
            var samples = MemoryMarshal.Cast<byte, TDds>(rawData);

            foreach (ref readonly var sample in samples)
            {
                if (TryDecode(sample, out TEcs ecsEvent))
                {
                    cmd.PublishEvent(ecsEvent);
                }
            }
        }

        // Logic to implement in specific classes
        protected abstract bool TryDecode(in TDds input, out TEcs output);
        protected abstract bool TryEncode(in TEcs input, out TDds output);

        // Events don't need ApplyToEntity or Dispose
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
