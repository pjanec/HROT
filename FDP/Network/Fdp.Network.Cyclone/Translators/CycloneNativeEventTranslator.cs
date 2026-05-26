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
    public abstract class CycloneNativeEventTranslator<TEcs, TDds> : CycloneBaseTranslator, INetworkEventTranslator
        where TEcs : unmanaged // <--- Supports Structs
        where TDds : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;

        protected CycloneNativeEventTranslator(
            DdsParticipant participant, 
            string topicName, 
            NetworkEntityMap entityMap)
            : base(topicName)
        {
            EntityMap = entityMap;
            // participant may be null in unit-test mode — Reader/Writer become no-ops.
            Reader = participant is not null ? new DdsReader<TDds>(participant) : null!;
            Writer = participant is not null ? new DdsWriter<TDds>(participant) : null!;
        }

        // =================================================================
        // INGRESS: Network -> ECS (Zero Alloc)
        // =================================================================
        public override void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (Reader is null) return; // test mode — no DDS participant supplied
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
        public override void ScanAndPublish(ISimulationView view)
        {
            // Get Span of events (Zero Copy)
            var events = view.ReadEvents<TEcs>();

            foreach (ref readonly var evt in events)
            {
                if (TryEncode(evt, out TDds ddsEvent))
                {
                    Writer?.Write(ddsEvent);
                    SentSampleCount++;
                }
            }
        }

        // Logic to implement in specific classes
        protected abstract bool TryDecode(in TDds input, out TEcs output);
        protected abstract bool TryEncode(in TEcs input, out TDds output);
    }
}
