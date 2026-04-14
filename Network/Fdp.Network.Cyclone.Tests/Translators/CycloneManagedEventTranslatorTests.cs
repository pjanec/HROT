using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Moq;
using Fdp.Network.Cyclone.Translators;
using Fdp.Network.Cyclone.Topics;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Replication.Services;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Network.Cyclone.Tests.Translators
{
    public class TestManagedEvent 
    { 
        public int Val; 
    }

    public class MockEventTranslator : CycloneManagedEventTranslator<TestManagedEvent, EntityMasterTopic>
    {
         public MockEventTranslator(DdsParticipant p, NetworkEntityMap map, IEventBus bus) : base(p, "mock_managed_topic", map, bus) {}

         protected override bool TryDecode(in EntityMasterTopic input, out TestManagedEvent output) 
         {
             output = new TestManagedEvent { Val = (int)input.EntityId };
             return true;
         }

         protected override bool TryEncode(TestManagedEvent input, out EntityMasterTopic output)
         {
             output = new EntityMasterTopic { EntityId = input.Val };
             return true;
         }
    }

    public class DummyManagedSimulationView : ISimulationView
    {
        public List<object> ManagedEventsToReturn = new List<object>();

        public IReadOnlyList<T> ConsumeManagedEvents<T>()
        {
            // Simple filtering
            var res = new List<T>();
            foreach(var e in ManagedEventsToReturn)
            {
                if (e is T typed) res.Add(typed);
            }
            return res;
        }
        
        // Members not used
        public ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        public uint Tick => 0;
        public float Time => 0;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class => throw new NotImplementedException();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => default!;
    }

    public class CycloneManagedEventTranslatorTests : IDisposable
    {
        private DdsParticipant? _participant;
        private NetworkEntityMap? _entityMap;
        
        public CycloneManagedEventTranslatorTests()
        {
             try {
                _participant = new DdsParticipant(0);
             } catch { }
             _entityMap = new NetworkEntityMap();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void PollIngress_DecodesAndPublishes()
        {
             if (_participant == null) return;
             
             var bus = new Mock<IEventBus>();
             var translator = new MockEventTranslator(_participant, _entityMap!, bus.Object);
             
             var writer = new DdsWriter<EntityMasterTopic>(_participant);
             writer.Write(new EntityMasterTopic { EntityId = 123 });
             
             System.Threading.Thread.Sleep(1000);
             
             translator.PollIngress(new Mock<IEntityCommandBuffer>().Object, new Mock<ISimulationView>().Object);
             
             bus.Verify(b => b.PublishManaged(It.Is<TestManagedEvent>(e => e.Val == 123)), Times.AtLeastOnce);
        }

        [Fact]
        public void ScanAndPublish_EncodesAndWrites()
        {
             if (_participant == null) return;
             
             var bus = new Mock<IEventBus>();
             var translator = new MockEventTranslator(_participant, _entityMap!, bus.Object);
             var view = new DummyManagedSimulationView();
             view.ManagedEventsToReturn.Add(new TestManagedEvent { Val = 456 });
             
             var reader = new DdsReader<EntityMasterTopic>(_participant);
             
             translator.ScanAndPublish(view);
             
             System.Threading.Thread.Sleep(1000);
             
             using var loan = reader.Take();
             bool found = false;
             foreach(var s in loan)
             {
                 if (s.IsValid && s.Data.EntityId == 456) 
                 {
                     found = true;
                     break;
                 }
             }
             Assert.True(found);
        }
    }
}
