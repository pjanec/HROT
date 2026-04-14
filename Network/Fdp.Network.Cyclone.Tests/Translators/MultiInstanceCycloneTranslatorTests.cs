using System;
using System.Runtime.InteropServices;
using Xunit;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Fdp.Network.Cyclone.Translators;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Kernel;

namespace Fdp.Network.Cyclone.Tests.Translators
{
    [StructLayout(LayoutKind.Sequential)]
    [DdsTopic("TestTopic_Multi_1")]
    public partial struct MultiInstanceTestComponent
    {
        [DdsKey, DdsId(0)]
        public long EntityId;
        [DdsKey, DdsId(1)]
        public long InstanceId;
        [DdsId(2)]
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InvalidMultiTestComponent
    {
        public long EntityId;
        // Missing InstanceId field
        public int Value;
    }

    public class MultiInstanceCycloneTranslatorTests : IDisposable
    {
        private DdsParticipant _participant;
        private NetworkEntityMap _entityMap;
        private EntityRepository _repo;

        public MultiInstanceCycloneTranslatorTests()
        {
            _participant = new DdsParticipant(0);
            _entityMap = new NetworkEntityMap();
            _repo = new EntityRepository();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public void Constructor_ValidatesLayout()
        {
            var translator = new MultiInstanceCycloneTranslator<MultiInstanceTestComponent>(
                _participant, "TestTopic_Multi_1", 1, _entityMap, new GhostCreationSystem(_entityMap));
            
            Assert.NotNull(translator);
        }

        [Fact]
        public void Constructor_Throws_OnInvalidLayout()
        {
            Assert.Throws<InvalidOperationException>(() => {
                new MultiInstanceCycloneTranslator<InvalidMultiTestComponent>(
                    _participant, "TestTopic_Multi_Invalid", 2, _entityMap, new GhostCreationSystem(_entityMap));
            });
        }
    }
}
