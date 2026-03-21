using Bagira.Map.Common;
using Bagira.Map.Definitions.Tkb;
using CarKinem.Core;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;

namespace Bagira.Map.Common.Tests
{
    public class BdcTkbBuilderCombatTests
    {
        [Fact]
        public void BdcTkbBuilder_WithCombat_AttachesWeaponState()
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
        public void BdcTkbBuilder_WithCombat_AttachesPerceptionReceptor()
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
        public void BdcTkbBuilder_WithCombat_AttachesHealth()
        {
            using var world = CreateWorld();
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams);
            var entity = world.CreateEntity();

            template.ApplyTo(world, entity);

            Assert.True(world.HasComponent<Health>(entity));
        }

        [Fact]
        public void BdcTkbBuilder_WithCombat_KeepsManagedCombatDefinition()
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
            BdcTkbCatalog.RegisterAll(db);
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
            world.RegisterComponent<Faction>();
            world.RegisterComponent<VisualData>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterManagedComponent<SimCombatDef>();

            return world;
        }
    }
}
