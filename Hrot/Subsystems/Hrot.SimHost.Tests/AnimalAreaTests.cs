using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Animals;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-9 (docs/designs/animals/DESIGN_Animals.md AR-16) — the ANIMAL AREA populates ONCE: its count of animals of its species, inside
    /// its outline, each a TRANSIENT creation request with a GUID derived from the area's (the same twice), the area marked populated and made the herd;
    /// a second frame, a RESTORED area (it carries the mark) and a REPLAY ask for nothing; a flock area asks for ONE flock of that many birds; an arriving
    /// animal joins its area's herd. 🔴 Red-proofs: the populated mark not checked ⇒ a second frame asks again ⇒ red; the outline ignored ⇒ an animal
    /// outside it ⇒ red.
    /// </summary>
    public sealed class AnimalAreaTests
    {
        private static readonly Guid AreaGuid = new("4623a0a0-0000-4000-8000-000000000001");

        private static (EntityRepository W, Entity Area) World(AnimalSpecies species, ushort count, bool populated = false)
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<AnimalArea>();
            w.RegisterComponent<AnimalAreaPopulated>();
            w.RegisterComponent<HerdState>();
            w.RegisterComponent<HerdMember>();
            w.RegisterComponent<AnimalHome>();
            w.RegisterComponent<AnimalState>();
            w.RegisterComponent<EntityGuid>();
            w.RegisterManagedComponent<EditablePolyline>();
            w.RegisterManagedEvent<EntityCreationRequest>();
            w.SetSingleton(new GlobalTime { TotalTime = 1.0 });
            var area = w.CreateEntity();
            w.AddComponent(area, new SimTransform { Position = new Vector3(100, 0, 0), Rotation = Quaternion.Identity });
            w.AddComponent(area, new AnimalArea { Species = species, Count = count, Seed = 42 });
            w.AddComponent(area, new EntityGuid(AreaGuid));
            w.RegisterComponent<EntityInfo>();
            w.AddComponent(area, new EntityInfo { Name = new FixedString64("Pasture"), ForceId = ForceId.Neutral });
            // a 40 × 20 m field, relative to the area's anchor
            w.SetManagedComponent(area, new EditablePolyline { Points = new[] { new Vector2(-20, -10), new Vector2(20, -10), new Vector2(20, 10), new Vector2(-20, 10) } });
            if (populated) w.AddComponent(area, new AnimalAreaPopulated { At = (SimInstant)0.5 });
            return (w, area);
        }

        private static EntityCreationRequest[] Frame(EntityRepository w, AnimalAreaSystem sys)
        {
            sys.Execute(w, 0.1f);
            w.Bus.SwapBuffers();
            return w.Bus.ReadManaged<EntityCreationRequest>().ToArray();
        }

        [Fact]
        public void CE4623_AnArea_PopulatesOnce_InsideItsOutline_TransientWithDerivedIds_AndBecomesTheHerd()
        {
            var (w, area) = World(AnimalSpecies.Cattle, 6);
            var sys = new AnimalAreaSystem();
            var asked = Frame(w, sys);
            Assert.Equal(6, asked.Length);
            foreach (var r in asked)
            {
                Assert.Equal(TkbEntityTypes.Animal_Cattle, (long)r.TkbType);
                Assert.True(r.IsTransient);
                var at = (Vector3)r.InitialComponents.OfType<SimTransform>().Single().Position;
                Assert.InRange(at.X, 80f, 120f);
                Assert.InRange(at.Y, -10f, 10f);
                var home = r.InitialComponents.OfType<AnimalHome>().Single();
                Assert.Equal(new Vector3(100, 0, 0), (Vector3)home.Centre);
            }
            // ⭐ AB-10 — each named after its area, neutral (the checks and the entity list find them by name)
            Assert.Equal(Enumerable.Range(1, 6).Select(n => $"Pasture {n}"), asked.Select(r => r.InitialComponents.OfType<EntityInfo>().Single().Name.ToString()));
            Assert.All(asked, r => Assert.Equal(ForceId.Neutral, r.InitialComponents.OfType<EntityInfo>().Single().ForceId));
            var ids = asked.Select(r => r.InitialComponents.OfType<EntityGuid>().Single().Value).ToArray();
            Assert.Equal(6, ids.Distinct().Count());
            Assert.True(w.HasComponent<AnimalAreaPopulated>(area));
            Assert.True(w.HasComponent<HerdState>(area));                                   // the area is the herd
            Assert.Empty(Frame(w, sys));                                                    // never again

            var (w2, _) = World(AnimalSpecies.Cattle, 6);                                   // the same area in another run: the same ids
            Assert.Equal(ids, Frame(w2, new AnimalAreaSystem()).Select(r => r.InitialComponents.OfType<EntityGuid>().Single().Value).ToArray());

            // an animal arriving with the area's home joins its herd
            var cow = w.CreateEntity();
            w.AddComponent(cow, new SimTransform { Position = new Vector3(95, 0, 0), Rotation = Quaternion.Identity });
            w.AddComponent(cow, new AnimalState { Species = AnimalSpecies.Cattle });
            w.AddComponent(cow, asked[0].InitialComponents.OfType<AnimalHome>().Single());
            new HerdSystem().Execute(w, 0.1f);
            Assert.Equal(area, w.GetComponentRO<HerdMember>(cow).Herd);
            w.Dispose(); w2.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1958</c> (pigs stuck in their pen's wall) — an area's HOME circle fits INSIDE its outline with a body's room to spare: a 16 m square pen
        /// (farm-under-fire's, its walls 8 m off its centre) gives a home of 8 − 1.5 = 6.5 m, so no grazing spot or flight is ever chosen past a wall.
        /// 🔴 Red-proof: the farthest-vertex radius again ⇒ 11.3 m (a spot in the corner beyond the walls).
        /// </summary>
        [Fact]
        public void CE1958_AnAreasHome_FitsInsideItsOutline()
        {
            var (w, area) = World(AnimalSpecies.Pig, 3);
            w.SetManagedComponent(area, new EditablePolyline { Points = new[] { new Vector2(-8, -8), new Vector2(8, -8), new Vector2(8, 8), new Vector2(-8, 8) } });
            var home = Frame(w, new AnimalAreaSystem())[0].InitialComponents.OfType<AnimalHome>().Single();
            Assert.Equal(8f - AnimalAreaSystem.BodyClearanceM, home.Radius, 3);
            Assert.Equal(home.Radius, w.GetComponentRO<AnimalHome>(area).Radius, 3);   // the herd's own home (its grazing spots) the same
            // the 40 × 20 m field: its narrow side decides (10 m), never its corner (22.4 m)
            var (w2, _) = World(AnimalSpecies.Cattle, 2);
            Assert.Equal(10f - AnimalAreaSystem.BodyClearanceM, Frame(w2, new AnimalAreaSystem())[0].InitialComponents.OfType<AnimalHome>().Single().Radius, 3);
            w.Dispose(); w2.Dispose();
        }

        /// <summary>⭐ <c>CE-4623</c> AB-10 — the PRODUCT registers every animal component: the shared registry the area (every node loads it with a
        /// scenario), the brain's registry the fear, herds and homes. 📐 Found by the demo: only the rails registered them, so in the product every
        /// "is it registered" guard was false — the scenario failed to load (<c>unknown component type name 'AnimalArea'</c>), and fear, herds and
        /// population were silently off. 🔴 Red-proof: drop the brain registry's animal lines ⇒ red.</summary>
        [Fact]
        public void CE4623_TheProductRegistries_RegisterEveryAnimalComponent()
        {
            using var w = new EntityRepository();
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(w);
            Hrot.SimHost.CognitiveComponentRegistry.RegisterAll(w);
            Assert.True(w.IsComponentTypeRegistered<AnimalArea>());
            Assert.True(w.IsComponentTypeRegistered<AnimalAreaPopulated>());
            Assert.True(w.IsComponentTypeRegistered<AnimalState>());
            Assert.True(w.IsComponentTypeRegistered<FlockState>());
            Assert.True(w.IsComponentTypeRegistered<Fear>());
            Assert.True(w.IsComponentTypeRegistered<HerdState>());
            Assert.True(w.IsComponentTypeRegistered<HerdMember>());
            Assert.True(w.IsComponentTypeRegistered<AnimalHome>());
        }

        [Fact]
        public void CE4623_ARestoredArea_AndAReplay_NeverRepopulate_AFlockAreaAsksForOneFlock()
        {
            var (restored, _) = World(AnimalSpecies.Cattle, 6, populated: true);
            Assert.Empty(Frame(restored, new AnimalAreaSystem()));
            restored.Dispose();

            var (replay, _) = World(AnimalSpecies.Cattle, 6);
            Fdp.Toolkit.Replay.ReplayState.Enter(replay);
            Assert.Empty(Frame(replay, new AnimalAreaSystem()));
            replay.Dispose();

            var (birds, _) = World(AnimalSpecies.BirdFlock, 35);
            var one = Assert.Single(Frame(birds, new AnimalAreaSystem()));
            Assert.Equal(TkbEntityTypes.Animal_BirdFlock, (long)one.TkbType);
            Assert.Equal((ushort)35, one.InitialComponents.OfType<FlockState>().Single().Count);
            birds.Dispose();
        }
    }
}
