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
        private const long TankType = 9201, CarType = 9202, CattleType = 9203, FlockType = 9204;   // ⭐ CE-4628 — an animal's species is its TKB type's
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
            _w.RegisterComponent<MineClearing>(); _w.RegisterComponent<EntityInfo>(); _w.RegisterEvent<MinesCleared>(); _w.RegisterEvent<LaneCleared>();   // ⭐ MS-T4 — the muscle
            var db = new TkbDatabase();
            var tank = new TkbTemplate("tank", TankType); tank.AddDescriptor(new VehicleParametersDto { Mass = 60000f }); db.Register(tank);
            var car = new TkbTemplate("car", CarType); car.AddDescriptor(new VehicleParametersDto { Mass = 1000f }); db.Register(car);
            var cattle = new TkbTemplate("cattle", CattleType); cattle.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.Cattle }); db.Register(cattle);
            var flock = new TkbTemplate("flock", FlockType); flock.AddDescriptor(new AnimalTemperamentDto { Species = (byte)AnimalSpecies.BirdFlock }); db.Register(flock);
            var lineCharge = new TkbTemplate("line charge", Minefields.MunitionLineCharge);
            lineCharge.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = 0f, Damage = 100f });
            db.Register(lineCharge);
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
            foreach (var c in _w.Bus.Read<MinesCleared>()) Collect(in c);
            foreach (var l in _w.Bus.Read<LaneCleared>()) _lanes.Add(l);
        }

        private unsafe void Collect(in MinesCleared c) { for (int i = 0; i < c.Count; i++) _cleared.Add((c.Row[i], c.Col[i])); }

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
            Assert.Equal(man, d.TriggeredBy);   // ⭐ CE-4654 D-3 — the burst names the man who set it off (the area effect maims exactly him)
            Assert.Equal(0f, d.HitX, 3); Assert.Equal(0f, d.HitY, 3);
        }

        /// <summary>
        /// ⭐⭐ CE-4655 — a laid field keeps its mines off its marked edge (<see cref="MinePattern.EdgeInset"/>: half a spacing, ≥ 1 m): a man walking ALL FOUR edges just
        /// inside the outline (0.2 m — where a route that skirts a known field runs) sets nothing off, every mine the pattern derives lies ≥ the inset inside, and a man
        /// crossing the middle still strikes. 📐 Before: a 3 m field laid a column 0.5 m inside its east edge and the skirting squad walked onto it (user, `2026-11-18`).
        /// 🔴 Red-proof: drop the edge term from <c>MinePattern.Laid</c> ⇒ strikes on the east edge, and a mine 0.5 m inside.
        /// </summary>
        [Fact]
        public void CE4655_AManWalkingTheFieldsEdge_SetsNothingOff_EveryMineKeepsTheInset_TheMiddleStillStrikes()
        {
            var field = Field(MineType.AntiPersonnel, spacing: 3f);
            var corners = new[] { new Vector2(-19.8f, -19.8f), new Vector2(19.8f, -19.8f), new Vector2(19.8f, 19.8f), new Vector2(-19.8f, 19.8f) };
            var man = Body(LocomotionClass.Pedestrian, corners[0]);
            Frame();
            for (int e = 0; e < 4; e++)
                for (int i = 1; i <= 200; i++) { var p = Vector2.Lerp(corners[e], corners[(e + 1) % 4], i / 200f); MoveTo(man, p.X, p.Y); Frame(); }
            Assert.Empty(_struck);

            var outline = new[] { new Vector2(-20, -20), new Vector2(20, -20), new Vector2(20, 20), new Vector2(-20, 20) };
            var mines = new List<Mine>();
            Assert.True(MinePattern.MinesIn(_w.GetComponentRO<Minefield>(field), Vector2.Zero, outline, mines) > 50);
            foreach (var m in mines) Assert.True(MinePattern.EdgeDistanceSquared(outline, m.At) >= 1.5f * 1.5f - 1e-3f, $"a mine {MathF.Sqrt(MinePattern.EdgeDistanceSquared(outline, m.At)):0.00} m inside the edge");

            MoveTo(man, -3f, 0.1f); Frame();
            for (int i = 1; i <= 60; i++) { MoveTo(man, -3f + i * 0.1f, 0.1f); Frame(); }   // across row 0, mines at x = −3, 0, 3
            Assert.NotEmpty(_struck);
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
            var flock = Body(LocomotionClass.Quadruped, new Vector2(0.1f, 0.1f), tkb: FlockType);
            _w.AddComponent(flock, new AnimalState());
            var air = Body(LocomotionClass.Bird, new Vector2(0.1f, 0.1f));
            Frame(); Frame();
            Assert.Empty(_struck);

            var cow = Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f), tkb: CattleType);
            _w.AddComponent(cow, new AnimalState());
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
                var a = w.Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f), tkb: CattleType);
                w._w.AddComponent(a, new AnimalState());
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
                var a = w.Body(LocomotionClass.Quadruped, new Vector2(-1f, 0f), tkb: CattleType);
                w._w.AddComponent(a, new AnimalState());
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
            // ⭐ CE-4655 — only cells the pattern LAYS (a cell within the edge inset holds no mine); at least two of these are
            var outline = new[] { new Vector2(-20, -20), new Vector2(20, -20), new Vector2(20, 20), new Vector2(-20, 20) };
            int walked = 0;
            foreach (var (r, c) in new[] { (2, 1), (-3, 2), (0, -1), (1, 1), (-1, 0) })
            {
                var p = MinePattern.At(mf, Vector2.Zero, r, c);
                if (!MinePattern.Laid(mf, outline, p)) continue;
                MoveTo(man, p.X + 0.2f, p.Y); Frame();
                Assert.Contains(_struck, x => x.Row == r && x.Col == c);
                walked++;
            }
            Assert.True(walked >= 2, $"only {walked} laid cells walked");
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

        // ── ⭐ MS-T4 — the muscle: a plough / flail clears a swath, a line charge a line, each ONE lane ─────────────────────────────────────────────────────────────────

        private readonly List<(short Row, short Col)> _cleared = new();
        private readonly List<LaneCleared> _lanes = new();

        private Entity Clearer(MineClearingTool tool, Vector2 at, float width = 4.5f, byte active = 1, Vector3 lineTo = default)
        {
            var e = Body(LocomotionClass.Tracked, at, TankType, 7f, 3.5f);
            _w.AddComponent(e, new EntityInfo { ForceId = (ForceId)1 });
            _w.AddComponent(e, new MineClearing { Active = active, Tool = tool, WidthM = width, LineTo = lineTo });
            return e;
        }

        /// <summary>A 40 × 40 m field of anti-personnel mines 1.5 m apart (the swath of a 4.5 m tool holds ~80), a clearer driving x −30 → 30 along y = 0 in half-metre steps, then idling beyond the far edge.</summary>
        private static MineTriggerTests Drive(MineClearingTool tool, out Entity field, out Entity clearer)
        {
            var t = new MineTriggerTests();
            field = t.Field(MineType.AntiPersonnel, spacing: 1.5f);
            t._w.SetComponent(field, new EntityGuid(Guid.Parse("a7000000-0000-4000-8000-0000000000f1")));   // a scenario's field has a STABLE id: the survivors are hashed from it, so they are the same every run
            clearer = t.Clearer(tool, new Vector2(-30f, 0f));
            for (int i = 0; i <= 120; i++) { t.MoveTo(clearer, -30f + i * 0.5f, 0f); t.Frame(); }
            for (int i = 0; i < 5; i++) t.Frame();
            return t;
        }

        private static List<Mine> Swath(MineTriggerTests t, Entity field, Vector2 a, Vector2 b, float half)
        {
            var mf = t._w.GetComponent<Minefield>(field);
            var all = new List<Mine>();
            MinePattern.MinesIn(mf, Vector2.Zero, new[] { new Vector2(-20, -20), new Vector2(20, -20), new Vector2(20, 20), new Vector2(-20, 20) }, all);
            var ab = b - a; float l2 = ab.LengthSquared();
            return all.FindAll(m => { float u = l2 < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(m.At - a, ab) / l2, 0f, 1f); return Vector2.Distance(m.At, a + ab * u) <= half; });
        }

        /// <summary>
        /// ⭐⭐ MR-15, MR-17, MF-9 — a FLAIL across a field: every mine in its 4.5 m swath is cleared except the seeded survivors (a handful — the same set on a second run); each cleared mine bursts where it lay (munition 6051)
        /// and ⛔ NEVER with the flail as the target; no mine is both cleared and struck, the survivors under its hull are what strike; and ONE lane is published — the right segment (entry → exit), width and side.
        /// 🔴 Red-proof: survival chance 1 ⇒ nothing cleared; chance 0 ⇒ no survivors (and the survivor bound fails on a wider chance); drop the burst ⇒ no 6051 at a cleared mine; <c>Target = body</c> ⇒ the flail is hit.
        /// </summary>
        [Fact]
        public void CE4624_AFlailAcrossAField_ClearsTheSwath_ExceptTheSeededSurvivors_BurstsNeverHitTheFlail_OneLane()
        {
            using var t = Drive(MineClearingTool.Flail, out var field, out var flail);
            var swath = Swath(t, field, new Vector2(-20f, 0f), new Vector2(20f, 0f), 2.25f);
            Assert.InRange(swath.Count, 60, 120);
            var cleared = new HashSet<(short, short)>(t._cleared);
            var inSwath = new HashSet<(short, short)>(swath.ConvertAll(m => (m.Row, m.Col)));
            Assert.True(cleared.IsSubsetOf(inSwath), "only mines in the swath are cleared");
            var survivors = new HashSet<(short, short)>(inSwath); survivors.ExceptWith(cleared);
            Assert.InRange(survivors.Count, 0, 8);                                   // ~2 % of ~80
            Assert.True(cleared.Count >= swath.Count - 8);
            var struck = new HashSet<(short, short)>(t._struck.ConvertAll(s => (s.Row, s.Col)));
            Assert.Empty(cleared.Intersect(struck));                                 // a cleared mine never goes off
            Assert.True(struck.IsSubsetOf(survivors), "only survivors can strike the hull");

            Assert.DoesNotContain(t._bursts, b => b.Target == flail || b.Shooter == flail);
            int atCleared = 0;
            foreach (var m in swath) if (cleared.Contains((m.Row, m.Col))) { Assert.Contains(t._bursts, b => b.Ammo == Minefields.MunitionAntiPersonnel && MathF.Abs(b.HitX - m.At.X) < 1e-3f && MathF.Abs(b.HitY - m.At.Y) < 1e-3f); atCleared++; }
            Assert.Equal(cleared.Count, atCleared);

            var lane = Assert.Single(t._lanes);
            Assert.Equal(field, lane.Field); Assert.Equal((byte)255, lane.Lane); Assert.Equal((byte)2, lane.SidePlus1); Assert.Equal(4.5f, lane.Width);
            Assert.InRange(lane.Ax, -21f, -19f); Assert.InRange(lane.Bx, 19f, 21f); Assert.InRange(lane.Ay, -0.1f, 0.1f); Assert.InRange(lane.By, -0.1f, 0.1f);

            // the same every run
            using var again = Drive(MineClearingTool.Flail, out _, out _);
            Assert.Equal(cleared, new HashSet<(short, short)>(again._cleared));

            // a man on a cleared mine: nothing
            var at = swath.Find(m => cleared.Contains((m.Row, m.Col)));
            int before = t._struck.Count;
            var man = t.Body(LocomotionClass.Pedestrian, at.At + new Vector2(-1f, 0f));
            t.Frame(); t.MoveTo(man, at.At.X, at.At.Y); t.Frame(); t.Frame();
            Assert.Equal(before, t._struck.Count);
        }

        /// <summary>
        /// ⭐ MR-15 — a PLOUGH clears silently: its swath is cleared (a few survivors, ~5 %), it turns the mines aside — no burst at a cleared mine (the only bursts are the survivors the hull strikes) — and ONE lane.
        /// 🔴 Red-proof: burst for the plough too ⇒ more bursts than strikes ⇒ red.
        /// </summary>
        [Fact]
        public void CE4624_APloughClearsSilently_FewSurvivors_OneLane()
        {
            using var t = Drive(MineClearingTool.Plough, out var field, out _);
            var swath = Swath(t, field, new Vector2(-20f, 0f), new Vector2(20f, 0f), 2.25f);
            var cleared = new HashSet<(short, short)>(t._cleared);
            Assert.True(cleared.Count >= swath.Count - 14 && cleared.Count > 40, $"{cleared.Count} of {swath.Count}");
            Assert.InRange(swath.Count - cleared.Count, 1, 14);                      // ~5 % of ~80: a seeded handful
            Assert.InRange(t._struck.Count, 1, 14);                                  // the seeded survivors are live mines under the hull: it strikes them (a chance of 0 leaves none)
            Assert.Equal(t._struck.Count, t._bursts.Count);                          // silent: the only bursts are the survivors under the hull
            Assert.Single(t._lanes);
        }

        /// <summary>
        /// ⭐ MF-9, MR-17 — a LINE CHARGE, on its activation EDGE: bursts of munition 6053 every ~8 m from the vehicle toward <c>LineTo</c>, every mine within WidthM / 2 of the line cleared (no survival roll), one lane for the
        /// field; staying active fires nothing more. 🔴 Red-proof: fire every frame while active ⇒ the second frame bursts again ⇒ red; a survival roll ⇒ a mine in the strip is left.
        /// </summary>
        [Fact]
        public void CE4624_ALineCharge_BurstsEvery8m_ClearsTheStrip_OnTheEdgeOnly_OneLane()
        {
            using var t = new MineTriggerTests();
            var field = t.Field(MineType.AntiPersonnel, spacing: 1.5f);
            var charge = t.Clearer(MineClearingTool.LineCharge, new Vector2(-30f, 0f), width: 8f, active: 0, lineTo: new Vector3(60f, 0f, 0f));
            t.Frame(); t.Frame();
            Assert.Empty(t._bursts); Assert.Empty(t._cleared);                       // inactive: nothing
            var mc = t._w.GetComponent<MineClearing>(charge); mc.Active = 1; t._w.SetComponent(charge, mc);
            t.Frame();
            var line = t._bursts.FindAll(b => b.Ammo == Minefields.MunitionLineCharge);
            Assert.Equal(11, line.Count);                                            // 90 m / 8 m
            for (int i = 0; i < line.Count; i++) Assert.Equal(-30f + 8f * (i + 1), line[i].HitX, 3);
            Assert.All(line, b => { Assert.True(b.Target.IsNull); Assert.Equal(0f, b.HitY, 3); });
            var strip = Swath(t, field, new Vector2(-30f, 0f), new Vector2(60f, 0f), 4f);
            Assert.InRange(strip.Count, 100, 200);
            Assert.Equal(new HashSet<(short, short)>(strip.ConvertAll(m => (m.Row, m.Col))), new HashSet<(short, short)>(t._cleared));   // every one, no roll
            var lane = Assert.Single(t._lanes);
            Assert.Equal(field, lane.Field); Assert.Equal((byte)2, lane.SidePlus1); Assert.Equal(8f, lane.Width);
            Assert.Equal((-30f, 0f, 60f, 0f), (lane.Ax, lane.Ay, lane.Bx, lane.By));
            int bursts = t._bursts.Count, lanes = t._lanes.Count;
            for (int i = 0; i < 5; i++) t.Frame();                                   // still active: nothing more
            Assert.Equal(bursts, t._bursts.Count); Assert.Equal(lanes, t._lanes.Count);
        }

        /// <summary>
        /// ⭐ MR-15 — the tool reaches beyond the hull's FRONT: a flail DRIVING INTO a field clears the mines under where its hull is about to be before the hull arrives — the strike pass has already run when the clearing
        /// does, and a 7 m hull covers mines 3.5 m ahead of its centre — so nothing strikes it at the edge (every mine of the row is cleared, none struck). 🔴 Red-proof: lookahead of 3 m from the hull's CENTRE ⇒ the first mine
        /// inside the edge is struck.
        /// </summary>
        [Fact]
        public void CE4624_AFlailDrivingIntoAField_ClearsAheadOfItsHullFront_NothingIsStruckAtTheEdge()
        {
            using var t = new MineTriggerTests();
            var field = t.Field(MineType.AntiPersonnel, spacing: 4f);
            t._w.SetComponent(field, new EntityGuid(Guid.Parse("a7000000-0000-4000-8000-0000000000f2")));
            var flail = t.Clearer(MineClearingTool.Flail, new Vector2(-34f, 0f));
            for (int i = 0; i <= 60; i++) { t.MoveTo(flail, -34f + i * 0.5f, 0f); t.Frame(); }   // x −34 → −4: through the edge at −20 and on
            Assert.Empty(t._struck);
            Assert.True(t._cleared.Count >= 5, $"{t._cleared.Count} cleared");
        }

        /// <summary>
        /// ⭐ R-2084 — 0 B a steady frame with clearers present: an idle plough, an ACTIVE flail standing still in a field whose swath is already cleared (its survivors, if any, are re-rolled by hash, never
        /// allocated), a line charge that has fired. 🔴 Red-proof: allocate in the clearing pass (a <c>new List</c>) ⇒ bytes &gt; 0.
        /// </summary>
        [Fact]
        public void CE4624_SteadyFramesWithClearers_Allocate_ZeroBytes()
        {
            using var t = Drive(MineClearingTool.Flail, out var field, out var flail);   // the swath is cleared; the flail stands beyond the far edge
            t.Clearer(MineClearingTool.Plough, new Vector2(100f, 100f), active: 0);
            var inside = t.Clearer(MineClearingTool.Flail, new Vector2(0f, 0f));         // an active flail standing inside, its swath already cleared by the first
            t.Frame(); t.Frame();
            t._struck.Clear(); t._bursts.Clear(); t._cleared.Clear(); t._lanes.Clear();
            for (int i = 0; i < 30; i++) t._trigger.Execute(t._w, 1f / 60f);              // settle: every mine in its swath is pending or recorded
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 120; f++) t._trigger.Execute(t._w, 1f / 60f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, bytes);
        }
    }
}
