using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T1 (docs/designs/minefields/DESIGN_Minefields.md §3b "As built — MS-T1 trigger"; MF-4, MR-14, MR-16, MR-17) — the mine TRIGGER on a small world: a 40 × 40 m field at the
    /// origin whose lattice (row 0 at y = 0, mines every <c>Spacing</c> m along x, odd rows half a spacing over) the rails walk bodies across; the field's outline is a stand-in
    /// <c>EditablePolyline</c> (the engine's lives in Hrot.Core, which this toolkit's tests cannot reference — the toolkit reads it through <see cref="IAreaOutline"/> by component id).
    /// </summary>
    public sealed class MineTriggerTests : IDisposable
    {
        private const long TankType = 9201, CarType = 9202;
        private readonly EntityRepository _w = new();
        private readonly MineTriggerSystem _trigger = new();
        private readonly List<MineStruck> _struck = new();
        private readonly List<DetonationNotification> _bursts = new();

        public MineTriggerTests()
        {
            _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<PhysicsCollider>(); _w.RegisterComponent<Locomotion>(); _w.RegisterComponent<VehicleParams>();
            _w.RegisterComponent<Minefield>(); _w.RegisterComponent<MinefieldStrikes>(); _w.RegisterComponent<EntityGuid>(); _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<AnimalState>(); _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterManagedComponent<TestAreaOutline>();
            _w.RegisterEvent<MineStruck>(); _w.RegisterEvent<DetonationNotification>();
            var db = new TkbDatabase();
            var tank = new TkbTemplate("tank", TankType); tank.AddDescriptor(new VehicleParametersDto { Mass = 60000f }); db.Register(tank);
            var car = new TkbTemplate("car", CarType); car.AddDescriptor(new VehicleParametersDto { Mass = 1000f }); db.Register(car);
            var mine = new TkbTemplate("anti-tank mine", Minefields.MunitionAntiTank);   // what the catalogue registers (MunitionTkbCatalog): its direct hit on the belly
            mine.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = 120f, Damage = 250f });
            db.Register(mine);
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        private Entity Field(MineType type, float spacing = 4f, float orientationDeg = 0f, uint seed = 0, Vector2 at = default)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(at.X, at.Y, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Minefield { Type = type, SpacingM = spacing, OrientationDeg = orientationDeg, Seed = seed });
            _w.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            _w.SetManagedComponent(e, new TestAreaOutline { Points = new[] { new Vector2(-20, -20), new Vector2(20, -20), new Vector2(20, 20), new Vector2(-20, 20) } });
            return e;
        }

        private Entity Body(LocomotionClass cls, Vector2 at, long tkb = 0, float length = 0f, float width = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(at.X, at.Y, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new PhysicsCollider());
            _w.AddComponent(e, new Locomotion { Class = cls });
            if (length > 0f) _w.AddComponent(e, new VehicleParams { Length = length, Width = width });
            if (tkb != 0) _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)tkb });
            return e;
        }

        private void MoveTo(Entity e, float x, float y) { var t = _w.GetComponent<SimTransform>(e); t.Position = new Vector3(x, y, 0f); _w.SetComponent(e, t); }

        /// <summary>One frame of the trigger; the events it published are collected.</summary>
        private void Frame()
        {
            _trigger.Execute(_w, 1f / 60f);
            _w.Bus.SwapBuffers();
            foreach (var s in _w.Bus.Read<MineStruck>()) _struck.Add(s);
            foreach (var d in _w.Bus.Read<DetonationNotification>()) _bursts.Add(d);
        }

        /// <summary>A man steps on an anti-personnel mine: ONE <see cref="MineStruck"/> naming the field, the mine's lattice cell and the man, and ONE detonation at the mine with munition 6051 and no target.
        /// 🔴 Red-proof: skip the publish of <c>MineStruck</c> / the burst ⇒ red; widen <c>FootRadius</c> to 3 m ⇒ the first frame (2 m off) fires ⇒ red.</summary>
        [Fact]
        public void CE4624_AManOnAnAntiPersonnelMine_OneStrike_OneBurstAtTheMine()
        {
            var field = Field(MineType.AntiPersonnel);
            var man = Body(LocomotionClass.Pedestrian, new Vector2(-2f, 0f));
            Frame();
            Assert.Empty(_struck);                       // two metres from the nearest mine
            MoveTo(man, 0.2f, 0.1f);
            Frame();
            var s = Assert.Single(_struck);
            Assert.Equal(field, s.Field); Assert.Equal(man, s.By); Assert.Equal((short)0, s.Row); Assert.Equal((short)0, s.Col);
            Assert.Equal(0f, s.X, 3); Assert.Equal(0f, s.Y, 3);
            var d = Assert.Single(_bursts);
            Assert.Equal(Minefields.MunitionAntiPersonnel, d.Ammo);
            Assert.True(d.Target.IsNull);
            Assert.Equal(0f, d.HitX, 3); Assert.Equal(0f, d.HitY, 3);
        }

        /// <summary>A man walks over an anti-tank mine: nothing — across the whole field, mine by mine. 🔴 Red-proof: drop the "a man walks over it" test ⇒ he fires them ⇒ red.</summary>
        [Fact]
        public void CE4624_AManWalksOverAntiTankMines_Nothing()
        {
            Field(MineType.AntiTank);
            var man = Body(LocomotionClass.Pedestrian, new Vector2(-12f, 0f));
            for (int i = 0; i <= 120; i++) { MoveTo(man, -12f + i * 0.2f, 0f); Frame(); }   // along row 0: over mines at x = −12, −8 … 12
            Assert.Empty(_struck); Assert.Empty(_bursts);
        }

        /// <summary>
        /// A heavy vehicle over an anti-tank mine: <see cref="MineStruck"/> and a detonation (munition 6052) whose TARGET is the vehicle and whose round travels UP, so the armour model reads
        /// its BOTTOM (MR-10); a light car (1 t) over the same mine sets nothing off. 🔴 Red-proof: send the burst with TravelZ −1 (or 0) ⇒ the face is Top / a side, not the Bottom ⇒ red; drop the mass test ⇒ the car fires ⇒ red.
        /// </summary>
        [Fact]
        public void CE4624_AHeavyVehicleOverAnAntiTankMine_Strikes_TheBurstTargetsItsBottom_ALightCarDoesNot()
        {
            var field = Field(MineType.AntiTank, spacing: 8f);
            var car = Body(LocomotionClass.Wheeled, new Vector2(-4f, 0f), CarType, 4f, 2f);
            for (int i = 0; i < 14; i++) { MoveTo(car, -4f + i * 0.5f, 0f); Frame(); }   // drives over the mine at the origin (and stops short of the next, at x = 8)
            Assert.Empty(_struck);

            var tank = Body(LocomotionClass.Tracked, new Vector2(-4f, 0f), TankType, 7f, 3.5f);
            for (int i = 0; i < 14; i++) { MoveTo(tank, -4f + i * 0.5f, 0f); Frame(); }
            var s = Assert.Single(_struck);
            Assert.Equal(tank, s.By); Assert.Equal(field, s.Field);
            var d = Assert.Single(_bursts);
            Assert.Equal(Minefields.MunitionAntiTank, d.Ammo);
            Assert.Equal(tank, d.Target);
            Assert.True(d.TravelZ > 0f, "the round travels up, into the belly");
            Assert.Equal(Facing.Bottom, ArmorModel.FacingOfArrival(_w.GetComponent<SimTransform>(tank), new Vector3(d.TravelX, d.TravelY, d.TravelZ)));
            Assert.True(d.Penetration > 0f && d.Damage > 0f, "the mine's own numbers (its TKB entry) travel with it");
        }

        /// <summary>
        /// ⭐ MR-14 — the SEGMENT test: a body that crosses a mine in ONE step (from 1.5 m before it to 1.5 m past it — neither end within a foot of it) fires it; and a vehicle whose footprint
        /// passes over an anti-tank mine between two frames fires it. 🔴 Red-proof: test the END point only (<c>from = to</c>) ⇒ both are missed ⇒ red.
        /// </summary>
        [Fact]
        public void CE4624_AFastMoverAcrossAMineInOneStep_Fires_ThePointTestWouldMissIt()
        {
            Field(MineType.AntiPersonnel);
            var man = Body(LocomotionClass.Pedestrian, new Vector2(-1.5f, 0.1f));
            Frame();
            MoveTo(man, 1.5f, 0.1f);       // a 3 m step at high time scale
            Frame();
            var s = Assert.Single(_struck);
            Assert.Equal((short)0, s.Row); Assert.Equal((short)0, s.Col);

            _struck.Clear(); _bursts.Clear();
            var f2 = Field(MineType.AntiTank, spacing: 8f, at: new Vector2(0f, 100f));
            var tank = Body(LocomotionClass.Tracked, new Vector2(-9f, 100f), TankType, 7f, 3.5f);
            Frame();
            MoveTo(tank, 9f, 100f);        // an 18 m step: its footprint swept over the mine at the origin of the second field
            Frame();
            Assert.Contains(_struck, x => x.Field == f2 && x.Row == 0 && x.Col == 0);
        }

        /// <summary>A bird flock never sets a mine off — even one whose locomotion reads as a ground class (the species is the test, MR-16); livestock sets off an AP mine.
        /// 🔴 Red-proof: drop the species test ⇒ the flock fires ⇒ red.</summary>
        [Fact]
        public void CE4624_ABirdFlockNeverStrikes_LivestockStrikesAnAntiPersonnelMine()
        {
            Field(MineType.AntiPersonnel);
            var flock = Body(LocomotionClass.Quadruped, new Vector2(0.1f, 0.1f));
            _w.AddComponent(flock, new AnimalState { Species = AnimalSpecies.BirdFlock });
            var air = Body(LocomotionClass.Bird, new Vector2(0.1f, 0.1f));
            Frame(); Frame();
            Assert.Empty(_struck);

            var cow = Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f));
            _w.AddComponent(cow, new AnimalState { Species = AnimalSpecies.Cattle });
            Frame();
            MoveTo(cow, 0.1f, 0.1f);
            Frame();
            Assert.Equal(cow, Assert.Single(_struck).By);
        }

        /// <summary>Livestock sets off an anti-tank mine only RARELY (5 % per mine-and-animal, a hash — the same every run): of 400 animals walking over the same mine a few fire it, most do not,
        /// and a second run fires exactly the same count. 🔴 Red-proof: chance 1 ⇒ all fire ⇒ red; chance 0 ⇒ none ⇒ red.</summary>
        [Fact]
        public void CE4624_LivestockOnAnAntiTankMine_RarelyAndTheSameEveryRun()
        {
            // the chance is per animal: ask each of 400 animals on its own mine-fresh world is too slow — count by the hash directly through 400 single-animal runs of the real system
            int hits = 0;
            for (int i = 0; i < 400; i++)
            {
                using var w = new MineTriggerTests();
                w.Field(MineType.AntiTank, spacing: 8f, seed: 0);
                // burn entity indices so each animal has its own index (the hash is of the animal's index)
                for (int k = 0; k < i; k++) w._w.CreateEntity();
                var a = w.Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f));
                w._w.AddComponent(a, new AnimalState { Species = AnimalSpecies.Cattle });
                w.Frame(); w.MoveTo(a, 0.1f, 0f); w.Frame();
                if (w._struck.Count > 0) hits++;
            }
            Assert.InRange(hits, 4, 45);                 // 5 % of 400 = 20
            int again = 0;
            for (int i = 0; i < 400; i++)
            {
                using var w = new MineTriggerTests();
                w.Field(MineType.AntiTank, spacing: 8f, seed: 0);
                for (int k = 0; k < i; k++) w._w.CreateEntity();
                var a = w.Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f));
                w._w.AddComponent(a, new AnimalState { Species = AnimalSpecies.Cattle });
                w.Frame(); w.MoveTo(a, 0.1f, 0f); w.Frame();
                if (w._struck.Count > 0) again++;
            }
            Assert.Equal(hits, again);
        }

        /// <summary>A struck mine never goes off twice: the man standing on it frame after frame fires it once; two bodies on one mine in one frame fire it once; and a mine already in a strikes
        /// record (another node's) is gone for a fresh trigger. 🔴 Red-proof: drop the pending / fired / record checks ⇒ a second strike ⇒ red.</summary>
        [Fact]
        public void CE4624_AStruckMine_NeverGoesOffTwice()
        {
            var field = Field(MineType.AntiPersonnel);
            var a = Body(LocomotionClass.Pedestrian, new Vector2(-1f, 0f));
            var b = Body(LocomotionClass.Pedestrian, new Vector2(-1f, 0.2f));
            Frame();
            MoveTo(a, 0.1f, 0f); MoveTo(b, -0.1f, 0.1f);
            Frame();
            Assert.Single(_struck);                      // two bodies, one mine, one strike
            for (int i = 0; i < 30; i++) Frame();        // both stand on it
            Assert.Single(_struck); Assert.Single(_bursts);

            // the record of another node: this mine is in the field's strikes
            _struck.Clear(); _bursts.Clear();
            var rec = _w.CreateEntity();
            var strikes = new MinefieldStrikes { Field = _w.GetComponent<EntityGuid>(field).Value, Count = 1 };
            strikes.Count = 2;
            strikes.Set(0, 0, 0, MineGone.Struck);       // the one the two men stand on, at (0, 0) — the fresh trigger has no memory of it
            strikes.Set(1, 1, 0, MineGone.Struck);       // row 1, col 0: at (2, 4)
            _w.AddComponent(rec, strikes);
            var fresh = new MineTriggerSystem();
            var c = Body(LocomotionClass.Pedestrian, new Vector2(2f, 3f));
            fresh.Execute(_w, 0.016f);
            MoveTo(c, 2f, 4f);
            fresh.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            Assert.Empty(_w.Bus.Read<MineStruck>().ToArray());
        }

        /// <summary>A node that does not own the body's position does nothing (its owner decides). 🔴 Red-proof: drop the <c>OwnsPosition</c> test ⇒ the replica fires ⇒ red.</summary>
        [Fact]
        public void CE4624_ABodyThisNodeDoesNotOwn_NothingFires()
        {
            Field(MineType.AntiPersonnel);
            var theirs = Body(LocomotionClass.Pedestrian, new Vector2(-1f, 0f));
            _w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 7, localNodeId: 1));
            Frame();
            MoveTo(theirs, 0.1f, 0.1f);
            Frame();
            Assert.Empty(_struck);
            var mine = Body(LocomotionClass.Pedestrian, new Vector2(-1f, 0f));
            _w.AddComponent(mine, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            Frame();
            MoveTo(mine, 0.1f, 0.1f);
            Frame();
            Assert.Equal(mine, Assert.Single(_struck).By);
        }

        /// <summary>A field TURNED by its orientation and with a seed (jitter) still fires on the exact mine the pattern derives: walk onto <c>MinePattern.At</c> of a few cells.
        /// 🔴 Red-proof: ignore the orientation in the lattice walk ⇒ the candidate cells are the wrong ones ⇒ red.</summary>
        [Fact]
        public void CE4624_ATurnedJitteredField_FiresOnTheMinesThePatternDerives()
        {
            var field = Field(MineType.AntiPersonnel, spacing: 5f, orientationDeg: 33f, seed: 7u);
            var mf = _w.GetComponent<Minefield>(field);
            var man = Body(LocomotionClass.Pedestrian, new Vector2(30f, 30f));
            Frame();
            foreach (var (r, c) in new[] { (2, 1), (-3, 2), (0, -1) })
            {
                var p = MinePattern.At(mf, Vector2.Zero, r, c);
                MoveTo(man, p.X + 0.2f, p.Y); Frame();
                Assert.Contains(_struck, x => x.Row == r && x.Col == c);
            }
        }

        /// <summary>
        /// ⭐ R-2084 — 0 B a steady frame with bodies INSIDE a field: men walking between the rows, one standing, a light car sitting on an anti-tank mine (it does not fire; its mass is cached), a
        /// bird flock overhead. 🔴 Red-proof: allocate in the per-body path (a <c>new List</c>) ⇒ bytes &gt; 0 ⇒ red.
        /// </summary>
        [Fact]
        public void CE4624_SteadyFramesWithBodiesInsideAField_Allocate_ZeroBytes()
        {
            Field(MineType.Mixed);
            var walkers = new Entity[6];
            for (int i = 0; i < walkers.Length; i++) walkers[i] = Body(LocomotionClass.Pedestrian, new Vector2(-15f + i, 2f));
            var car = Body(LocomotionClass.Wheeled, new Vector2(4f, 4f), CarType, 4f, 2f);   // sits on an AT mine (row 1, col 0 is at (2, 4); this one is at x = 4: row 1 col 1 → (6, 4))
            var flock = Body(LocomotionClass.Bird, new Vector2(0f, 0f));
            for (int f = 0; f < 10; f++) { for (int i = 0; i < walkers.Length; i++) MoveTo(walkers[i], -15f + i + f * 0.05f, 2f); Frame(); }
            _struck.Clear(); _bursts.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 120; f++)
            {
                for (int i = 0; i < walkers.Length; i++) MoveTo(walkers[i], -15f + i + 0.5f + (f % 20) * 0.1f, 2f + (i & 1) * 4f);   // between the rows (mines at y = 0, 4, 8; x offsets keep ≥ 1.5 m)
                _trigger.Execute(_w, 1f / 60f);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            _w.Bus.SwapBuffers();
            Assert.Empty(_w.Bus.Read<MineStruck>().ToArray());
            Assert.Equal(0L, bytes);
        }
    }
}
