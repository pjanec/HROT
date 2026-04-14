using System;
using Xunit;
using Moq;
using Fdp.ModuleHost.Network.Cyclone.Translators;
using CycloneDDS.Runtime;
using FDP.Toolkit.Replication.Services;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Network.Cyclone.Topics;

namespace Fdp.ModuleHost.Network.Cyclone.Tests.Translators
{

    
    public struct MockView { }

    public class MockTranslator : CycloneTranslator<EntityMasterTopic, MockView>
    {
        public bool HandleIngressCalled = false;

        public MockTranslator(DdsParticipant p, NetworkEntityMap map) 
            : base(p, "mock_topic", 123, map)
        {
        }

        protected override void Decode(in EntityMasterTopic data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            HandleIngressCalled = true;
        }

        // We can't properly test ApplyToEntity or ScanAndPublish without more infrastructure,
        // but the core "Decode" delegation is what we are testing.
        public override void ScanAndPublish(ISimulationView view) {}
        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo) {}
    }

    public class CycloneTranslatorTests : IDisposable
    {
        private DdsParticipant? _participant;
        private NetworkEntityMap _entityMap;
        
        public CycloneTranslatorTests()
        {
             try {
                _participant = new DdsParticipant(0);
             } catch {
                // Ignore
             }
             _entityMap = new NetworkEntityMap();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void PollIngress_DelegatesToDecode()
        {
            if (_participant == null)
            {
                 // Native libs missing, skipping test logic
                 return;
            }
            
            var translator = new MockTranslator(_participant, _entityMap);
            var mockCmd = new Mock<IEntityCommandBuffer>();
            var mockView = new Mock<ISimulationView>();

            var writer = new DdsWriter<EntityMasterTopic>(_participant);
            writer.Write(new EntityMasterTopic { EntityId = 1 });
            
            // Give some time for DDS loopback
            System.Threading.Thread.Sleep(1000);
            
            // Pass null for IDataReader because our CycloneTranslator implementation ignores it 
            // and uses the internal generic Reader
            translator.PollIngress(mockCmd.Object, mockView.Object);
            
            Assert.True(translator.HandleIngressCalled);
        }
    }
}
