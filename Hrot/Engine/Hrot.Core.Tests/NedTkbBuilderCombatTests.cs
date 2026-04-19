using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.Map.Common.Tests
{
    public class NedTkbBuilderCombatTests
    {
        [Fact]
        public void NedTkbBuilder_WithCombat_AttachesWeaponState()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams);
            var entity = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasComponent<WeaponState>(entity));
            Assert.True(world.TryGetComponent(entity, out WeaponState weapon));
            Assert.Equal(42, weapon.Ammo);
        }

        [Fact]
        public void NedTkbBuilder_WithCombat_AttachesPerceptionReceptor()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams);
            var entity = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasComponent<PerceptionReceptor>(entity));
            Assert.True(world.TryGetComponent(entity, out PerceptionReceptor receptor));
            Assert.Equal(8000f, receptor.VisionRange);
        }

        [Fact]
        public void NedTkbBuilder_WithCombat_AttachesHealth()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams);
            var entity = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasComponent<Health>(entity));
        }

        [Fact]
        public void NedTkbBuilder_WithCombat_KeepsManagedCombatDefinition()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams);
            var entity = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasManagedComponent<SimCombatDef>(entity));
        }

        private static TkbDatabase BuildDatabase()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();

            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<EntityInfo>();
            world.RegisterComponent<VisualData>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterManagedComponent<SimCombatDef>();

            return world;
        }
    }
}
