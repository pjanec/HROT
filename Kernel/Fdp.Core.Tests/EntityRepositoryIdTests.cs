using Xunit;
using Fdp.Kernel;
using System.Threading;
using System;

namespace Fdp.Tests
{
    public class EntityRepositoryIdTests : IDisposable
    {
        private EntityRepository _repo;
        private NativeEventStream<EntityLifecycleEvent> _lifecycleStream;

        public EntityRepositoryIdTests()
        {
            _repo = new EntityRepository();
            _lifecycleStream = new NativeEventStream<EntityLifecycleEvent>(1024);
            _repo.RegisterLifecycleStream(_lifecycleStream);
        }

        public void Dispose()
        {
            _repo?.Dispose();
            _lifecycleStream?.Dispose();
        }

        [Fact]
        public void ReserveIdRange_PreventsCollision()
        {
            // Reserve range 0-1000
            _repo.ReserveIdRange(1000);

            // Create an entity
            var entity = _repo.CreateEntity();

            // Should be > 1000
            Assert.True(entity.Index > 1000);
        }

        [Fact]
        public void ReserveIdRange_MultipleCalls()
        {
            _repo.ReserveIdRange(1000);
            _repo.ReserveIdRange(2000); // Should bump to 2000

            var entity = _repo.CreateEntity();
            Assert.True(entity.Index > 2000);

            // Call with lower value should be ignored
            _repo.ReserveIdRange(1000);
            var entity2 = _repo.CreateEntity();
            Assert.True(entity2.Index > entity.Index);
        }

        [Fact]
        public void HydrateEntity_CreatesAtSpecificId()
        {
            int targetId = 5000;
            int targetGen = 42;

            var entity = _repo.HydrateEntity(targetId, targetGen);

            Assert.Equal(targetId, entity.Index);
            Assert.Equal(targetGen, entity.Generation);
            Assert.True(_repo.IsAlive(entity));
        }

        [Fact]
        public void HydrateEntity_EmitsLifecycleEvent()
        {
             var entity = _repo.HydrateEntity(100, 5);
             
             _lifecycleStream.Swap();
             var events = _lifecycleStream.Read();
             
             Assert.Single(events.ToArray());
             var evt = events[0];
             Assert.Equal(LifecycleEventType.Created, evt.Type);
             Assert.Equal(entity, evt.Entity);
        }
    }
}
