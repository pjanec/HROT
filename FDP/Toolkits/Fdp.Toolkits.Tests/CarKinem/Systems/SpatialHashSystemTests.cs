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
    }
}
