using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
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
            w.RegisterComponent<FlockState>();

            var cowType = Type("Cattle", 700, AnimalSpecies.Cattle, 200);
            var cow = w.CreateEntity();
            w.AddComponent(cow, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(cow, new EntityInfo { ForceId = ForceId.Neutral });
            w.AddComponent(cow, new Health { Current = 400f, Max = 400f });
            tr.Inject(w, cow, cowType);
            Assert.True(Animals.IsAnimal(w, cow));
            Assert.Equal(AnimalSpecies.Cattle, w.GetComponentRO<AnimalState>(cow).Species);
            Assert.False(w.HasComponent<FlockState>(cow));

            var flockType = Type("BirdFlock", 707, AnimalSpecies.BirdFlock, 205);
            var flock = w.CreateEntity();
            tr.Inject(w, flock, flockType);
            Assert.Equal(AnimalTkbTranslator.DefaultFlockCount, w.GetComponentRO<FlockState>(flock).Count);

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
