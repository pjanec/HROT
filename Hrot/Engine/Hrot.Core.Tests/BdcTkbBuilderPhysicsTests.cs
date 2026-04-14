using System;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using CarKinem.Core;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Tests for BD1-P3T1: NedTkbBuilder.WithPhysics must add a <see cref="PhysicsCollider"/>
    /// so the entity appears in <c>SpatialHashSystem</c>'s broadphase grid.
    /// </summary>
    public class NedTkbBuilderPhysicsTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TkbDatabase BuildDatabase() =>
            BuildDatabase(length: 6f, width: 2.5f);

        private static TkbDatabase BuildDatabase(float length, float width)
        {
            var db = new TkbDatabase();
            new NedTkbBuilder(db)
                .DefineVehicle(TestTkbId, "TestVehicle")
                .WithPhysics(TestTkbId, def =>
                {
                    def.Length = length;
                    def.Width  = width;
                });
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<PhysicsCollider>();
            return world;
        }

        private const long TestTkbId = 9901L;

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>BD1-P3T1 SC1: WithPhysics must add a PhysicsCollider component.</summary>
        [Fact]
        public void WithPhysics_AddsPhysicsCollider()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TestTkbId);
            var entity   = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasComponent<PhysicsCollider>(entity));
        }

        /// <summary>BD1-P3T1 SC2: Radius must equal Math.Max(Length, Width) / 2f.</summary>
        [Fact]
        public void WithPhysics_ColliderRadiusIsMaxDimension()
        {
            using var world = CreateWorld();
            var template = BuildDatabase(length: 6f, width: 2.5f).GetByType(TestTkbId);
            var entity   = world.CreateEntity();

            template.ApplyTo(world, entity);

            world.TryGetComponent(entity, out PhysicsCollider collider);
            // Max(6, 2.5) / 2 = 3.0
            Assert.Equal(3f, collider.Radius);
        }

        /// <summary>BD1-P3T1 SC2 (width dominant): Radius uses Width when Width > Length.</summary>
        [Fact]
        public void WithPhysics_ColliderRadius_UsesLargerDimension()
        {
            using var world = CreateWorld();
            var template = BuildDatabase(length: 3f, width: 5f).GetByType(TestTkbId);
            var entity   = world.CreateEntity();

            template.ApplyTo(world, entity);

            world.TryGetComponent(entity, out PhysicsCollider collider);
            // Max(3, 5) / 2 = 2.5
            Assert.Equal(2.5f, collider.Radius);
        }

        /// <summary>BD1-P3T1 SC1: CollisionLayer must equal PhysicsConstants.EntityCollisionLayer.</summary>
        [Fact]
        public void WithPhysics_ColliderUsesEntityCollisionLayer()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TestTkbId);
            var entity   = world.CreateEntity();

            template.ApplyTo(world, entity);

            world.TryGetComponent(entity, out PhysicsCollider collider);
            Assert.Equal(PhysicsConstants.EntityCollisionLayer, collider.CollisionLayer);
        }
    }
}
