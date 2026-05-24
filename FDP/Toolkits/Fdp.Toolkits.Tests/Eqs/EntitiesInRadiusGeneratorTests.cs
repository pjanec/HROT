using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EntitiesInRadiusGenerator"/> (TASK-EQS-009).
    /// Uses a manually constructed <see cref="SpatialHashGrid"/> -- no EditorHarness needed.
    /// </summary>
    public class EntitiesInRadiusGeneratorTests : System.IDisposable
    {
        private readonly EntityRepository _repo;
        private SpatialHashGrid _grid;

        public EntitiesInRadiusGeneratorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<SpatialGridData>();

            // 100x100 world, 5 m cell, up to 256 entities.
            _grid = SpatialHashGrid.Create(100, 100, 5f, 256, Allocator.Persistent);
            _grid.Clear();
        }

        public void Dispose()
        {
            _grid.Dispose();
            _repo.Dispose();
        }

        // Helper: create observer in repo with SimTransform and add to grid.
        private Entity CreateObserver(Vector2 pos)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new SimTransform
            {
                Position = new Vector3(pos.X, pos.Y, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });
            _grid.Add(e, pos);
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });
            return e;
        }

        // Helper: create a dummy entity in the grid only (not in repo).
        // grid is passed by ref so EntityCount is updated on the caller's struct.
        private static Entity CreateGridEntity(ref SpatialHashGrid grid, int index, Vector2 pos)
        {
            var e = new Entity(index, 1);
            grid.Add(e, pos);
            return e;
        }

        // T-EQS-009-1: Zero radius returns 0 candidates.
        [Fact]
        public void Generate_ZeroRadius_ReturnsZero()
        {
            var observer = CreateObserver(Vector2.Zero);
            CreateGridEntity(ref _grid, 10, new Vector2(1f, 0f));
            CreateGridEntity(ref _grid, 11, new Vector2(2f, 0f));
            CreateGridEntity(ref _grid, 12, new Vector2(3f, 0f));
            CreateGridEntity(ref _grid, 13, new Vector2(4f, 0f));
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });

            var sensor     = new EqsSensor { SearchRadius = 0f };
            var candidates = new EqsResult[16];
            var gen        = new EntitiesInRadiusGenerator();

            int count = gen.Generate(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(0, count);
        }

        // T-EQS-009-2: Observer entity is excluded from results.
        [Fact]
        public void Generate_ObserverExcluded_ResultCountEqualsOtherEntities()
        {
            var observer = CreateObserver(Vector2.Zero);

            // 3 other entities at distances 2, 4, 6 -- all within radius 10.
            CreateGridEntity(ref _grid, 20, new Vector2(2f, 0f));
            CreateGridEntity(ref _grid, 21, new Vector2(4f, 0f));
            CreateGridEntity(ref _grid, 22, new Vector2(6f, 0f));
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });

            var sensor     = new EqsSensor { SearchRadius = 10f };
            var candidates = new EqsResult[16];
            var gen        = new EntitiesInRadiusGenerator();

            int count = gen.Generate(observer, ref sensor, _repo, candidates.AsSpan());

            // Only the 3 non-observer entities should be returned.
            Assert.Equal(3, count);
        }

        // T-EQS-009-3: Only entities within radius are returned.
        [Fact]
        public void Generate_WithRadius_ReturnsOnlyEntitiesWithinRadius()
        {
            var observer = CreateObserver(Vector2.Zero);

            // One entity at distance 3 (inside radius 10), one at distance 15 (outside).
            CreateGridEntity(ref _grid, 30, new Vector2(3f, 0f));
            CreateGridEntity(ref _grid, 31, new Vector2(15f, 0f));
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });

            var sensor     = new EqsSensor { SearchRadius = 10f };
            var candidates = new EqsResult[16];
            var gen        = new EntitiesInRadiusGenerator();

            int count = gen.Generate(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(1, count);
            // The result should be the entity at distance 3.
            Assert.True(System.Math.Abs(candidates[0].PositionX - 3f) < 0.001f);
        }
    }
}
