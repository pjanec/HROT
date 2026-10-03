using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Spatial;
using CarKinem.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tests.Systems
{
    /// <summary>
    /// Integration tests for <see cref="SpatialHashSystem"/>.
    ///
    /// <para>BATCH-05 Task 2: <see cref="SpatialHashSystem"/> now filters on
    /// <c>PhysicsCollider</c> (component ID <see cref="GlobalComponentIds.PhysicsCollider"/>)
    /// alongside <see cref="SimTransform"/>.  Entities without a physics collider are excluded
    /// from the broadphase grid to avoid unnecessary CPU insertion cost.</para>
    /// </summary>
    public class SpatialHashSystemTests
    {
        /// <summary>
        /// BATCH-05 Task 2: <see cref="SpatialHashSystem"/> indexes entities that have
        /// both <see cref="SimTransform"/> and a <c>PhysicsCollider</c>.
        /// An entity with <see cref="SimTransform"/> but no collider must NOT appear in the grid.
        /// An entity with <see cref="SimTransform"/> AND a collider MUST appear in the grid.
        /// </summary>
        [Fact]
        public void SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState()
        {
            // Arrange
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SpatialGridData>();
            repo.RegisterComponent<PhysicsCollider>();

            var sys = new SpatialHashSystem();

            // Entity WITH collider — must be indexed.
            var collidable = repo.CreateEntity();
            repo.AddComponent(collidable, new SimTransform
            {
                Position = new Vector3(100f, 100f, 0f),
                Rotation = Quaternion.Identity,
            });
            repo.AddComponent(collidable, new PhysicsCollider { Radius = 2.0f });

            // Entity WITHOUT collider — must NOT be indexed (non-collidable camera / waypoint).
            var nonCollidable = repo.CreateEntity();
            repo.AddComponent(nonCollidable, new SimTransform
            {
                Position = new Vector3(100f, 100f, 0f),
                Rotation = Quaternion.Identity,
            });
            // Deliberately NOT adding PhysicsCollider.

            // Act
            sys.Execute(repo, 0.016f);

            // Assert: grid singleton exists.
            Assert.True(repo.HasSingleton<SpatialGridData>(),
                "SpatialHashSystem must publish a SpatialGridData singleton.");

            var gridData = repo.GetSingleton<SpatialGridData>();

            Span<(Entity foundEntity, Vector2 pos)> results =
                stackalloc (Entity, Vector2)[10];
            int count = gridData.Grid.QueryNeighbors(
                new Vector2(100f, 100f), radius: 1f, results);

            // Only the collidable entity is indexed.
            Assert.Equal(1, count);
            Assert.Equal(collidable, results[0].foundEntity);

            // Cleanup
            repo.Dispose();
        }

        /// <summary>
        /// ⭐ CE-3018 (W9) — the collider grid FOLLOWS the resident terrain. An entity outside today's [-750,750)² is
        /// invisible to avoidance until a terrain that contains it is resident; a terrain INSIDE the default extent
        /// changes nothing (same origin, same cell).
        /// </summary>
        [Fact]
        public void ColliderGrid_RebasesToTheResidentTerrain_CE3018()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SpatialGridData>();
            repo.RegisterComponent<PhysicsCollider>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            var sys = new SpatialHashSystem();
            var far = repo.CreateEntity();
            repo.AddComponent(far, new SimTransform { Position = new Vector3(2000f, 100f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(far, new PhysicsCollider { Radius = 1f });

            int Hits()
            {
                Span<(Entity entity, Vector2 pos)> hits = stackalloc (Entity, Vector2)[4];
                return repo.GetSingleton<SpatialGridData>().Grid.QueryNeighbors(new Vector2(2000f, 100f), 1f, hits);
            }

            sys.Execute(repo, 0.016f);
            Assert.Equal(0, Hits());                                   // the defect: silently missing
            var dflt = sys.Geometry;

            repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
                { BoundsMin = new Vector2(0f, 0f), BoundsMax = new Vector2(200f, 200f) });
            sys.Execute(repo, 0.016f);
            Assert.Equal(dflt, sys.Geometry);                          // fits ⇒ unchanged

            repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
                { BoundsMin = new Vector2(0f, 0f), BoundsMax = new Vector2(2500f, 500f) });
            sys.Execute(repo, 0.016f);
            Assert.Equal(1, Hits());
            Assert.Equal(dflt.OriginX, sys.Geometry.OriginX);          // only the side the terrain leaves moved
            Assert.True(sys.Geometry.CellSize > dflt.CellSize);
            repo.Dispose();
        }
    }
}
