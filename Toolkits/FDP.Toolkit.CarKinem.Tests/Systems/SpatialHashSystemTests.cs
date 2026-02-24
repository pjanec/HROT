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
    /// Specifically validates DEBT-001: that <see cref="SpatialHashSystem"/> indexes
    /// entities that have <see cref="SimTransform"/> but <b>no</b> <see cref="VehicleState"/>,
    /// confirming that Perception entities are correctly included in the spatial grid.
    /// </summary>
    public class SpatialHashSystemTests
    {
        /// <summary>
        /// DEBT-001: <see cref="SpatialHashSystem"/> queries all entities with
        /// <see cref="SimTransform"/>, not only those with <see cref="VehicleState"/>.
        /// A Perception entity (SimTransform only) must appear in the grid.
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

            var sys = new SpatialHashSystem();
            sys.Create(repo);

            // Create a Perception entity: has SimTransform but NO VehicleState.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform
            {
                Position = new System.Numerics.Vector3(100f, 100f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });
            // Deliberately NOT adding VehicleState — this is what DEBT-001 tested.

            // Act
            sys.Run();

            // Assert: grid singleton exists and the entity is findable at its position.
            Assert.True(repo.HasSingleton<SpatialGridData>(),
                "SpatialHashSystem must publish a SpatialGridData singleton.");

            var gridData = repo.GetSingleton<SpatialGridData>();

            Span<(Entity foundEntity, Vector2 pos)> results =
                stackalloc (Entity, Vector2)[10];
            int count = gridData.Grid.QueryNeighbors(
                new Vector2(100f, 100f), radius: 1f, results);

            Assert.Equal(1, count);
            Assert.Equal(entity, results[0].foundEntity);

            // Cleanup
            sys.Dispose();
            repo.Dispose();
        }
    }
}
