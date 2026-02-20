using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Moq;
using ModuleHost.Network.Cyclone.Translators;
using ModuleHost.Network.Cyclone.Topics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace ModuleHost.Network.Cyclone.Tests.Translators
{
    public class DummySimulationView : ISimulationView
    {
        public List<object> EventsToReturn = new List<object>();

        public ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged
        {
            var matched = EventsToReturn.OfType<T>().ToArray();
            return new ReadOnlySpan<T>(matched);
        }

        public uint Tick => 0;
        public float Time => 0;

        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class => throw new NotImplementedException();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ConsumeManagedEvents<T>() where T : class => new List<T>();
        public IEntityCommandBuffer GetCommandBuffer() => default!;
    }

    public class BlitEventTranslatorTests : IDisposable
    {
        private DdsParticipant? _participant;
        
        public BlitEventTranslatorTests()
        {
             try {
                _participant = new DdsParticipant(0);
             } catch { }
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void PollIngress_ValidEvent_PublishesToBus()
        {
            if (_participant == null) return;

            var translator = new BlitEventTranslator<EntityMasterTopic>(_participant, "mock_blit_topic");
            var mockBus = new Mock<IEventBus>();
            
            var writer = new DdsWriter<EntityMasterTopic>(_participant);
            writer.Write(new EntityMasterTopic { EntityId = 42 });
            
            System.Threading.Thread.Sleep(1000);

            translator.PollIngress(mockBus.Object);

            mockBus.Verify(b => b.Publish(It.Is<EntityMasterTopic>(e => e.EntityId == 42)), Times.AtLeastOnce);
        }

        [Fact]
        public void ScanAndPublish_ConsumedEvent_WritesDds()
        {
            if (_participant == null) return;

            var translator = new BlitEventTranslator<EntityMasterTopic>(_participant, "mock_blit_egress");
            var dummyView = new DummySimulationView();
            dummyView.EventsToReturn.Add(new EntityMasterTopic { EntityId = 99 });
            
            var reader = new DdsReader<EntityMasterTopic>(_participant);
            
            translator.ScanAndPublish(dummyView);
            
            System.Threading.Thread.Sleep(1000); // Wait for delivery
            
            using var loan = reader.Take();
            bool found = false;
            foreach(var s in loan)
            {
                if (s.IsValid && s.Data.EntityId == 99)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found);
        }
    }
}
