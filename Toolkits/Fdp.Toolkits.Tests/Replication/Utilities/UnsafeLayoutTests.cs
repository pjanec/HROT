using Xunit;
using Fdp.Toolkit.Replication.Utilities;

namespace Fdp.Toolkit.Replication.Tests.Utilities
{
    public class UnsafeLayoutTests
    {
        struct TestData
        {
            public long EntityId;
            public float X;
        }

        struct InvalidData
        {
            public float X;
        }

        struct InvalidDataInt
        {
            public int EntityId;
        }

        [Fact]
        public void ReadId_ValidStruct_ReturnsCorrectValue()
        {
            var data = new TestData { EntityId = 12345, X = 1.0f };
            unsafe
            {
                long id = UnsafeLayout<TestData>.ReadId(&data);
                Assert.Equal(12345, id);
            }
        }

        [Fact]
        public void WriteId_ValidStruct_ModifiesCorrectly()
        {
            var data = new TestData { EntityId = 0, X = 1.0f };
            unsafe
            {
                UnsafeLayout<TestData>.WriteId(&data, 99999);
            }
            Assert.Equal(99999, data.EntityId);
            Assert.Equal(1.0f, data.X); // Other fields unchanged
        }

        [Fact]
        public void IsValid_StructWithoutEntityId_ReturnsFalse()
        {
            Assert.False(UnsafeLayout<InvalidData>.IsValid);
        }

        [Fact]
        public void IsValid_StructWithIntEntityId_ReturnsTrue()
        {
            Assert.True(UnsafeLayout<InvalidDataInt>.IsValid);
        }
    }
}
