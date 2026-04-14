using Xunit;
using FDP.Toolkit.Replication.Utilities;

namespace FDP.Toolkit.Replication.Tests.Utilities
{
    public class MultiInstanceLayoutTests
    {
        struct MultiData
        {
            public long EntityId;
            public long InstanceId;
            public float X;
        }

        struct IncompleteData
        {
            public long EntityId;
            public float X;
        }

        [Fact]
        public void ReadIds_ValidStruct_ReturnsCorrectValues()
        {
            var data = new MultiData { EntityId = 12345, InstanceId = 99, X = 1.0f };
            unsafe
            {
                long eid = MultiInstanceLayout<MultiData>.ReadEntityId(&data);
                long iid = MultiInstanceLayout<MultiData>.ReadInstanceId(&data);
                Assert.Equal(12345, eid);
                Assert.Equal(99, iid);
            }
        }

        [Fact]
        public void WriteIds_ValidStruct_ModifiesCorrectly()
        {
            var data = new MultiData { EntityId = 0, InstanceId = 0, X = 1.0f };
            unsafe
            {
                MultiInstanceLayout<MultiData>.WriteEntityId(&data, 55555);
                MultiInstanceLayout<MultiData>.WriteInstanceId(&data, 777);
            }
            Assert.Equal(55555, data.EntityId);
            Assert.Equal(777, data.InstanceId);
            Assert.Equal(1.0f, data.X);
        }

        [Fact]
        public void IsValid_StructWithOnlyEntityId_ReturnsFalse()
        {
            Assert.False(MultiInstanceLayout<IncompleteData>.IsValid);
        }
    }
}
