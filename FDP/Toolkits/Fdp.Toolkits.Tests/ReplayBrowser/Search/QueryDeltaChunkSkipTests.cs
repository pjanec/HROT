using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Support;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// SR-T09: QueryDelta correctness gate -- yields only the mutating entity, not stationary ones.
    /// </summary>
    public class QueryDeltaChunkSkipTests : IDisposable
    {
        public QueryDeltaChunkSkipTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // -- SR-T09: QueryDelta yields only the mutating entity ---------------

        [Fact]
        public void SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary()
        {
            // Arrange: 100 stationary entities (HarnessPosition only) + 1 mutating entity
            // (HarnessVelocity). On each delta frame, only the HarnessVelocity entity mutates.
            // QueryDelta with a HarnessVelocity query must yield exactly 1 entity per frame.
            // Direct EntityRepository test (no recording/playback): validates ComponentMask
            // filtering and version tracking in QueryDelta as a core primitive.
            const int stationaryCount = 100;

            var repo = new EntityRepository();
            repo.RegisterComponent<HarnessPosition>();
            repo.RegisterComponent<HarnessVelocity>();

            for (int i = 0; i < stationaryCount; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new HarnessPosition { X = i });
            }

            var mutatingEntity = repo.CreateEntity();
            repo.AddComponent(mutatingEntity, new HarnessVelocity { Vx = 0f });
            int mutatingEntityIndex = mutatingEntity.Index;

            int velocityTypeId = ComponentTypeRegistry.GetId(typeof(HarnessVelocity));
            var velocityQuery  = repo.Query().WithComponentId(velocityTypeId).Build();

            // Advance past the setup phase to establish a stable baseline version.
            repo.Tick();
            uint lastVersion = repo.GlobalVersion;

            // 5 delta frames: mutate only the HarnessVelocity entity each frame.
            for (int frame = 1; frame <= 5; frame++)
            {
                repo.Tick();
                repo.SetComponent(mutatingEntity, new HarnessVelocity { Vx = frame });

                int visitCount        = 0;
                int visitedEntityIndex = -1;

                repo.QueryDelta(velocityQuery, lastVersion, entity =>
                {
                    visitCount++;
                    visitedEntityIndex = entity.Index;
                });

                Assert.Equal(1, visitCount);
                Assert.Equal(mutatingEntityIndex, visitedEntityIndex);

                lastVersion = repo.GlobalVersion;
            }
        }
    }
}
