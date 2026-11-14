using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    public class DescriptorOwnershipTests
    {
        [Fact]
        public void CanSetAndGetOwner()
        {
            var ownership = new DescriptorOwnership();
            long packedKey = 100;
            int ownerId = 5;

            ownership = ownership.WithOwner(packedKey, ownerId);

            bool found = ownership.TryGetOwner(packedKey, out int retrievedOwner);
            Assert.True(found);
            Assert.Equal(ownerId, retrievedOwner);
        }

        [Fact]
        public void TryGetOwner_ReturnsFalse_WhenKeyMissing()
        {
            var ownership = new DescriptorOwnership();
            bool found = ownership.TryGetOwner(999, out _);
            Assert.False(found);
        }

        [Fact]
        public void OverwriteOwner_UpdatesValue()
        {
            var ownership = new DescriptorOwnership();
            long packedKey = 100;
            
            ownership = ownership.WithOwner(packedKey, 1);
            ownership = ownership.WithOwner(packedKey, 2);

            ownership.TryGetOwner(packedKey, out int owner);
            Assert.Equal(2, owner);
        }
    }
}