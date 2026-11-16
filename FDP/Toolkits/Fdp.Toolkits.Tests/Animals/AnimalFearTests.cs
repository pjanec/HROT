using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Tests;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Animals.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-3 (docs/designs/animals/DESIGN_Animals.md §6.2 AB-3, AR-3 / AR-4) — the ONE fear system frightens animals: a tank
    /// driving at a herd frightens it SILENTLY (the flight zone — no sound at all), uneasy → frightened → panic as it comes through the zones;
    /// a busy road beside the field never panics it and it is calm again within its recovery time; a fearless dog is untouched; a shot is
    /// heard by an animal from any side; the replicated band follows the fear. 🔴 Red-proofs: the flight-zone look removed ⇒ the tank rail
    /// red; the heading-at-me test removed ⇒ the road rail red.
    /// </summary>
    public sealed class AnimalFearTests
    {
        private const long CattleType = 700, DogType = 705, HorseType = 703;
        private static readonly AnimalTemperamentDto Cattle = new()
        {
            Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, FleeSpeed = 9f, FleeDistanceM = 200f, RecoverySeconds = 60f,
        };

        private static EntityRepository World()
        {
            var w = PerceptionTestWorldFactory.Create();
            w.RegisterComponent<Fear>();
            w.RegisterComponent<AnimalState>();
            w.RegisterComponent<FlockState>();
            if (!w.IsComponentTypeRegistered<CivilianState>()) w.RegisterComponent<CivilianState>();
            if (!w.IsComponentTypeRegistered<TkbIdentity>()) w.RegisterComponent<TkbIdentity>();
            if (!w.IsComponentTypeRegistered<VehicleParams>()) w.RegisterComponent<VehicleParams>();
            if (!w.IsComponentTypeRegistered<SimVelocity>()) w.RegisterComponent<SimVelocity>();
            if (!w.Bus.IsRegistered<SoundContactEvent>()) w.Bus.Register<SoundContactEvent>();
            if (!w.Bus.IsRegistered<SensorChangedEvent>()) w.Bus.Register<SensorChangedEvent>();
            var db = new TkbDatabase();
            var cow = new TkbTemplate("Cattle", CattleType); cow.AddDescriptor(Cattle); db.Register(cow);
            var dog = new TkbTemplate("Dog", DogType);
            dog.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.Dog, Fearless = true, Territorial = true, FlightZoneM = 30f, PanicZoneM = 8f, RecoverySeconds = 20f });
            db.Register(dog);
            var horse = new TkbTemplate("HorseRiding", HorseType);
            horse.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.HorseRiding, FlightZoneM = 15f, PanicZoneM = 6f, RecoverySeconds = 30f, BoltFear = 0.75f });
            db.Register(horse);
            w.SetSingletonManaged<ITkbDatabase>(db);
            return w;
        }

        private static Entity Animal(EntityRepository w, long type, Vector3 at)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            w.GetSingletonManaged<ITkbDatabase>().TryGetByType(type, out var t);
            new AnimalTkbTranslator().Inject(w, e, t!);
            ref var f = ref w.GetComponentRW<Fear>(e);
            f.NextZoneLookAt = 0;   // look from the first frame
            return e;
        }

        private static Entity Mover(EntityRepository w, VehicleClass cls, Vector3 at, Vector3 v)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new SimVelocity { Linear = v });
            w.AddComponent(e, new VehicleParams { Class = cls });
            return e;
        }

        private static void Move(EntityRepository w, Entity e, float dt)
        {
            var v = w.GetComponentRO<SimVelocity>(e).Linear;
            ref var t = ref w.GetComponentRW<SimTransform>(e);
            t.Position = (Vector3)t.Position + v * dt;
        }

        private static void Frame(EntityRepository w, CivilianFearSystem sys, float dt)
        {
            w.SetSimulationTime(w.SimulationTime + dt);
            w.Bus.SwapBuffers();
            sys.Execute(w, dt);
        }

        private static byte Band(EntityRepository w, Entity e) => w.GetComponentRO<AnimalState>(e).FearBand;

        [Fact]
        public void CE4623_ATankDrivingAtTheHerd_FrightensItSilently_ThroughTheZones_AFearlessDogIsUntouched()
        {
            using var w = World();
            var sys = new CivilianFearSystem();
            var cow = Animal(w, CattleType, Vector3.Zero);
            var dog = Animal(w, DogType, new Vector3(2f, 0f, 0f));
            var tank = Mover(w, VehicleClass.Tank, new Vector3(-80f, 0f, 0f), new Vector3(4f, 0f, 0f));
            const float dt = 0.1f;
            byte outside = 0, beforePanic = 0, inside = 0;
            for (int i = 0; i < 200; i++)
            {
                Move(w, tank, dt);
                Frame(w, sys, dt);
                float d = -((Vector3)w.GetComponentRO<SimTransform>(tank).Position).X;
                if (d > Cattle.FlightZoneM * AnimalFear.VehicleZoneFactor + 1f) outside = Math.Max(outside, Band(w, cow));
                else if (d > Cattle.PanicZoneM + 1f) beforePanic = Math.Max(beforePanic, Band(w, cow));
                else if (d > 0f) inside = Math.Max(inside, Band(w, cow));
            }
            Assert.Equal(0, outside);                       // nothing heard, nothing near: calm
            Assert.Equal(2, beforePanic);                   // the flight zone: frightened, moving off — not yet running
            Assert.Equal(3, inside);                        // the panic zone: it runs
            var fear = w.GetComponentRO<Fear>(cow);
            Assert.Equal(1, fear.Seen);                     // SEEN, not heard
            Assert.Equal(1, fear.HasFrightFrom);
            Assert.Equal(0, Band(w, dog));                  // fearless
        }

        [Fact]
        public void CE4623_ABusyRoadBesideTheField_NeverPanicsTheHerd_AndItIsCalmAgainWithinItsRecovery()
        {
            using var w = World();
            var sys = new CivilianFearSystem();
            var cow = Animal(w, CattleType, new Vector3(0f, 20f, 0f));   // 20 m off the road: inside a car's flight zone (37.5 m)
            const float dt = 0.1f;
            var cars = new System.Collections.Generic.List<Entity>();
            byte worst = 0;
            int frames = (int)(90f / dt);
            for (int i = 0; i < frames; i++)
            {
                if (i % 30 == 0) cars.Add(Mover(w, VehicleClass.PersonalCar, new Vector3(-150f, 0f, 0f), new Vector3(14f, 0f, 0f)));   // a car every 3 s
                foreach (var c in cars) Move(w, c, dt);
                Frame(w, sys, dt);
                worst = Math.Max(worst, Band(w, cow));
            }
            Assert.Equal(1, worst);                         // uneasy — head up — never frightened, never panicked
            foreach (var c in cars) w.DestroyEntity(c);     // the traffic stops
            for (int i = 0; i < (int)(Cattle.RecoverySeconds / dt); i++) Frame(w, sys, dt);
            Assert.Equal(0, Band(w, cow));
            Assert.Equal(0f, w.GetComponentRO<Fear>(cow).Value);
        }

        [Fact]
        public void CE4623_AShotIsHeard_TheSameTableAsACivilian_AndAFearlessDogOnlyFlinchesAtABlast()
        {
            using var w = World();
            var sys = new CivilianFearSystem();
            var cow = Animal(w, CattleType, Vector3.Zero);
            var dog = Animal(w, DogType, Vector3.Zero);
            w.Bus.Publish(new SoundContactEvent { Observer = cow, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SoundContactEvent { Observer = dog, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SoundContactEvent { Observer = cow, X = 5, Y = 0, SourceClass = (byte)SoundSourceClass.Footsteps });
            Frame(w, sys, 0.1f);
            Assert.Equal(CivilianFear.Impulse(SoundSourceClass.SmallArms, 20f), w.GetComponentRO<Fear>(cow).Value, 3);
            Assert.Equal(0f, w.GetComponentRO<Fear>(dog).Value);
            Assert.Equal(new Vector3(20, 0, 0), w.GetComponentRO<Fear>(cow).FrightFrom);
            Assert.False(w.HasComponent<CivilianState>(cow));   // ⛔ never a civilian

            w.Bus.Publish(new SoundContactEvent { Observer = dog, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.Explosion });
            Frame(w, sys, 0.1f);
            Assert.Equal(CivilianFear.Impulse(SoundSourceClass.Explosion, 20f) * AnimalFear.FearlessBlastFactor, w.GetComponentRO<Fear>(dog).Value, 3);
        }

        /// <summary>⭐ <c>CE-4623</c> AB-4 — the HERD: its centre and lead from its members; a panicking member pulls the others' fear up through the
        /// crowd's contagion rule and passes on where the fright came from; the steady frame allocates nothing (R-2084). 🔴 Red-proof: the
        /// contagion skipped ⇒ the second cow stays calm.</summary>
        [Fact]
        public void CE4623_AHerd_HasACentre_CatchesFearFromOneMember_AndAllocatesNothing()
        {
            using var w = World();
            w.RegisterComponent<HerdMember>();
            w.RegisterComponent<HerdState>();
            w.RegisterComponent<AnimalHome>();
            var herd = w.CreateEntity();
            w.AddComponent(herd, new HerdState { Species = AnimalSpecies.Cattle });
            var a = Animal(w, CattleType, new Vector3(0, 0, 0));
            var b = Animal(w, CattleType, new Vector3(10, 0, 0));
            w.AddComponent(a, new HerdMember { Herd = herd });
            w.AddComponent(b, new HerdMember { Herd = herd });
            var herds = new HerdSystem();
            herds.Execute(w, 0.1f);
            Assert.Equal(new Vector3(5, 0, 0), w.GetComponentRO<HerdState>(herd).Centre);
            Assert.Equal(a, w.GetComponentRO<HerdState>(herd).Lead);

            w.GetComponentRW<Fear>(a) = new Fear { Value = 0.95f, FrightFrom = new Vector3(-40, 0, 0), HasFrightFrom = 1 };
            for (int i = 0; i < 15; i++) { w.SetSimulationTime(w.SimulationTime + 0.1f); herds.Execute(w, 0.1f); }   // 1.5 s
            var fb = w.GetComponentRO<Fear>(b);
            Assert.True(fb.Value > 0.75f, $"the cow beside a panicking one panics too: {fb.Value}");
            Assert.True(fb.Value <= Civilians.CrowdRules.ContagionShare * 0.95f + 1e-4f);
            Assert.Equal(new Vector3(-40, 0, 0), fb.FrightFrom);

            for (int i = 0; i < 10; i++) herds.Execute(w, 0.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) herds.Execute(w, 0.1f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ <c>CE-4623</c> AB-4 — after a fright the herd's next grazing spot is picked AWAY from it (the farthest of the candidates),
        /// inside its home: 20 picks from a fright in the west all land east, on average well east; with no fright they centre on the home.
        /// 🔴 Red-proof: the candidates' score ignoring the fright ⇒ the picks scatter both ways ⇒ red.</summary>
        [Fact]
        public void CE4623_AfterAFright_TheNextGrazingSpot_IsAwayFromIt_InsideTheHome()
        {
            using var w = World();
            w.RegisterComponent<HerdMember>();
            w.RegisterComponent<HerdState>();
            w.RegisterComponent<AnimalHome>();
            var herd = w.CreateEntity();
            w.AddComponent(herd, new AnimalHome { Centre = Vector3.Zero, Radius = 60f });
            var lead = Animal(w, CattleType, Vector3.Zero);
            w.GetComponentRW<Fear>(lead) = new Fear { Value = 0.2f, FrightFrom = new Vector3(-80, 0, 0), HasFrightFrom = 1, LastFrightAt = 10.0 };
            var hs = new HerdState { Lead = lead };
            float sumAfraid = 0f;
            for (int i = 0; i < 20; i++)
            {
                var p = HerdSystem.PickGrazing(w, herd, in hs, 10.0 + i * 3.1);
                Assert.True(p.Length() <= 60f, $"inside the home: {p}");
                Assert.True(p.X > 0f, $"pick {i} lands on the far side from the fright: {p}");
                sumAfraid += p.X;
            }
            Assert.True(sumAfraid / 20f > 20f, $"well east on average: {sumAfraid / 20f}");
        }

        /// <summary>⭐ <c>CE-4623</c> AB-5 (AR-10) — a RIDDEN horse BOLTS: past its bolt line it is marked Bolting (replicated) for
        /// <see cref="AnimalFear.BoltSeconds"/>, and the rider's controls are overridden — full gallop, turning away from the fright (here a
        /// rider holding it still and steering right, a shot from the east); after the bolt, with its fear down, he has it again. A cow never
        /// bolts. 🔴 Red-proof: the bolt line ignored ⇒ no Bolting flag ⇒ red.</summary>
        [Fact]
        public void CE4623_ARiddenHorse_BoltsPastItsLine_OverridingTheRider_ForAWhile()
        {
            using var w = World();
            var sys = new CivilianFearSystem();
            var horse = Animal(w, HorseType, Vector3.Zero);
            var cow = Animal(w, CattleType, new Vector3(50, 0, 0));
            w.GetComponentRW<Fear>(horse) = new Fear { Value = 0.8f, FrightFrom = new Vector3(30, 0, 0), HasFrightFrom = 1 };
            w.GetComponentRW<Fear>(cow) = new Fear { Value = 0.95f };
            Frame(w, sys, 0.1f);
            Assert.True((w.GetComponentRO<AnimalState>(horse).Flags & AnimalFlags.Bolting) != 0);
            Assert.True((w.GetComponentRO<AnimalState>(cow).Flags & AnimalFlags.Bolting) == 0);

            var rider = new Fdp.Toolkit.Navigation.ManualDrive { Forward = 0f, Steer = -1f };   // holding it, steering right
            Assert.True(AnimalFear.BoltDrive(w, horse, Vector2.Zero, Vector2.UnitX, ref rider));   // facing the shot (east)
            Assert.Equal(1f, rider.Forward);
            Assert.Equal(1f, rider.Steer);                                // the fright is ahead: it wheels round at full lock
            var calm = new Fdp.Toolkit.Navigation.ManualDrive { Forward = 0.3f, Steer = 0.2f };
            Assert.False(AnimalFear.BoltDrive(w, cow, Vector2.Zero, Vector2.UnitX, ref calm));
            Assert.Equal(0.3f, calm.Forward);

            w.GetComponentRW<Fear>(horse).Value = 0.5f;
            for (int i = 0; i < (int)(AnimalFear.BoltSeconds / 0.1f) + 2; i++) Frame(w, sys, 0.1f);
            Assert.True((w.GetComponentRO<AnimalState>(horse).Flags & AnimalFlags.Bolting) == 0, "the bolt is over");
        }

        /// <summary>⭐ R-2084 — the steady frame of the animal pass allocates nothing (the threat buffer is reused).</summary>
        [Fact]
        public void CE4623_TheAnimalPass_AllocatesNothingInTheSteadyState()
        {
            using var w = World();
            var sys = new CivilianFearSystem();
            for (int i = 0; i < 20; i++) Animal(w, CattleType, new Vector3(i * 3f, 0f, 0f));
            for (int i = 0; i < 20; i++) Mover(w, VehicleClass.Pedestrian, new Vector3(i * 3f, 30f, 0f), new Vector3(0f, -1f, 0f));
            for (int i = 0; i < 30; i++) Frame(w, sys, 0.1f);   // warm: queries built, buffer grown
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) Frame(w, sys, 0.1f);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0, after - before);
        }
    }
}
