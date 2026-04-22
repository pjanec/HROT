using System;
using CycloneDDS.Runtime;
using Fdp.Core; // For IEventBus probably
using Fdp.Interfaces; // Or Fdp.Interfaces for IEventBus if it moved
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Base class for MANAGED event translators (classes).
    /// </summary>
    public abstract class CycloneManagedEventTranslator<TEcs, TDds> : CycloneBaseTranslator, INetworkEventTranslator
        where TEcs : class
        where TDds : struct
    {
        protected readonly DdsReader<TDds> Reader;
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;
        protected readonly IEventBus EventBus;

        protected CycloneManagedEventTranslator(
             DdsParticipant participant, 
             string topicName, 
             NetworkEntityMap entityMap,
             IEventBus eventBus)
             : base(topicName)
        {
             EntityMap = entityMap;
             EventBus = eventBus;
             Reader = new DdsReader<TDds>(participant);
             Writer = new DdsWriter<TDds>(participant);
        }

        public override void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
             using var loan = Reader.Take();
             foreach(var sample in loan)
             {
                 if(sample.IsValid)
                 {
                     ReceivedSampleCount++;
                     if(TryDecode(sample.Data, out TEcs output))
                     {
                         EventBus.PublishManaged(output);
                     }
                 }
             }
        }


        public override void ScanAndPublish(ISimulationView view)
        {
             var events = view.ReadManagedEvents<TEcs>();
             foreach(var evt in events)
             {
                  if(TryEncode(evt, out TDds dds)) 
                  {
                      Writer.Write(dds);
                      SentSampleCount++;
                  }
             }
        }

        protected abstract bool TryDecode(in TDds input, out TEcs output);
        protected abstract bool TryEncode(TEcs input, out TDds output);
    }
}
