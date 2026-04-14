using System;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Kernel; // For IEventBus probably
using Fdp.Interfaces; // Or Fdp.Interfaces for IEventBus if it moved
using Fdp.ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Network.Cyclone.Abstractions;

namespace Fdp.ModuleHost.Network.Cyclone.Translators
{
    /// <summary>
    /// Base class for MANAGED event translators (classes).
    /// </summary>
    public abstract class CycloneManagedEventTranslator<TEcs, TDds> : IDescriptorTranslator, INetworkReplayTarget
        where TEcs : class
        where TDds : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;
        protected readonly IEventBus EventBus;

        public string TopicName { get; }
        public long DescriptorOrdinal { get; } 

        protected CycloneManagedEventTranslator(
             DdsParticipant participant, 
             string topicName, 
             NetworkEntityMap entityMap,
             IEventBus eventBus)
        {
             TopicName = topicName;
             DescriptorOrdinal = topicName.GetHashCode();

             EntityMap = entityMap;
             EventBus = eventBus;
             Reader = new DdsReader<TDds>(participant);
             Writer = new DdsWriter<TDds>(participant);
        }

        public virtual void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
             using var loan = Reader.Take();
             foreach(var sample in loan)
             {
                 if(sample.IsValid)
                 {
                     if(TryDecode(sample.Data, out TEcs output))
                     {
                         EventBus.PublishManaged(output);
                     }
                 }
             }
        }


        public void ScanAndPublish(ISimulationView view)
        {
             var events = view.ConsumeManagedEvents<TEcs>();
             foreach(var evt in events)
             {
                  if(TryEncode(evt, out TDds dds)) 
                  {
                      Writer.Write(dds);
                  }
             }
        }

        public void InjectReplayData(ReadOnlySpan<byte> rawData, IEntityCommandBuffer cmd, ISimulationView view)
        {
            var samples = MemoryMarshal.Cast<byte, TDds>(rawData);
            foreach (ref readonly var sample in samples)
            {
                if (TryDecode(sample, out TEcs output))
                {
                    EventBus.PublishManaged(output);
                }
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }

        protected abstract bool TryDecode(in TDds input, out TEcs output);
        protected abstract bool TryEncode(TEcs input, out TDds output);
    }
}
