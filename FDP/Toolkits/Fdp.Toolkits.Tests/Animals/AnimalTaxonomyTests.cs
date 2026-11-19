using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Creatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Animals.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-1 (docs/designs/animals/DESIGN_Animals.md §6.2, AR-6 / AR-19) — an animal type gets its <see cref="AnimalState"/> (a flock
    /// its <see cref="FlockState"/>) from the ONE translator, <see cref="Animals.IsAnimal"/> asks that one component, an animal is hit as a whole body
    /// (never a man's zones), and a NEUTRAL animal is no collateral veto (a herd near the target never stops an air weapon) while a neutral PERSON
    /// still is. 🔴 Red-proof: drop the animal skip in <c>HiddenTargetThrow.FriendlyInBurst</c> ⇒ red.
    /// </summary>
    public sealed class AnimalTaxonomyTests
    {
        private static TkbTemplate Type(string name, long type, AnimalSpecies species, byte category)
        {
            var t = new TkbTemplate(name, type) { DisType = new DISEntityType { Kind = 3, Domain = 1, Category = category, Subcategory = 1 } };
            t.AddDescriptor(new AnimalTemperamentDto { Species = (byte)species, FlightZoneM = 25f });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 400f });
            return t;
        }

        /// <summary>
        /// ⭐ <c>CE-4628</c> rail (b) — an animal spawned by <see cref="AnimalTkbTranslator"/> carries its WHOLE mind from the first frame, each part with its
        /// final value (no add-then-write inside the parallel brain phase): the shared <c>Fear</c> (calm), its <see cref="AnimalTasks"/> (no intruder / handler /
        /// quarry, the flight-zone look staggered by its entity index) beside its <see cref="AnimalState"/>. A flock is no different. 🔴 Red-proof: either add
        /// removed from the translator ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_AnAnimalSpawnedByTheTranslator_HasFearAndAnimalTasks_WithTheirFinalValues()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<AnimalState>();
            w.RegisterComponent<Fear>();
            w.RegisterComponent<AnimalTasks>();
            w.RegisterComponent<Home>();
            w.RegisterComponent<FlockState>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationStatus>();
            var tr = new AnimalTkbTranslator();
            Assert.Contains(typeof(Home), tr.GetProducedComponents());
            Assert.Contains(typeof(AnimalTasks), tr.GetProducedComponents());
            Assert.Contains(typeof(Fear), tr.GetProducedComponents());
            Assert.Contains(typeof(Group), tr.GetProducedComponents());
            foreach (var type in new[] { Type("Cattle", 700, AnimalSpecies.Cattle, 200), Type("BirdFlock", 707, AnimalSpecies.BirdFlock, 205) })
            {
                var a = w.CreateEntity();
                w.AddComponent(a, new SimTransform { Position = new Vector3(7f, 8f, 0f), Rotation = Quaternion.Identity });
                tr.Inject(w, a, type);
                Assert.True(w.HasComponent<AnimalState>(a));
                var home = w.GetComponentRO<Home>(a);   // the spawn home: where it stands, no radius, no tether
                Assert.Equal((new Vector3(7f, 8f, 0f), 0f, 0f, 0), ((Vector3)home.Centre, home.Radius, home.TetherMetres, (int)home.Enclosed));
                Assert.True(home.Area.IsNull);
                Assert.Equal(default(Fear), w.GetComponentRO<Fear>(a));
                var tasks = w.GetComponentRO<AnimalTasks>(a);
                Assert.True(tasks.Intruder.IsNull && tasks.Handler.IsNull && tasks.Quarry.IsNull);
                Assert.Equal((a.Index % 10) * (AnimalFear.ZoneLookSeconds / 10.0), (double)tasks.NextZoneLookAt, 6);
                Assert.Equal(0.0, (double)tasks.BoltUntil);
            }
        }

        /// <summary>⭐ <c>CE-4628</c> — an AREA animal's own home (set from the creation request's InitialComponents after Inject) wins over the spawn home.</summary>
        [Fact]
        public void CE4628_AnAreaAnimalsHome_WinsOverTheSpawnHome()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<AnimalState>(); w.RegisterComponent<Home>();
            var a = w.CreateEntity();
            new AnimalTkbTranslator().Inject(w, a, Type("Cattle", 700, AnimalSpecies.Cattle, 200));
            Assert.Equal(0f, w.GetComponentRO<Home>(a).Radius);
            w.SetComponent(a, new Home { Centre = new Vector3(100, 0, 0), Radius = 22f });   // what NetworkSpawningSystem does with InitialComponents
            var h = w.GetComponentRO<Home>(a);
            Assert.Equal((new Vector3(100, 0, 0), 22f), ((Vector3)h.Centre, h.Radius));
        }

        /// <summary>
        /// ⭐ <c>CE-4628</c> rail (c) — <see cref="AnimalSpeciesOf"/> answers the species of an entity's TKB TYPE (the temperament descriptor's), false for an
        /// entity with no type or a type that is no animal's, and the SECOND ask for a type reads a per-type table: it allocates nothing (R-2084).
        /// 🔴 Red-proof: the table lookup replaced by a fresh descriptor walk allocating, or the species read off the wrong field ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_AnimalSpeciesOf_ReturnsTheTkbTypesSpecies_AndAllocatesNothingOnTheSecondCall()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<TkbIdentity>();
            var db = new TkbDatabase();
            db.Register(Type("Cattle", 700, AnimalSpecies.Cattle, 200));
            db.Register(Type("Duck", 713, AnimalSpecies.Duck, 205));
            db.Register(new TkbTemplate("Rifleman", 9000));   // no animal descriptor
            w.SetSingletonManaged<ITkbDatabase>(db);
            Entity Typed(long type) { var e = w.CreateEntity(); w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type }); return e; }
            var cow = Typed(700); var duck = Typed(713); var man = Typed(9000); var untyped = w.CreateEntity(); var unknownType = Typed(123456);

            Assert.True(AnimalSpeciesOf.TryOf(w, cow, out var s1)); Assert.Equal(AnimalSpecies.Cattle, s1);
            Assert.True(AnimalSpeciesOf.TryOf(w, duck, out var s2)); Assert.Equal(AnimalSpecies.Duck, s2);
            Assert.False(AnimalSpeciesOf.TryOf(w, man, out _));
            Assert.False(AnimalSpeciesOf.TryOf(w, untyped, out _));
            Assert.False(AnimalSpeciesOf.TryOf(w, unknownType, out _));
            Assert.True(AnimalSpeciesOf.TryOf((ISimulationView)w, db, cow, out var s3)); Assert.Equal(AnimalSpecies.Cattle, s3);   // the view overload, the database handed in

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                AnimalSpeciesOf.TryOf(w, cow, out _); AnimalSpeciesOf.TryOf(w, duck, out _); AnimalSpeciesOf.TryOf(w, man, out _);
                AnimalSpeciesOf.TryOf((ISimulationView)w, db, cow, out _);
            }
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);

            // two databases may give one type id two meanings: the table is per database, never a global one
            var other = new TkbDatabase();
            other.Register(Type("Sheep", 700, AnimalSpecies.Sheep, 200));
            Assert.True(AnimalSpeciesOf.TryOf((ISimulationView)w, other, cow, out var s4)); Assert.Equal(AnimalSpecies.Sheep, s4);
        }

        [Fact]
        public void CE4623_AnAnimal_IsAnAnimalByItsState_AWholeBody_AndNoCollateralVeto()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<EntityInfo>();
            w.RegisterComponent<Health>();
            var tr = new AnimalTkbTranslator();
            foreach (var t in tr.GetProducedComponents()) { }
            w.RegisterComponent<AnimalState>();
            w.RegisterComponent<Fear>();
            w.RegisterComponent<AnimalTasks>();
            w.RegisterComponent<FlockState>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
            w.RegisterComponent<Fdp.Toolkit.Navigation.NavigationStatus>();

            var cowType = Type("Cattle", 700, AnimalSpecies.Cattle, 200);
            var cow = w.CreateEntity();
            w.AddComponent(cow, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(cow, new EntityInfo { ForceId = ForceId.Neutral });
            w.AddComponent(cow, new Health { Current = 400f, Max = 400f });
            tr.Inject(w, cow, cowType);
            Assert.True(Animals.IsAnimal(w, cow));
            Assert.Equal(AnimalMode.Calm, w.GetComponentRO<AnimalState>(cow).Mode);   // ⭐ CE-4628 — its species is no state: the TKB type's (AnimalSpeciesOf)
            Assert.False(w.HasComponent<FlockState>(cow));

            var flockType = Type("BirdFlock", 707, AnimalSpecies.BirdFlock, 205);
            var flock = w.CreateEntity();
            tr.Inject(w, flock, flockType);
            Assert.Equal(AnimalTkbTranslator.DefaultFlockCount, w.GetComponentRO<FlockState>(flock).Count);
            // ⭐ AB-10 — a bird has no ground (vehicle) translator, so the animal translator gives it the navigation contract its MoveTo writes;
            //   🔴 red-proof: drop it ⇒ red (the farm demo's flock threw "missing NavigationIntent" on its first flight)
            Assert.True(w.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(flock));
            Assert.True(w.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(flock));

            Assert.Null(Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(cowType));                  // a whole body, never a man's zones
            Assert.True(Animals.IsAnimalDis(cowType.DisType));
            Assert.False(Animals.IsAnimalDis(new DISEntityType { Kind = 3, Domain = 1, Category = 2 }));   // a civilian

            var pilot = w.CreateEntity();
            w.AddComponent(pilot, new EntityInfo { ForceId = ForceId.Friend });
            w.AddComponent(pilot, new SimTransform { Position = new Vector3(5000f, 0f, 0f), Rotation = Quaternion.Identity });
            Assert.False(HiddenTargetThrow.FriendlyInBurst(w, pilot, Vector3.Zero, 30f, neutralsToo: true), "a cow is no collateral veto");

            var farmer = w.CreateEntity();
            w.AddComponent(farmer, new EntityInfo { ForceId = ForceId.Neutral });
            w.AddComponent(farmer, new SimTransform { Position = new Vector3(-8f, 0f, 0f), Rotation = Quaternion.Identity });
            Assert.True(HiddenTargetThrow.FriendlyInBurst(w, pilot, Vector3.Zero, 30f, neutralsToo: true), "a neutral PERSON still is");
            Assert.False(Animals.IsAnimal(w, farmer));
        }
    }
}
