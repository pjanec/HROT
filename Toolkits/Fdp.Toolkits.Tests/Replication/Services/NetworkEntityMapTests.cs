using System;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Replication.Tests.Services
{
    public class NetworkEntityMapTests
    {
        [Fact]
        public void Register_And_Get_Works()
        {
            var map = new NetworkEntityMap();
            var entity = new Entity(123);
            long netId = 1001;

            map.Register(netId, entity);

            Assert.True(map.TryGetEntity(netId, out var resultEntity));
            Assert.Equal(entity, resultEntity);

            Assert.True(map.TryGetNetworkId(entity, out var resultNetId));
            Assert.Equal(netId, resultNetId);
        }

        [Fact]
        public void Unregister_MovesToGraveyard()
        {
            var map = new NetworkEntityMap(graveyardDurationFrames: 10);
            var entity = new Entity(123);
            long netId = 1001;
            uint frame = 50;

            map.Register(netId, entity);
            map.Unregister(netId, frame);

            Assert.False(map.TryGetEntity(netId, out _));
            Assert.True(map.IsGraveyard(netId));
        }

        [Fact]
        public void PruneGraveyard_RemovesOldEntries()
        {
            var map = new NetworkEntityMap(graveyardDurationFrames: 10);
            long netId = 1001;
            uint deathFrame = 50;
            
            // Manually simulate unregister (or just expose AddToGraveyard via internal/test helper, 
            // but public Unregister is easy enough if we register first)
            map.Register(netId, new Entity(1));
            map.Unregister(netId, deathFrame);

            // Check
            Assert.True(map.IsGraveyard(netId));
            
            // Frame 55 (diff 5) -> Should keep
            map.PruneGraveyard(55);
            Assert.True(map.IsGraveyard(netId));
            
            // Frame 61 (diff 11 > 10) -> Should remove
            map.PruneGraveyard(61);
            Assert.False(map.IsGraveyard(netId));
        }

        [Fact]
        public void Register_ReusesGraveyardId_RemovesFromGraveyard()
        {
            var map = new NetworkEntityMap();
            long netId = 1001;
            map.Register(netId, new Entity(1));
            map.Unregister(netId, 10);
            
            Assert.True(map.IsGraveyard(netId));
            
            // Reuse ID
            map.Register(netId, new Entity(2));
            
            Assert.False(map.IsGraveyard(netId));
            Assert.True(map.TryGetEntity(netId, out var e));
            Assert.Equal(new Entity(2), e);
        }
    }
}
