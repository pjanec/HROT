using Xunit;
using Fdp.Core;
using System.Linq;

namespace Fdp.Tests
{
    public class EntityQueryLifecycleTests
    {
        private EntityRepository CreateRepository()
        {
            return new EntityRepository();
        }

        [Fact]
        public void EntityQuery_DefaultFilter_OnlyActive()
        {
            using var repo = CreateRepository();
            
            var entity1 = repo.CreateEntity(); // Active
            var entity2 = repo.CreateEntity();
            repo.SetLifecycleState(entity2, EntityLifecycle.Constructing);
            
            var query = repo.Query().Build();
            
            bool found1 = false;
            bool found2 = false;
            foreach (var e in query)
            {
                if (e.Equals(entity1)) found1 = true;
                if (e.Equals(entity2)) found2 = true;
            }
            
            Assert.True(found1, "Entity1 (Active) should be found");
            Assert.False(found2, "Entity2 (Constructing) should NOT be found");
        }
        
        [Fact]
        public void EntityQuery_IncludeConstructing_ReturnsStaging()
        {
            using var repo = CreateRepository();
            
            var entity1 = repo.CreateEntity();
            repo.SetLifecycleState(entity1, EntityLifecycle.Constructing);
            
            var query = repo.Query().IncludeConstructing().Build();
            
            bool found = false;
            foreach (var e in query)
            {
                if (e.Equals(entity1)) found = true;
            }
            Assert.True(found);
        }
        
        /// <summary>
        /// ⭐ CE-4007 / CE-4000 residual (docs/DESIGN_Cluster_Load_Phase.md §8.3) — <c>AllButTearDown</c>: what an egress publishes from — a
        /// constructing entity yes (it must announce itself), a dying one never (its delete may already be on the wire). Both the enumerator and
        /// <c>Matches</c> apply it. 🔴 Red-proof: make <c>LifecyclePasses</c> treat AllButTearDown like All ⇒ the TearDown entity is found.
        /// </summary>
        [Fact]
        public void EntityQuery_AllButTearDown_HasConstructingAndActive_NeverTearDown()
        {
            using var repo = CreateRepository();
            var active = repo.CreateEntity();
            var constructing = repo.CreateEntity();
            repo.SetLifecycleState(constructing, EntityLifecycle.Constructing);
            var dying = repo.CreateEntity();
            repo.SetLifecycleState(dying, EntityLifecycle.TearDown);

            var query = repo.Query().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
            var found = new System.Collections.Generic.List<Entity>();
            foreach (var e in query) found.Add(e);

            Assert.Contains(active, found);
            Assert.Contains(constructing, found);
            Assert.DoesNotContain(dying, found);
            Assert.False(query.Matches(default, new EntityMetadataCold { LifecycleState = EntityLifecycle.TearDown }));
        }

        [Fact]
        public void EntityQuery_IncludeAll_ReturnsEverything()
        {
            using var repo = CreateRepository();
            
            var active = repo.CreateEntity();
            var constructing = repo.CreateEntity();
            var tearDown = repo.CreateEntity();
            
            repo.SetLifecycleState(constructing, EntityLifecycle.Constructing);
            repo.SetLifecycleState(tearDown, EntityLifecycle.TearDown);
            
            var query = repo.Query().IncludeAll().Build();
            var list = query.GetEnumerator();
            
            int count = 0;
            while(list.MoveNext()) count++;
            
            Assert.Equal(3, count);
        }
    }
}
