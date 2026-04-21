using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Interfaces;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Specialized translator for Unmanaged (Struct) events.
    /// Zero-Allocation, High-Performance.
    /// </summary>
    /// <typeparam name="TEcs">Internal ECS event (unmanaged struct)</typeparam>
    /// <typeparam name="TDds">DDS network event (struct)</typeparam>
    public abstract class CycloneNativeEventTranslator<TEcs, TDds> : IDescriptorTranslator
        where TEcs : unmanaged // <--- Supports Structs
        where TDds : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;

        public string TopicName { get; }
        public long DescriptorOrdinal { get; } // Usually not used for events, but required by interface
        public long ReceivedSampleCount { get; protected set; }
        public long SentSampleCount { get; protected set; }
        public abstract TranslatorDirection Direction { get; }

        protected CycloneNativeEventTranslator(
            DdsParticipant participant, 
            string topicName, 
            NetworkEntityMap entityMap)
        {
            TopicName = topicName;
            
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
                ReceivedSampleCount++;

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
            var events = view.ReadEvents<TEcs>();

            foreach (ref readonly var evt in events)
            {
                if (TryEncode(evt, out TDds ddsEvent))
                {
                    Writer.Write(ddsEvent);
                    SentSampleCount++;
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
