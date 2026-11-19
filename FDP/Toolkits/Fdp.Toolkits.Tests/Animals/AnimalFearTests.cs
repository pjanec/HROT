using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Creatures;
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
        private const long CattleType = 700, DogType = 705, HorseType = 703, BreakerType = 701;
        private static readonly AnimalTemperamentDto Cattle = new()
        {
            Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, FleeSpeed = 9f, FleeDistanceM = 200f, Recovery = 60f,
        };

        private static EntityRepository World()
        {
            var w = PerceptionTestWorldFactory.Create();
            w.RegisterComponent<Fear>();
            w.RegisterComponent<AnimalTasks>();
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
            var breaker = new TkbTemplate("Bull", BreakerType);   // ⭐ CE-4628 — a species that breaks fences when it panics
            breaker.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.Cattle, FlightZoneM = 25f, PanicZoneM = 10f, Breaks = true });
            db.Register(breaker);
            var dog = new TkbTemplate("Dog", DogType);
            dog.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.Dog, Fearless = true, Territorial = true, FlightZoneM = 30f, PanicZoneM = 8f, Recovery = 20f });
            db.Register(dog);
            var horse = new TkbTemplate("HorseRiding", HorseType);
            horse.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.HorseRiding, FlightZoneM = 15f, PanicZoneM = 6f, Recovery = 30f, BoltFear = 0.75f });
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
            w.GetComponentRW<AnimalTasks>(e).NextZoneLookAt = default;   // look from the first frame
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

        private static byte Band(EntityRepository w, Entity e) => AnimalFear.Band(w.GetComponentRO<Fear>(e).Value);   // ⭐ CE-4628 — the band is read off the fear, not stored

        /// <summary>
        /// ⭐ <c>CE-4628</c> rail (d) — <see cref="AnimalFear.PanicBreaks"/> reads the REPLICATED band the fear system writes (§10b — kept: every node can read it,
        /// and it is the pass's snapshot): fear 0.74 ⇒ band 2, no breach; 0.76 ⇒ band 3, a fleeing fence-breaker breaches; a calm one and a species that does not
        /// break fences never do. 🔴 Red-proof: <c>PanicBreaks</c> reading the live fear ⇒ the "band still 2 after the fear rose" assertion fails.
        /// </summary>
        [Fact]
        public void CE4628_PanicBreaks_ReadsTheFearAtBand3sOwnThreshold_074No_076Yes()
        {
            using var w = World();
            var bull = Animal(w, BreakerType, Vector3.Zero);
            var cow = Animal(w, CattleType, Vector3.Zero);   // a species that does not break fences
            w.GetComponentRW<AnimalState>(bull).Mode = AnimalMode.Flee;
            w.GetComponentRW<AnimalState>(cow).Mode = AnimalMode.Flee;
            w.GetComponentRW<AnimalState>(bull).FearBand = AnimalFear.Band(0.74f);
            w.GetComponentRW<AnimalState>(cow).FearBand = AnimalFear.Band(0.9f);
            Assert.False(AnimalFear.PanicBreaks(w, bull));
            w.GetComponentRW<Fear>(bull).Value = 0.9f;                         // the fear rose after the pass: the band is still the pass's
            Assert.False(AnimalFear.PanicBreaks(w, bull));
            w.GetComponentRW<AnimalState>(bull).FearBand = AnimalFear.Band(0.76f);
            Assert.True(AnimalFear.PanicBreaks(w, bull));
            Assert.False(AnimalFear.PanicBreaks(w, cow));                    // it does not break fences, however frightened
            w.GetComponentRW<AnimalState>(bull).Mode = AnimalMode.Calm;
            Assert.False(AnimalFear.PanicBreaks(w, bull));                   // not fleeing
            Assert.Equal(AnimalFear.Band(0.76f), (byte)3);                   // the band's own edge is the threshold
            Assert.Equal(AnimalFear.Band(0.74f), (byte)2);
        }

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
            for (int i = 0; i < (int)(Cattle.Recovery / dt); i++) Frame(w, sys, dt);
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
            Assert.Equal(new Vector3(20, 0, 0), (Vector3)w.GetComponentRO<Fear>(cow).FrightFrom);
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
            w.RegisterComponent<GroupMember>();
            w.RegisterComponent<HerdState>();
            w.RegisterComponent<Group>();
            w.RegisterComponent<Home>();
            var herd = w.CreateEntity();
            w.AddComponent(herd, new HerdState { Species = AnimalSpecies.Cattle });
            w.AddComponent(herd, new Group { Kind = GroupKind.Herd });
            var a = Animal(w, CattleType, new Vector3(0, 0, 0));
            var b = Animal(w, CattleType, new Vector3(10, 0, 0));
            w.AddComponent(a, new GroupMember { Group = herd });
            w.AddComponent(b, new GroupMember { Group = herd });
            var herds = new HerdSystem();
            herds.Execute(w, 0.1f);
            Assert.Equal(new Vector3(5, 0, 0), (Vector3)w.GetComponentRO<Group>(herd).Centre);
            Assert.Equal(a, w.GetComponentRO<Group>(herd).Lead);
            Assert.Equal(2, w.GetComponentRO<Group>(herd).Count);

            w.GetComponentRW<Fear>(a) = new Fear { Value = 0.95f, FrightFrom = new Vector3(-40, 0, 0), HasFrightFrom = 1 };
            for (int i = 0; i < 15; i++) { w.SetSimulationTime(w.SimulationTime + 0.1f); herds.Execute(w, 0.1f); }   // 1.5 s
            var fb = w.GetComponentRO<Fear>(b);
            Assert.True(fb.Value > 0.75f, $"the cow beside a panicking one panics too: {fb.Value}");
            Assert.True(fb.Value <= Civilians.CrowdRules.ContagionShare * 0.95f + 1e-4f);
            Assert.Equal(new Vector3(-40, 0, 0), (Vector3)fb.FrightFrom);

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
            w.RegisterComponent<GroupMember>();
            w.RegisterComponent<HerdState>();
            w.RegisterComponent<Group>();
            w.RegisterComponent<Home>();
            var herd = w.CreateEntity();
            w.AddComponent(herd, new Home { Centre = Vector3.Zero, Radius = 60f });
            var lead = Animal(w, CattleType, Vector3.Zero);
            w.GetComponentRW<Fear>(lead) = new Fear { Value = 0.2f, FrightFrom = new Vector3(-80, 0, 0), HasFrightFrom = 1, LastFrightAt = (SimInstant)10.0 };
            var hs = new HerdState();
            var grp = new Group { Kind = GroupKind.Herd, Lead = lead };
            float sumAfraid = 0f;
            for (int i = 0; i < 20; i++)
            {
                var p = HerdSystem.PickGrazing(w, herd, in hs, in grp, 10.0 + i * 3.1);
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

        /// <summary>⭐⭐ <c>CE-4623</c> AB-8 (AR-7) — the CLOSED-FORM call schedule: a lone cow calls about once per its interval, a herd of 30 calls
        /// about <see cref="AnimalCalls.CallersPerHerd"/> times per interval in all (the per-herd throttle) — not 30; pure (the same numbers twice);
        /// another animal's ALARM frightens an animal and never a civilian. 🔴 Red-proof: the herd stretch removed ⇒ the herd calls ~30× ⇒ red.</summary>
        [Fact]
        public void CE4623_Calls_FollowAClosedFormSchedule_AHerdIsThrottled_AnAlarmFrightensAnimalsOnly()
        {
            const float interval = 45f;
            int lone = 0, herd = 0;
            long last = -1;
            for (double t = 0; t < 900; t += 0.1)
            {
                long k = AnimalCalls.CallDue(7, t, interval, 1);
                if (k >= 0 && k != last) { lone++; last = k; }
            }
            var lastOf = new long[30];
            for (int m = 0; m < 30; m++) lastOf[m] = -1;
            for (double t = 0; t < 900; t += 0.1)
                for (int m = 0; m < 30; m++)
                {
                    long k = AnimalCalls.CallDue((uint)(100 + m), t, interval, 30);
                    if (k >= 0 && k != lastOf[m]) { herd++; lastOf[m] = k; }
                }
            Assert.InRange(lone, 18, 21);                                       // 900 / 45 = 20
            Assert.InRange(herd, 55, 65);                                       // 3 per interval, not 600
            Assert.Equal(AnimalCalls.CallDue(7, 123.4, interval, 1), AnimalCalls.CallDue(7, 123.4, interval, 1));

            Assert.True(AnimalFear.Impulse(Cattle, SoundSourceClass.Animal, 50f) > 0.2f);
            Assert.Equal(0f, Civilians.CivilianFear.Impulse(SoundSourceClass.Animal, 50f));
            Assert.Equal(0f, AnimalFear.Impulse(Cattle, SoundSourceClass.Footsteps, 5f));   // a moo nearby frightens nothing
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
