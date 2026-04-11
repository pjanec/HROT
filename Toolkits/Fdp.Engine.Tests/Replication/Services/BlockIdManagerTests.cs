using System;
using Xunit;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Replication.Tests.Services
{
    public class BlockIdManagerTests
    {
        [Fact]
        public void Allocate_FromBlock_ReturnsCorrectIds()
        {
            var manager = new BlockIdManager();
            manager.AddBlock(100, 3);

            Assert.Equal(100, manager.AllocateId());
            Assert.Equal(101, manager.AllocateId());
            Assert.Equal(102, manager.AllocateId());
        }

        [Fact]
        public void EmptyPool_ThrowsException()
        {
            var manager = new BlockIdManager();
            // No block added
            Assert.Throws<InvalidOperationException>(() => manager.AllocateId());
        }

        [Fact]
        public void LowWaterMark_TriggersCallback()
        {
            var manager = new BlockIdManager(lowWaterMark: 1);
            manager.AddBlock(100, 3); // 100, 101, 102. Count = 3.

            bool eventFired = false;
            manager.OnLowWaterMark += () => eventFired = true;

            // Count 3 -> Alloc -> Count 2. 2 > 1. No event.
            manager.AllocateId(); 
            Assert.False(eventFired);

            // Count 2 -> Alloc -> Count 1. 1 <= 1. Event fires.
            manager.AllocateId();
            Assert.True(eventFired);
        }
    }
}
