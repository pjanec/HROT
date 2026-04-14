using System;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using CycloneDDS.Runtime;
using Fdp.Network.Cyclone.Translators;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Topics;

namespace Fdp.Network.Cyclone.Tests.Translators
{
    public class AutoCycloneTranslatorTests : IDisposable
    {
        private DdsParticipant _participant;
        private NetworkEntityMap _entityMap;

        public AutoCycloneTranslatorTests()
        {
            // Use domain 100 to avoid collision with other tests if possible, though mostly isolated by topic name usually
            _participant = new DdsParticipant(0); 
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void Constructor_ValidatesLayout()
        {
            // Should succeed for EntityMasterTopic (has long EntityId at offset 0)
            // Note: EntityMasterTopic is a valid DDS Topic defined in Network.Cyclone
            var translator = new AutoCycloneTranslator<EntityMasterTopic>(
                _participant, "SST_EntityMaster", 1, _entityMap,
                new GhostCreationSystem(_entityMap));
            
            Assert.NotNull(translator);
        }

        [Fact]
        public void Constructor_Throws_OnInvalidLayout()
        {
            Assert.Throws<InvalidOperationException>(() => {
                new AutoCycloneTranslator<int>(
                    _participant, "TestTopic_Auto_Invalid", 2, _entityMap,
                    new GhostCreationSystem(_entityMap));
            });
        }
    }
}
