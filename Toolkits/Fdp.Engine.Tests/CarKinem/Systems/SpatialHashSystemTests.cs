using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Spatial;
using CarKinem.Systems;
using Fdp.Kernel;
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
    ///
    /// <para>Test uses a local stub struct with <c>[ComponentId(GlobalComponentIds.PhysicsCollider)]</c>
    /// to avoid a circular project dependency (CarKinem.Tests cannot reference
    /// FDP.Toolkit.Physics without creating a cycle).</para>
    /// </summary>
    public class SpatialHashSystemTests
    {
        // ── Stub component sharing the PhysicsCollider component-ID slot ───────
        // CarKinem.Tests cannot reference FDP.Toolkit.Physics (circular dependency),
        // so we declare a local struct with the same [ComponentId] constant.
        // The EntityRepository maps both types to slot 40 — semantically identical.
        [ComponentId(GlobalComponentIds.PhysicsCollider)]
        private struct PhysicsCollidableStub
        {
            public float Radius;
        }

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
            repo.RegisterComponent<PhysicsCollidableStub>(); // maps to component ID 40

            var sys = new SpatialHashSystem();
            sys.Create(repo);

            // Entity WITH collider — must be indexed.
            var collidable = repo.CreateEntity();
            repo.AddComponent(collidable, new SimTransform
            {
                Position = new Vector3(100f, 100f, 0f),
                Rotation = Quaternion.Identity,
            });
            repo.AddComponent(collidable, new PhysicsCollidableStub { Radius = 2.0f });

            // Entity WITHOUT collider — must NOT be indexed (non-collidable camera / waypoint).
            var nonCollidable = repo.CreateEntity();
            repo.AddComponent(nonCollidable, new SimTransform
            {
                Position = new Vector3(100f, 100f, 0f),
                Rotation = Quaternion.Identity,
            });
            // Deliberately NOT adding PhysicsCollidableStub.

            // Act
            sys.Run();

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
            sys.Dispose();
            repo.Dispose();
        }
    }
}
